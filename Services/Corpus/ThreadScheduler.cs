using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Assigns SentAt/TimeZoneId to a planned thread (PLAN-04, D-16): every message in its sender's business hours,
/// monotonic by Seq, and later than its parent by at least one minute with a realistic business-time latency.
/// </summary>
public static class ThreadScheduler
{
    // Snapped replies land 0-45 minutes after 09:00
    private const int MaxSnapJitterMinutes = 45;

    // Bump used when the sampled time would break monotonic order
    private const int MinBumpMinutes = 2;
    private const int MaxBumpMinutes = 20;

    /// <summary>
    /// Schedules <paramref name="thread"/> whose messages already have Seq, ReplyToSeq, FromId, To and CC. The root goes on
    /// the first business day on or after max(anchorDay, notBefore) in the root sender's zone.
    /// </summary>
    public static void Schedule(PlannedThread thread, DateOnly anchorDay, SkeletonContext ctx, DateOnly? notBefore = null)
    {
        var messages = thread.Messages.Where(m => m != null).OrderBy(m => m.Seq).ToList();
        if (messages.Count == 0)
            return;

        var random = ctx.Random;
        var root = messages[0];
        var rootZone = ctx.ZoneFor(root.FromId);
        var firstDay = notBefore.HasValue && notBefore.Value > anchorDay ? notBefore.Value : anchorDay;
        var day = BusinessCalendar.NextBusinessDay(firstDay);
        var rootAt = BusinessCalendar.At(rootZone, day, ThreadShapes.SampleRootTime(random));
        Assign(root, rootAt, rootZone);

        for (var i = 1; i < messages.Count; i++)
        {
            var message = messages[i];
            var previous = messages[i - 1];
            var parent = messages.Take(i).FirstOrDefault(m => m.Seq == message.ReplyToSeq) ?? previous;
            var zone = ctx.ZoneFor(message.FromId);

            var latency = ThreadShapes.SampleLatencyMinutes(ctx.IsExternalTo(message.FromId, parent.FromId), random);
            var parentLocal = TimeZoneInfo.ConvertTime(parent.SentAt, zone);
            var start = BusinessCalendar.NextBusinessInstant(parentLocal, zone, random.Next(MaxSnapJitterMinutes + 1));
            var candidate = BusinessCalendar.AddBusinessMinutes(start, latency, zone);

            // Monotonic: >= parent + 1 min and > previous Seq + 1 min; otherwise go a little after the previous message
            if (candidate < parent.SentAt.AddMinutes(1) || candidate <= previous.SentAt.AddMinutes(1))
            {
                var later = previous.SentAt > parent.SentAt ? previous.SentAt : parent.SentAt;
                candidate = BusinessCalendar.AddBusinessMinutes(later, random.Next(MinBumpMinutes, MaxBumpMinutes + 1), zone);
            }

            Assign(message, candidate, zone);
        }
    }

    /// <summary>
    /// True when every message is business time in its sender zone, Seq order is strictly increasing in time, each
    /// reply is at least one minute after its parent and ReplyToSeq points backwards. Used by tests and the plan validator.
    /// </summary>
    public static bool CheckInvariants(PlannedThread thread, Func<string, TimeZoneInfo> zoneFor, out string problem)
    {
        problem = string.Empty;
        var messages = thread.Messages.Where(m => m != null).OrderBy(m => m.Seq).ToList();

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var zone = zoneFor(message.FromId);

            if (i > 0 && message.Seq == messages[i - 1].Seq)
            {
                problem = $"Duplicate Seq {message.Seq}.";
                return false;
            }

            if (message.ReplyToSeq < 0 || message.ReplyToSeq >= message.Seq || (i == 0 && message.ReplyToSeq != 0))
            {
                problem = $"Message {message.Seq} replies to {message.ReplyToSeq}.";
                return false;
            }

            if (!BusinessCalendar.IsBusinessTime(message.SentAt, zone))
            {
                problem = $"Message {message.Seq} at {message.SentAt:O} is outside business hours in {zone.Id}.";
                return false;
            }

            if (!string.IsNullOrEmpty(message.TimeZoneId) && message.TimeZoneId != zone.Id)
            {
                problem = $"Message {message.Seq} zone {message.TimeZoneId} is not the sender zone {zone.Id}.";
                return false;
            }

            if (i == 0)
                continue;

            if (message.SentAt <= messages[i - 1].SentAt)
            {
                problem = $"Message {message.Seq} is not later than message {messages[i - 1].Seq}.";
                return false;
            }

            var parent = messages.Take(i).FirstOrDefault(m => m.Seq == message.ReplyToSeq) ?? messages[i - 1];
            if (message.SentAt < parent.SentAt.AddMinutes(1))
            {
                problem = $"Message {message.Seq} is less than a minute after its parent {parent.Seq}.";
                return false;
            }
        }

        return true;
    }

    private static void Assign(PlannedMessage message, DateTimeOffset instant, TimeZoneInfo zone)
    {
        message.SentAt = instant.ToOffset(zone.GetUtcOffset(instant));
        message.TimeZoneId = zone.Id;
    }
}
