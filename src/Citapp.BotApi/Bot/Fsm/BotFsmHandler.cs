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

    public BotFsmHandler(
        ConversationStateRepository states,
        TenantRepository tenants,
        ServiceRepository services,
        BookingRepository bookings,
        SlotCalculationService slots,
        BookingTransactionService bookingTransactions,
        WhatsAppMessageSender sender)
    {
        _states = states;
        _tenants = tenants;
        _services = services;
        _bookings = bookings;
        _slots = slots;
        _bookingTransactions = bookingTransactions;
        _sender = sender;
    }

    public async Task HandleAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId, string messageType, string? interactiveType, string? payloadId, string? textBody)
    {
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
            await SaveStateAsync(tenantId, customerId, BotState.MainMenu, null);
            return;
        }

        var input = ParseInput(messageType, interactiveType, payloadId, textBody);
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
                await HandleSelectTimeAsync(input, currentState, tenantId, customerId, waUserId, phoneNumberId);
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
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId);
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

        if (input.PayloadId is null || !input.PayloadId.StartsWith("date:", StringComparison.Ordinal))
        {
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId);
            return;
        }

        if (!DateOnly.TryParseExact(input.PayloadId[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId);
            return;
        }

        await SendTimeMenuAsync(tenantId, customerId, payload.ServiceId.Value, date, waUserId, phoneNumberId);
    }

    private async Task HandleSelectTimeAsync(UserInput input, ConversationSnapshot currentState, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
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
        await SaveStateAsync(tenantId, customerId, BotState.BookingConfirm, confirmPayload);

        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            $"Подтвердить запись на {payload.Date:dd.MM} в {startAt:HH:mm}?",
            [
                ("book_yes", "Подтвердить"),
                ("book_no", "Назад")
            ]);
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
            if (payload.ExistingBookingId is not null)
            {
                await _bookings.CancelAsync(payload.ExistingBookingId.Value, tenantId, CancelledBy.Customer.ToString());
            }

            await _sender.SendTextAsync(waUserId, phoneNumberId, $"Готово ✅ {booking.Date:dd.MM} в {booking.StartAt:HH:mm}");
            await ShowMainMenuAsync(tenant, tenantId, customerId, waUserId, phoneNumberId);
        }
        catch (BookingConflictException)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Слот уже занят. Выберите другое время.");
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

        await SaveStateAsync(tenantId, customerId, BotState.CancelConfirm, new FsmPayload { BookingId = bookingId });
        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            "Отменить эту запись?",
            [
                ("cancel_yes", "Да"),
                ("cancel_no", "Нет")
            ]);
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
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Выберите новую дату. Текущая запись будет отменена только после подтверждения новой.");
            await SendDateMenuAsync(tenantId, customerId, payload.ServiceId.Value, waUserId, phoneNumberId);
            await SaveStateAsync(
                tenantId,
                customerId,
                BotState.BookingSelectDate,
                new FsmPayload
                {
                    ServiceId = payload.ServiceId,
                    ExistingBookingId = payload.ExistingBookingId
                });
            return;
        }

        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            "Используйте кнопки ниже.",
            [
                ("dup_keep", "Оставить"),
                ("dup_rebook", "Перезаписаться")
            ]);
    }

    private async Task ShowMainMenuAsync(TenantBotSettings tenant, Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            tenant.GreetingText,
            [
                ("menu_book", "Записаться"),
                ("menu_manage", "Мои записи / Отменить")
            ]);
        await SaveStateAsync(tenantId, customerId, BotState.MainMenu, null);
    }

    private async Task SendServicesMenuAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId)
    {
        var services = await _services.GetActiveForBotAsync(tenantId);
        if (services.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Сейчас нет доступных услуг.");
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

        await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите услугу", "Услуги", rows);
        await SaveStateAsync(tenantId, customerId, BotState.BookingSelectService, new FsmPayload());
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
            await _sender.SendButtonsAsync(
                waUserId,
                phoneNumberId,
                $"Для \"{service.Name}\" доступно фото. Что показать?",
                [
                    ($"svc_photo:{serviceId}", "📷 Смотреть фото"),
                    ($"svc_book:{serviceId}", "Продолжить запись"),
                    ("main_menu", "В меню")
                ]);
            await SaveStateAsync(tenantId, customerId, BotState.BookingSelectService, new FsmPayload { ServiceId = serviceId });
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

        await _sender.SendButtonsAsync(
            waUserId,
            phoneNumberId,
            $"Продолжить запись на \"{service.Name}\"?",
            [
                ($"svc_book:{serviceId}", "Продолжить запись"),
                ("main_menu", "В меню")
            ]);

        await SaveStateAsync(tenantId, customerId, BotState.PortfolioView, new FsmPayload { ServiceId = serviceId });
    }

    private async Task StartBookingFlowAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId)
    {
        var duplicate = await _bookings.GetActiveByCustomerAndServiceAsync(tenantId, customerId, serviceId);
        if (duplicate is not null)
        {
            var svc = await _services.GetSnapshotAsync(tenantId, serviceId);
            var title = svc is null ? "услуга" : "эта услуга";
            await _sender.SendButtonsAsync(
                waUserId,
                phoneNumberId,
                $"У вас уже есть запись на {title}. Что делаем?",
                [
                    ("dup_keep", "Оставить"),
                    ("dup_rebook", "Перезаписаться")
                ]);
            await SaveStateAsync(tenantId, customerId, BotState.BookingConfirm, new FsmPayload { ServiceId = serviceId, ExistingBookingId = duplicate.Id, DuplicatePrompt = true });
            return;
        }

        await SendDateMenuAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
    }

    private async Task SendDateMenuAsync(Guid tenantId, Guid customerId, Guid serviceId, string waUserId, string phoneNumberId)
    {
        var dates = await _slots.GetAvailableDatesAsync(tenantId, serviceId, 30);
        if (dates.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "Нет доступных дат. Выберите другую услугу.");
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

        await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите дату", "Даты", rows);
        await SaveStateAsync(tenantId, customerId, BotState.BookingSelectDate, new FsmPayload { ServiceId = serviceId });
    }

    private async Task SendTimeMenuAsync(Guid tenantId, Guid customerId, Guid serviceId, DateOnly date, string waUserId, string phoneNumberId)
    {
        var slots = await _slots.GetAvailableSlotsAsync(tenantId, serviceId, date);
        if (slots.Count == 0)
        {
            await _sender.SendTextAsync(waUserId, phoneNumberId, "На эту дату нет времени. Выберите другую дату.");
            await SendDateMenuAsync(tenantId, customerId, serviceId, waUserId, phoneNumberId);
            return;
        }

        var rows = slots.Take(10)
            .Select(x => ($"time:{x.StartAt.UtcDateTime:O}", x.Label, ""))
            .ToList();

        await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите время", "Время", rows);
        var previous = await LoadStateAsync(tenantId, customerId);
        var previousPayload = Deserialize(previous.Payload);
        await SaveStateAsync(
            tenantId,
            customerId,
            BotState.BookingSelectTime,
            new FsmPayload
            {
                ServiceId = serviceId,
                Date = date,
                ExistingBookingId = previousPayload.ExistingBookingId
            });
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
        var rows = futureBooked.Take(10)
            .Select(x =>
            {
                var serviceName = serviceNames.GetValueOrDefault(x.ServiceId, "Услуга");
                return ($"cancel:{x.Id}", $"{x.StartAt:dd.MM HH:mm}", serviceName);
            })
            .ToList();

        await _sender.SendListAsync(waUserId, phoneNumberId, "Выберите запись для отмены", "Записи", rows);
        await SaveStateAsync(tenantId, customerId, BotState.CancelSelectBooking, new FsmPayload());
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
                    await SendDateMenuAsync(tenantId, customerId, date.ServiceId.Value, waUserId, phoneNumberId);
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
                if (confirm.DuplicatePrompt)
                {
                    await _sender.SendButtonsAsync(
                        waUserId,
                        phoneNumberId,
                        "Используйте кнопки ниже.",
                        [
                            ("dup_keep", "Оставить"),
                            ("dup_rebook", "Перезаписаться")
                        ]);
                }
                else
                {
                    await _sender.SendButtonsAsync(
                        waUserId,
                        phoneNumberId,
                        "Подтвердите запись кнопкой.",
                        [
                            ("book_yes", "Подтвердить"),
                            ("book_no", "Назад")
                        ]);
                }

                await SaveStateAsync(tenantId, customerId, BotState.BookingConfirm, confirm);
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
                        ("cancel_no", "Нет")
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

    private static UserInput ParseInput(string messageType, string? interactiveType, string? payloadId, string? textBody)
    {
        var isInteractive = messageType == "interactive" && (interactiveType == "button_reply" || interactiveType == "list_reply");
        return new UserInput(isInteractive, payloadId, textBody);
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
