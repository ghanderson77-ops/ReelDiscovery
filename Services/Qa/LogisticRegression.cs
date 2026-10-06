namespace ReelDiscovery.Services.Qa;

/// <summary>
/// L2-regularized logistic regression on standardized columns, fitted by batch gradient descent with a fixed iteration
/// count. Fully deterministic: no random initialization or shuffling, so the same input always gives the same weights.
/// </summary>
internal sealed class LogisticRegression
{
    private LogisticRegression(double[] weights, double bias, double[] means, double[] stds)
    {
        Weights = weights;
        Bias = bias;
        Means = means;
        Stds = stds;
    }

    public double[] Weights { get; }
    public double Bias { get; }

    /// <summary>Training column means used to standardize prediction rows.</summary>
    public double[] Means { get; }

    /// <summary>Training column standard deviations (0 replaced by 1 so constant columns stay finite).</summary>
    public double[] Stds { get; }

    /// <summary>
    /// Fits the model. <paramref name="lambda"/> / n is applied to the weights (not the bias) each step.
    /// </summary>
    public static LogisticRegression Fit(double[][] x, int[] y, double lambda = 1.0, int iterations = 300,
        double learningRate = 0.5)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
            throw new ArgumentException("Rows and labels differ in length.", nameof(y));

        var n = x.Length;
        var d = n == 0 ? 0 : x[0].Length;
        var means = new double[d];
        var stds = new double[d];
        for (var j = 0; j < d; j++)
        {
            var mean = 0.0;
            for (var i = 0; i < n; i++)
                mean += x[i][j];
            mean /= n;

            var variance = 0.0;
            for (var i = 0; i < n; i++)
                variance += (x[i][j] - mean) * (x[i][j] - mean);
            var std = Math.Sqrt(variance / n);

            means[j] = mean;
            // A constant column would divide by zero; with std 1 it standardizes to all zeros and gets no weight
            stds[j] = std > 1e-12 ? std : 1.0;
        }

        var z = new double[n][];
        for (var i = 0; i < n; i++)
            z[i] = Standardize(x[i], means, stds);

        var weights = new double[d];
        var bias = 0.0;
        if (n == 0)
            return new LogisticRegression(weights, bias, means, stds);

        var gradient = new double[d];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            Array.Clear(gradient);
            var biasGradient = 0.0;
            for (var i = 0; i < n; i++)
            {
                var error = Sigmoid(Dot(weights, z[i]) + bias) - y[i];
                for (var j = 0; j < d; j++)
                    gradient[j] += error * z[i][j];
                biasGradient += error;
            }

            for (var j = 0; j < d; j++)
                weights[j] -= learningRate * (gradient[j] / n + lambda / n * weights[j]);
            bias -= learningRate * biasGradient / n;
        }

        return new LogisticRegression(weights, bias, means, stds);
    }

    /// <summary>P(y = 1) for a raw (unstandardized) row, standardized with the training mean and std.</summary>
    public double PredictProbability(double[] row) => Sigmoid(Dot(Weights, Standardize(row, Means, Stds)) + Bias);

    /// <summary>Mean of the per-class recalls at <paramref name="threshold"/>; a class with no rows is left out.</summary>
    public static double BalancedAccuracy(int[] y, double[] p, double threshold = 0.5)
    {
        int pos = 0, neg = 0, truePos = 0, trueNeg = 0;
        for (var i = 0; i < y.Length; i++)
        {
            var predicted = p[i] >= threshold;
            if (y[i] == 1)
            {
                pos++;
                if (predicted)
                    truePos++;
            }
            else
            {
                neg++;
                if (!predicted)
                    trueNeg++;
            }
        }

        if (pos == 0 && neg == 0)
            return 0.5;
        if (pos == 0)
            return trueNeg / (double)neg;
        if (neg == 0)
            return truePos / (double)pos;
        return (truePos / (double)pos + trueNeg / (double)neg) / 2;
    }

    /// <summary>Rank-based ROC AUC (Mann-Whitney), ties get their average rank; 0.5 when a class is empty.</summary>
    public static double Auc(int[] y, double[] p)
    {
        var order = Enumerable.Range(0, y.Length).OrderBy(i => p[i]).ToArray();
        var ranks = new double[y.Length];
        var k = 0;
        while (k < order.Length)
        {
            var end = k;
            while (end + 1 < order.Length && p[order[end + 1]] == p[order[k]])
                end++;
            // Ranks are 1-based; a tie group shares the mean of its ranks
            var rank = (k + end) / 2.0 + 1;
            for (var m = k; m <= end; m++)
                ranks[order[m]] = rank;
            k = end + 1;
        }

        long pos = y.Count(v => v == 1);
        long neg = y.Length - pos;
        if (pos == 0 || neg == 0)
            return 0.5;

        var rankSum = 0.0;
        for (var i = 0; i < y.Length; i++)
            if (y[i] == 1)
                rankSum += ranks[i];
        return (rankSum - pos * (pos + 1) / 2.0) / (pos * (double)neg);
    }

    private static double[] Standardize(double[] row, double[] means, double[] stds)
    {
        var z = new double[means.Length];
        for (var j = 0; j < means.Length; j++)
            z[j] = (row[j] - means[j]) / stds[j];
        return z;
    }

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var j = 0; j < a.Length; j++)
            sum += a[j] * b[j];
        return sum;
    }

    private static double Sigmoid(double t) => t >= 0 ? 1 / (1 + Math.Exp(-t)) : Math.Exp(t) / (1 + Math.Exp(t));
}
