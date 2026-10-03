using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Sender-local message time, shared by wave building, knowledge gating and ledger dating.
/// </summary>
public static class MessageTime
{
    /// <summary>
    /// SentAt converted to the sender's zone; falls back to the stored offset when the zone id does not resolve.
    /// </summary>
    public static DateTimeOffset Local(PlannedMessage message)
    {
        var tz = BusinessCalendar.ResolveId(message.TimeZoneId);
        return tz != null ? TimeZoneInfo.ConvertTime(message.SentAt, tz) : message.SentAt;
    }

    public static DateOnly LocalDate(PlannedMessage message) => DateOnly.FromDateTime(Local(message).DateTime);

    // DateTime.MaxValue when the thread has no messages
    public static DateTime FirstUtc(PlannedThread thread) =>
        thread.Messages.Count > 0 ? thread.Messages.Min(m => m.SentAt.UtcDateTime) : DateTime.MaxValue;

    // DateOnly.MaxValue when the thread has no messages
    public static DateOnly FirstLocalDate(PlannedThread thread) =>
        thread.Messages.Count > 0 ? LocalDate(thread.Messages.OrderBy(m => m.Seq).First()) : DateOnly.MaxValue;

    // DateOnly.MinValue when the thread has no messages
    public static DateOnly LastLocalDate(PlannedThread thread) =>
        thread.Messages.Count > 0 ? LocalDate(thread.Messages.OrderBy(m => m.Seq).Last()) : DateOnly.MinValue;
}
