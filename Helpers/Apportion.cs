namespace ReelDiscovery.Helpers;

/// <summary>
/// Largest-remainder apportionment shared by corpus settings and the corpus planner.
/// </summary>
public static class Apportion
{
    /// <summary>
    /// Splits <paramref name="total"/> into integer shares proportional to <paramref name="weights"/>, each at least
    /// <paramref name="min"/>. Equal shares when every weight is 0; every item gets <paramref name="min"/> when the
    /// total cannot cover the minimums (the caller reports the shortfall). Ties go to the lower index.
    /// </summary>
    public static List<int> Largest(int total, IReadOnlyList<double> weights, int min = 0)
    {
        var n = weights.Count;
        var result = Enumerable.Repeat(min, n).ToList();
        var remaining = total - min * n;
        if (n == 0 || remaining <= 0)
            return result;

        // Negative weights count as 0
        var clean = weights.Select(w => w > 0 ? w : 0).ToList();
        var sum = clean.Sum();
        var shares = clean.Select(w => sum > 0 ? remaining * w / sum : (double)remaining / n).ToList();
        for (var i = 0; i < n; i++)
            result[i] += (int)Math.Floor(shares[i]);

        var left = total - result.Sum();
        foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => shares[i] - Math.Floor(shares[i])).ThenBy(i => i).Take(left))
            result[i]++;

        return result;
    }
}
