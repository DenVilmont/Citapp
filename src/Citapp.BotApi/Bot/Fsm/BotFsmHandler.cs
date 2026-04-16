using System.Globalization;
using System.Text.Json;
using Citapp.BotApi.Infrastructure.Repositories;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;

namespace Citapp.BotApi.Services;

public class BotFsmHandler
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(24);
    private readonly ConversationStateRepository _states;
    private readonly TenantRepository _tenants;
    private readonly ServiceRepository _services;
    private readonly BookingRepository _bookings;
    private readonly SlotCalculationService _slots;
    private readonly BookingTransactionService _bookingTransactions;
    private readonly WhatsAppMessageSender _sender;
    private readonly ILogger<BotFsmHandler> _logger;

    public BotFsmHandler(
        ConversationStateRepository states,
        TenantRepository tenants,
        ServiceRepository services,
        BookingRepository bookings,
        SlotCalculationService slots,
        BookingTransactionService bookingTransactions,
        WhatsAppMessageSender sender,
        ILogger<BotFsmHandler> logger)
    {
        _states = states;
        _tenants = tenants;
        _services = services;
        _bookings = bookings;
        _slots = slots;
        _bookingTransactions = bookingTransactions;
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId, DateTimeOffset customerLastSeenAt, string messageType, string? interactiveType, string? payloadId, string? textBody)
    {
        using var _ = _sender.BeginOutboundScope(tenantId, customerId, waUserId, customerLastSeenAt);
        var tenant = await _tenants.GetBotSettingsAsync(tenantId);
        if (tenant is null)
        {
            return;
        }

        var currentState = await LoadStateAsync(tenantId, customerId);
        if (!tenant.BookingEnabled)
        {
            var unavailableMessage = string.IsNullOrWhiteSpace(tenant.AboutText)
                ? "Онлайн-запись сейчас недоступна."
                : tenant.AboutText;
            await _sender.SendTextAsync(waUserId, phoneNumberId, unavailableMessage);
            await _states.DeleteAsync(tenantId, customerId);
            return;
        }

        var input = ParseInput(messageType, interactiveType, payloadId, textBody);
        if (ShouldResetToMainMenu(input))
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (currentState.State == BotState.Idle)
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        switch (currentState.State)
        {
            case BotState.MainMenu:
                await HandleMainMenuAsync(input, tenant, currentState, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.BookingSelectService:
                await HandleSelectServiceAsync(input, currentState, tenant, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.PortfolioView:
                await HandlePortfolioStateAsync(input, currentState, tenant, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.BookingSelectDate:
                await HandleSelectDateAsync(input, currentState, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.BookingSelectTime:
                await HandleSelectTimeAsync(input, currentState, tenant, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.BookingConfirm:
                await HandleBookingConfirmAsync(input, currentState, tenant, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.CancelSelectBooking:
                await HandleCancelSelectBookingAsync(input, currentState, tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.CancelConfirm:
                await HandleCancelConfirmAsync(input, currentState, tenantId, customerId, waUserId, phoneNumberId);
                break;
            default:
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
                break;
        }
    }

    private async Task HandleMainMenuAsync(UserInput input, TenantBotSettings tenant, ConversationSnapshot currentState, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId, tenant);
            return;
        }

        if (input.PayloadId == "menu_book")
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "menu_manage")
        {
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
    }

    private async Task HandleSelectServiceAsync(UserInput input, ConversationSnapshot currentState, TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId, tenant);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId.StartsWith("svc:", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(input.PayloadId[4..], out var selectedId))
            {
                await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
                return;
            }

            await SendServiceChoiceMenuAsync(tenantId, customerId, selectedId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId.StartsWith("svc_book:", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(input.PayloadId["svc_book:".Length..], out var serviceId))
            {
                await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
                return;
            }

            await StartBookingFlowAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId.StartsWith("svc_photo:", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(input.PayloadId["svc_photo:".Length..], out var serviceId))
            {
                await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
                return;
            }

            await SendServicePhotoAndContinueAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
            return;
        }

        await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
    }

    private async Task HandlePortfolioStateAsync(UserInput input, ConversationSnapshot currentState, TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var payload = Deserialize(currentState.Payload);
        if (payload.ServiceId is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId, tenant);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == $"svc_book:{payload.ServiceId.Value}")
        {
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId, payload.ExistingBookingId);
            return;
        }

        await SendServicePhotoAndContinueAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId);
    }

    private async Task HandleSelectDateAsync(UserInput input, ConversationSnapshot currentState, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var payload = Deserialize(currentState.Payload);
        if (payload.ServiceId is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        if (input.PayloadId is null || !input.PayloadId.StartsWith("date:", StringComparison.Ordinal))
        {
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId, payload.ExistingBookingId);
            return;
        }

        if (!DateOnly.TryParseExact(input.PayloadId[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId, payload.ExistingBookingId);
            return;
        }

        await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, date, waUserId, phoneNumberId);
    }

    private async Task HandleSelectTimeAsync(UserInput input, ConversationSnapshot currentState, TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var payload = Deserialize(currentState.Payload);
        if (payload.ServiceId is null || payload.Date is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId is null || !input.PayloadId.StartsWith("time:", StringComparison.Ordinal))
        {
            if (input.PayloadId == "main_menu")
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
                return;
            }

            await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, payload.Date.Value, waUserId, phoneNumberId);
            return;
        }

        if (!DateTimeOffset.TryParse(input.PayloadId[5..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startAt))
        {
            await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, payload.Date.Value, waUserId, phoneNumberId);
            return;
        }

        var service = await _services.GetSnapshotAsync(tenantId, payload.ServiceId.Value);
        if (service is null || !service.IsActive)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        var endAt = startAt.ToUniversalTime().AddMinutes(service.DurationMinutes);
        var confirmPayload = payload with { StartAt = startAt.ToUniversalTime(), EndAt = endAt };

        var tenantTimezone = ResolveTimeZone(tenant.Timezone);
        var localStart = TimeZoneInfo.ConvertTime(startAt, tenantTimezone);
        var localEnd = localStart.AddMinutes(service.DurationMinutes);

        var sendResult = await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            $"Подтвердить запись: {localStart:dd.MM}, {localStart:HH:mm} — {localEnd:HH:mm}?",
            [
                ("book_yes", "Подтвердить"),
                ("book_no", "Назад"),
                ("main_menu", "В меню")
            ]);

        await SaveStateIfSentAsync(
            sendResult,
            tenantId,
            customerId,
            BotState.BookingConfirm,
            confirmPayload,
            currentState.State,
            "booking_time_to_confirm");
    }

    private async Task HandleBookingConfirmAsync(UserInput input, ConversationSnapshot currentState, TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var payload = Deserialize(currentState.Payload);
        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId, tenant);
            return;
        }

        if (payload.DuplicatePrompt)
        {
            await HandleDuplicateDecisionAsync(input, payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (payload.ServiceId is null || payload.Date is null || payload.StartAt is null || payload.EndAt is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "book_no")
        {
            await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, payload.Date.Value, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId != "book_yes")
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId, tenant);
            return;
        }

        var create = new CreateBookingDto(
            customerId,
            payload.ServiceId.Value,
            BookingSource.Chat,
            payload.Date.Value,
            payload.StartAt.Value,
            payload.EndAt.Value,
            0,
            0,
            "");

        try
        {
            var booking = await _bookingTransactions.CreateAsync(tenantId, create, null, payload.ExistingBookingId);

            var service = await _services.GetSnapshotAsync(tenantId, booking.ServiceId);
            var bookingTimezone = ResolveTimeZone(tenant.Timezone);
            var localStart = TimeZoneInfo.ConvertTime(booking.StartAt, bookingTimezone);
            var serviceDuration = service?.DurationMinutes ?? booking.DurationSnapshotMinutes;
            var localEnd = localStart.AddMinutes(serviceDuration);
            var serviceName = await ResolveServiceNameAsync(tenantId, booking.ServiceId);
            var amount = booking.PriceSnapshotAmount.ToString("0.##", CultureInfo.InvariantCulture);
            var currency = string.IsNullOrWhiteSpace(booking.CurrencySnapshot) ? "RUB" : booking.CurrencySnapshot;
            await _sender.SendTextAsync(
                waUserId,
                phoneNumberId,
                $"Готово ✅ {serviceName}\n{localStart:dd.MM.yyyy}, {localStart:HH:mm} — {localEnd:HH:mm}\n{amount} {currency}\nМастер/салон: {tenant.BusinessName}");
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
        }
        catch (BookingConflictException)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Это время уже заняли. Выберите другой слот.");
            await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, payload.Date.Value, waUserId, phoneNumberId);
        }
    }

    private async Task HandleCancelSelectBookingAsync(UserInput input, ConversationSnapshot currentState, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId is null || !input.PayloadId.StartsWith("cancel:", StringComparison.Ordinal))
        {
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!Guid.TryParse(input.PayloadId[7..], out var bookingId))
        {
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        var sendResult = await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            "Отменить эту запись?",
            [
                ("cancel_yes", "Да"),
                ("cancel_no", "Нет")
            ]);

        await SaveStateIfSentAsync(
            sendResult,
            tenantId,
            customerId,
            BotState.CancelConfirm,
            new FsmPayload { BookingId = bookingId },
            currentState.State,
            "cancel_select_to_confirm");
    }

    private async Task HandleCancelConfirmAsync(UserInput input, ConversationSnapshot currentState, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var payload = Deserialize(currentState.Payload);
        if (payload.BookingId is null)
        {
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!input.IsInteractive)
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "cancel_no")
        {
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        if (input.PayloadId != "cancel_yes")
        {
            await RepeatWithButtonsHintAsync(currentState.State, currentState.Payload, tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        var existing = await _bookings.GetFutureBookedByIdAndCustomerAsync(tenantId, customerId, payload.BookingId.Value);
        if (existing is null)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Эту запись уже нельзя отменить.");
            await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        await _bookings.CancelAsync(existing.Id, tenantId, CancelledBy.Customer.ToString());
        await _sender.SendTextAsync(waUserId, phoneNumberId, "Запись отменена.");

        var tenant = await _tenants.GetBotSettingsAsync(tenantId);
        if (tenant is not null)
        {
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
        }
    }

    private async Task HandleDuplicateDecisionAsync(UserInput input, FsmPayload payload, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        if (payload.ServiceId is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (input.PayloadId == "dup_keep")
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Оставили текущую запись.");
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        if (input.PayloadId == "dup_rebook")
        {
            var introSendResult = await _sender.SendTextAsync(waUserId, phoneNumberId, "Выберите новую дату. Текущая запись будет отменена только после подтверждения новой.");
            if (!introSendResult.IsSent)
            {
                LogStateNotAdvanced(
                    introSendResult,
                    tenantId,
                    customerId,
                    BotState.BookingConfirm,
                    BotState.BookingSelectDate,
                    "duplicate_rebook_intro");
                return;
            }

            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId, payload.ExistingBookingId);
            return;
        }

        if (input.PayloadId == "main_menu")
        {
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            "Выберите действие кнопкой.",
            [
                ("dup_keep", "Оставить"),
                ("dup_rebook", "Перезаписаться"),
                ("main_menu", "В меню")
            ]);
    }

    private async Task ShowMainMenuAsync(TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var sendResult = await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            tenant.GreetingText,
            [
                ("menu_book", "Записаться"),
                ("menu_manage", "Мои записи / Отменить")
            ]);
        await SaveStateIfSentAsync(sendResult, tenantId, customerId, BotState.MainMenu, null, null, "show_main_menu");
    }

    private async Task SendServicesMenuAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var services = await _services.GetActiveForBotAsync(tenantId);
        if (services.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Сейчас нет доступных услуг для записи.");
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        var rows = services
            .Take(10)
            .Select(x =>
            {
                var marker = x.HasPrimaryImage ? "📷 Фото • " : string.Empty;
                return ($"svc:{x.ServiceId}", x.Name, $"{marker}{x.DurationMinutes} мин");
            })
            .ToList();
        AppendMainMenuRow(rows);

        var sendResult = await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите услугу", "Услуги", rows);
        await SaveStateIfSentAsync(sendResult, tenantId, customerId, BotState.BookingSelectService, new FsmPayload(), null, "show_services_menu");
    }

    private async Task SendServiceChoiceMenuAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId)
    {
        var service = (await _services.GetActiveForBotAsync(tenantId)).FirstOrDefault(x => x.ServiceId == serviceId);
        if (service is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (service.HasPrimaryImage && !string.IsNullOrWhiteSpace(service.PrimaryImageUrl))
        {
            var sendResult = await _sender.SendButtonsAsync(
                waUserId,
                phoneNumberId,
                $"Для \"{service.Name}\" доступно фото. Что показать?",
                [
                    ($"svc_photo:{serviceId}", "📷 Смотреть фото"),
                    ($"svc_book:{serviceId}", "Продолжить запись"),
                    ("main_menu", "В меню")
                ]);
            await SaveStateIfSentAsync(
                sendResult,
                tenantId,
                customerId,
                BotState.BookingSelectService,
                new FsmPayload { ServiceId = serviceId },
                null,
                "service_choice_menu");
            return;
        }

        await StartBookingFlowAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
    }

    private async Task SendServicePhotoAndContinueAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId)
    {
        var service = (await _services.GetActiveForBotAsync(tenantId)).FirstOrDefault(x => x.ServiceId == serviceId);
        if (service is null)
        {
            await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(service.PrimaryImageUrl))
        {
            await _sender.SendImageAsync(waUserId, phoneNumberId, service.PrimaryImageUrl, service.Name);
        }

        var sendResult = await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            $"Продолжить запись на \"{service.Name}\"?",
            [
                ($"svc_book:{serviceId}", "Продолжить запись"),
                ("main_menu", "В меню")
            ]);

        await SaveStateIfSentAsync(
            sendResult,
            tenantId,
            customerId,
            BotState.PortfolioView,
            new FsmPayload { ServiceId = serviceId },
            null,
            "service_photo_continue");
    }

    private async Task StartBookingFlowAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId)
    {
        var duplicate = await _bookings.GetActiveByCustomerAndServiceAsync(tenantId, customerId, serviceId);
        if (duplicate is not null)
        {
            var serviceName = await ResolveServiceNameAsync(tenantId, serviceId);
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            var timezone = ResolveTimeZone(tenant?.Timezone);
            var localStart = TimeZoneInfo.ConvertTime(duplicate.StartAt, timezone);
            var sendResult = await _sender.SendButtonsAsync(
                waUserId,
                phoneNumberId,
                $"У вас уже есть запись на {serviceName}: {localStart:dd.MM.yyyy} в {localStart:HH:mm}. Что делаем?",
                [
                    ("dup_keep", "Оставить"),
                    ("dup_rebook", "Перезаписаться"),
                    ("main_menu", "В меню")
                ]);
            await SaveStateIfSentAsync(
                sendResult,
                tenantId,
                customerId,
                BotState.BookingConfirm,
                new FsmPayload { ServiceId = serviceId, ExistingBookingId = duplicate.Id, DuplicatePrompt = true },
                null,
                "duplicate_prompt");
            return;
        }

        await SendDateMenuAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
    }

    private async Task SendDateMenuAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId, Guid? existingBookingId = null)
    {
        var dates = await _slots.GetAvailableDatesAsync(tenantId, serviceId, 30);
        if (dates.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Нет доступных дат для этой услуги.");
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        var rows = dates.Take(10)
            .Select(x => ($"date:{x:yyyy-MM-dd}", x.ToString("dd.MM (ddd)", CultureInfo.GetCultureInfo("ru-RU")), ""))
            .ToList();
        AppendMainMenuRow(rows);

        var sendResult = await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите дату", "Даты", rows);
        await SaveStateIfSentAsync(
            sendResult,
            tenantId,
            customerId,
            BotState.BookingSelectDate,
            new FsmPayload { ServiceId = serviceId, ExistingBookingId = existingBookingId },
            null,
            "show_date_menu");
    }

    private async Task SendTimeMenuAsync(Guid tenantId, Guid customerId, Guid serviceId, DateOnly date, string waUserId, string phoneNumberId)
    {
        var slots = await _slots.GetAvailableSlotsAsync(tenantId, serviceId, date);
        if (slots.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "На эту дату нет свободного времени. Выберите другую дату.");
            var previous = await LoadStateAsync(tenantId, customerId);
            var previousPayload = Deserialize(previous.Payload);
            await SendDateMenuAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId, previousPayload.ExistingBookingId);
            return;
        }

        var rows = slots.Take(10)
            .Select(x => ($"time:{x.StartAt.UtcDateTime:O}", x.Label, ""))
            .ToList();
        AppendMainMenuRow(rows);

        var sendResult = await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите время", "Время", rows);
        var previous = await LoadStateAsync(tenantId, customerId);
        var previousPayload = Deserialize(previous.Payload);
        await SaveStateIfSentAsync(
            sendResult,
            tenantId,
            customerId,
            BotState.BookingSelectTime,
            new FsmPayload
            {
                ServiceId = serviceId,
                Date = date,
                ExistingBookingId = previousPayload.ExistingBookingId
            },
            previous.State,
            "show_time_menu");
    }

    private async Task SendCancelableBookingsMenuAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var allFuture = await _bookings.GetFutureByCustomerAsync(tenantId, customerId);
        var futureBooked = allFuture.Where(x => x.Status == BookingStatus.Booked).OrderBy(x => x.StartAt).ToList();
        if (futureBooked.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "У вас нет будущих записей.");
            var tenant = await _tenants.GetBotSettingsAsync(tenantId);
            if (tenant is not null)
            {
                await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
            }

            return;
        }

        var serviceNames = await _services.GetNamesByIdsAsync(tenantId, futureBooked.Select(x => x.ServiceId));
        var tenant = await _tenants.GetBotSettingsAsync(tenantId);
        var timezone = ResolveTimeZone(tenant?.Timezone);
        var rows = futureBooked.Take(10)
            .Select(x =>
            {
                var serviceName = serviceNames.GetValueOrDefault(x.ServiceId, "Услуга");
                var localStart = TimeZoneInfo.ConvertTime(x.StartAt, timezone);
                return ($"cancel:{x.Id}", $"{localStart:dd.MM HH:mm}", serviceName);
            })
            .ToList();
        AppendMainMenuRow(rows);

        var sendResult = await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите запись для отмены", "Записи", rows);
        await SaveStateIfSentAsync(sendResult, tenantId, customerId, BotState.CancelSelectBooking, new FsmPayload(), null, "show_cancelable_bookings");
    }

    private async Task RepeatWithButtonsHintAsync(BotState state, string? payloadJson, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId, TenantBotSettings? tenant = null)
    {
        await _sender.SendTextAsync(waUserId, phoneNumberId, "Пожалуйста, используйте кнопки ниже.");
        switch (state)
        {
            case BotState.MainMenu:
                if (tenant is null)
                {
                    tenant = await _tenants.GetBotSettingsAsync(tenantId);
                }

                if (tenant is not null)
                {
                    await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
                }

                break;
            case BotState.BookingSelectService:
                await SendServicesMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.PortfolioView:
                var portfolio = Deserialize(payloadJson);
                if (portfolio.ServiceId is not null)
                {
                    await SendServicePhotoAndContinueAsync(tenantId, customerId, portfolio.ServiceId.Value, waUserId, phoneNumberId);
                }

                break;
            case BotState.BookingSelectDate:
                var date = Deserialize(payloadJson);
                if (date.ServiceId is not null)
                {
                    await SendDateMenuAsync(tenantId, customerId, date.ServiceId.Value, waUserId, phoneNumberId, date.ExistingBookingId);
                }

                break;
            case BotState.BookingSelectTime:
                var time = Deserialize(payloadJson);
                if (time.ServiceId is not null && time.Date is not null)
                {
                    await SendTimeMenuAsync(tenantId, customerId, time.ServiceId.Value, time.Date.Value, waUserId, phoneNumberId);
                }

                break;
            case BotState.BookingConfirm:
                var confirm = Deserialize(payloadJson);
                OutboundSendResult confirmSendResult;
                if (confirm.DuplicatePrompt)
                {
                    confirmSendResult = await _sender.SendButtonsAsync(
                        waUserId,
                        phoneNumberId,
                        "Выберите действие кнопкой.",
                        [
                            ("dup_keep", "Оставить"),
                            ("dup_rebook", "Перезаписаться"),
                            ("main_menu", "В меню")
                        ]);
                }
                else
                {
                    confirmSendResult = await _sender.SendButtonsAsync(
                        waUserId,
                        phoneNumberId,
                        "Подтвердите запись кнопкой.",
                        [
                            ("book_yes", "Подтвердить"),
                            ("book_no", "Назад"),
                            ("main_menu", "В меню")
                        ]);
                }

                await SaveStateIfSentAsync(
                    confirmSendResult,
                    tenantId,
                    customerId,
                    BotState.BookingConfirm,
                    confirm,
                    state,
                    "repeat_booking_confirm");
                break;
            case BotState.CancelSelectBooking:
                await SendCancelableBookingsMenuAsync(tenantId, customerId, waUserId, phoneNumberId);
                break;
            case BotState.CancelConfirm:
                await _sender.SendButtonsAsync(
                    waUserId,
                    phoneNumberId,
                    "Подтвердите отмену кнопкой.",
                    [
                        ("cancel_yes", "Да"),
                        ("cancel_no", "Нет"),
                        ("main_menu", "В меню")
                    ]);
                break;
            default:
                if (tenant is null)
                {
                    tenant = await _tenants.GetBotSettingsAsync(tenantId);
                }

                if (tenant is not null)
                {
                    await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
                }

                break;
        }
    }

    private async Task<ConversationSnapshot> LoadStateAsync(Guid tenantId, Guid customerId)
    {
        var record = await _states.GetAsync(tenantId, customerId);
        if (record is null)
        {
            return new ConversationSnapshot(BotState.Idle, null);
        }

        if (record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _states.DeleteAsync(tenantId, customerId);
            return new ConversationSnapshot(BotState.Idle, null);
        }

        return new ConversationSnapshot(record.State, record.PayloadJson);
    }

    private async Task SaveStateAsync(Guid tenantId, Guid customerId, BotState state, FsmPayload? payload)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ConversationStateDto(
            Guid.NewGuid(),
            tenantId,
            customerId,
            state,
            payload is null ? "{}" : JsonSerializer.Serialize(payload),
            now,
            now.Add(StateTtl));

        await _states.UpsertAsync(entity);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static UserInput ParseInput(string messageType, string? interactiveType, string? payloadId, string? textBody)
    {
        var isInteractive = messageType == "interactive" && (interactiveType == "button_reply" || interactiveType == "list_reply");
        return new UserInput(isInteractive, payloadId, textBody);
    }

    private static bool ShouldResetToMainMenu(UserInput input)
    {
        if (input.IsInteractive && input.PayloadId == "main_menu")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(input.TextBody))
        {
            return false;
        }

        var normalized = input.TextBody.Trim().ToLowerInvariant();
        return normalized is "start" or "/start" or "menu" or "меню" or "reset" or "сброс" or "заново";
    }

    private void AppendMainMenuRow(List<(string Id, string Title, string Description)> rows)
    {
        if (rows.Count >= 10)
        {
            return;
        }

        rows.Add(("main_menu", "🏠 В главное меню", ""));
    }

    private async Task<string> ResolveServiceNameAsync(Guid tenantId, Guid serviceId)
    {
        var names = await _services.GetNamesByIdsAsync(tenantId, [serviceId]);
        return names.GetValueOrDefault(serviceId, "Услуга");
    }

    private async Task SaveStateIfSentAsync(
        OutboundSendResult sendResult,
        Guid tenantId,
        Guid customerId,
        BotState newState,
        FsmPayload? payload,
        BotState? previousState,
        string transition)
    {
        if (sendResult.IsSent)
        {
            await SaveStateAsync(tenantId, customerId, newState, payload);
            return;
        }

        LogStateNotAdvanced(sendResult, tenantId, customerId, previousState, newState, transition);
    }

    private void LogStateNotAdvanced(
        OutboundSendResult sendResult,
        Guid tenantId,
        Guid customerId,
        BotState? previousState,
        BotState nextState,
        string transition)
    {
        _logger.LogWarning(
            "Bot state was not advanced because outbound message was not sent. tenant_id={tenantId}, customer_id={customerId}, from_state={fromState}, to_state={toState}, transition={transition}, outbound_outcome={outboundOutcome}",
            tenantId,
            customerId,
            previousState,
            nextState,
            transition,
            sendResult.Outcome);
    }

    private static FsmPayload Deserialize(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new FsmPayload();
        }

        return JsonSerializer.Deserialize<FsmPayload>(payloadJson) ?? new FsmPayload();
    }

    private sealed record ConversationSnapshot(BotState State, string? Payload);
    private sealed record UserInput(bool IsInteractive, string? PayloadId, string? TextBody);
    private sealed record FsmPayload
    {
        public Guid? ServiceId { get; init; }
        public DateOnly? Date { get; init; }
        public DateTimeOffset? StartAt { get; init; }
        public DateTimeOffset? EndAt { get; init; }
        public Guid? BookingId { get; init; }
        public Guid? ExistingBookingId { get; init; }
        public bool DuplicatePrompt { get; init; }
    }
}
