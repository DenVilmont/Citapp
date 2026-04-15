using Citapp.Shared.DTOs;

namespace Citapp.BotApi.Services;

public record TimeSlot(DateTimeOffset StartAt, DateTimeOffset EndAt, string Label);
public class BookingConflictException : Exception { public BookingConflictException(string message) : base(message) {} }
