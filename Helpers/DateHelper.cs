namespace ReelDiscovery.Helpers;

public static class DateHelper
{

    public static List<DateTime> DistributeDatesForThread(
        int emailCount,
        DateTime threadStart,
        DateTime threadEnd)
    {
        var dates = new List<DateTime>();

        if (emailCount <= 0) return dates;
        if (emailCount == 1)
        {
            dates.Add(AdjustToBusinessHours(threadStart));
            return dates;
        }

        var totalMinutes = (threadEnd - threadStart).TotalMinutes;
        var avgGap = totalMinutes / (emailCount - 1);

        var current = threadStart;

        for (int i = 0; i < emailCount; i++)
        {
            // Adjust to business hours 90% of the time
            var adjustedDate = Random.Shared.NextDouble() < 0.9
                ? AdjustToBusinessHours(current)
                : current;

            dates.Add(adjustedDate);

            if (i < emailCount - 1)
            {
                // Add some randomness to gaps (0.3x to 1.7x the average)
                var gapVariation = avgGap * (0.3 + Random.Shared.NextDouble() * 1.4);
                current = current.AddMinutes(gapVariation);

                // Ensure we don't go past the end date
                if (current > threadEnd)
                    current = threadEnd;
            }
        }

        return dates;
    }

    public static DateTime AdjustToBusinessHours(DateTime dt)
    {
        // Skip weekends
        while (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
        {
            dt = dt.AddDays(1).Date.AddHours(9);
        }

        // Adjust to business hours (8 AM - 7 PM)
        if (dt.Hour < 8)
        {
            dt = dt.Date.AddHours(8).AddMinutes(Random.Shared.Next(0, 60));
        }
        else if (dt.Hour >= 19)
        {
            // Move to next business day
            dt = dt.Date.AddDays(1);
            while (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
            {
                dt = dt.AddDays(1);
            }
            dt = dt.AddHours(8).AddMinutes(Random.Shared.Next(0, 60));
        }

        return dt;
    }

    public static (DateTime start, DateTime end) AllocateDateWindow(
        DateTime overallStart,
        DateTime overallEnd,
        int storylineIndex,
        int totalStorylines)
    {
        var totalDays = (overallEnd - overallStart).TotalDays;
        var daysPerStoryline = totalDays / totalStorylines;

        // Allow some overlap between storylines
        var overlapDays = daysPerStoryline * 0.2;

        var start = overallStart.AddDays(storylineIndex * daysPerStoryline - overlapDays);
        var end = overallStart.AddDays((storylineIndex + 1) * daysPerStoryline + overlapDays);

        // Clamp to overall range
        if (start < overallStart) start = overallStart;
        if (end > overallEnd) end = overallEnd;

        return (start, end);
    }

    public static DateTime RandomDateInRange(DateTime start, DateTime end)
    {
        var range = (end - start).TotalMinutes;
        var randomMinutes = Random.Shared.NextDouble() * range;
        return start.AddMinutes(randomMinutes);
    }

    /// <summary>
    /// Interpolate a date within a range based on a progress fraction (0.0 to 1.0)
    /// </summary>
    public static DateTime InterpolateDateInRange(DateTime start, DateTime end, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        var totalMinutes = (end - start).TotalMinutes;
        return start.AddMinutes(totalMinutes * fraction);
    }

    public static string FormatForFileName(DateTime date)
    {
        return date.ToString("yyyyMMdd_HHmmss");
    }
}
