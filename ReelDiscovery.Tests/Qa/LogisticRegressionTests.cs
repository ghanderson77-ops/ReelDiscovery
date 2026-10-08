using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;

namespace ReelDiscovery.Tests.Qa;

public class LogisticRegressionTests
{
    // Two clusters separated on both features; no Random
    private static (double[][] X, int[] Y) Separable()
    {
        var x = new List<double[]>();
        var y = new List<int>();
        for (var i = 0; i < 40; i++)
        {
            var jitter = (i % 7) * 0.1;
            x.Add(new[] { 1.0 + jitter, 2.0 - jitter });
            y.Add(0);
            x.Add(new[] { 4.0 + jitter, 6.0 - jitter });
            y.Add(1);
        }

        return (x.ToArray(), y.ToArray());
    }

    [Fact]
    public void Fit_Separable_TrainingAccuracyIsOne()
    {
        var (x, y) = Separable();
        var model = LogisticRegression.Fit(x, y);
        var p = x.Select(model.PredictProbability).ToArray();
        Assert.Equal(1.0, LogisticRegression.BalancedAccuracy(y, p));
        Assert.Equal(1.0, LogisticRegression.Auc(y, p));
    }

    [Fact]
    public void Fit_NoiseLabels_HeldOutBalancedAccuracyNearHalf()
    {
        // Labels from StableRank parity are independent of the features
        var n = 400;
        var x = new double[n][];
        var y = new int[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = new[] { i % 10, (i * 7) % 13, i % 2 == 0 ? 1.0 : 0.0 };
            y[i] = (int)(RenderAssignments.StableRank($"noise-{i}") % 2);
        }

        var train = Enumerable.Range(0, n).Where(i => i % 2 == 0).ToArray();
        var test = Enumerable.Range(0, n).Where(i => i % 2 == 1).ToArray();
        var model = LogisticRegression.Fit(train.Select(i => x[i]).ToArray(), train.Select(i => y[i]).ToArray());
        var p = test.Select(i => model.PredictProbability(x[i])).ToArray();
        var ba = LogisticRegression.BalancedAccuracy(test.Select(i => y[i]).ToArray(), p);

        Assert.InRange(ba, 0.40, 0.60);
    }

    [Fact]
    public void Fit_IsDeterministic()
    {
        var (x, y) = Separable();
        var a = LogisticRegression.Fit(x, y);
        var b = LogisticRegression.Fit(x, y);
        Assert.Equal(a.Weights, b.Weights);
        Assert.Equal(a.Bias, b.Bias);
    }

    [Fact]
    public void Predict_UsesTrainingMeanAndStd()
    {
        var (x, y) = Separable();
        var model = LogisticRegression.Fit(x, y);
        Assert.Equal(x.Average(r => r[0]), model.Means[0], 10);
        Assert.Equal(x.Average(r => r[1]), model.Means[1], 10);
        // A point far on the positive side stays positive even though it is outside the training range
        Assert.True(model.PredictProbability(new[] { 40.0, 60.0 }) > 0.99);
        Assert.True(model.PredictProbability(new[] { -40.0, -60.0 }) < 0.01);
    }

    [Fact]
    public void ZeroVarianceFeature_NoNaN()
    {
        var x = new[] { new[] { 1.0, 5.0 }, new[] { 2.0, 5.0 }, new[] { 3.0, 5.0 }, new[] { 4.0, 5.0 } };
        var y = new[] { 0, 0, 1, 1 };
        var model = LogisticRegression.Fit(x, y);
        Assert.All(model.Weights, w => Assert.False(double.IsNaN(w)));
        Assert.False(double.IsNaN(model.Bias));
        var p = model.PredictProbability(new[] { 2.5, 9.0 });
        Assert.False(double.IsNaN(p));
        Assert.InRange(p, 0.0, 1.0);
    }

    [Fact]
    public void BalancedAccuracy_AveragesClassRecalls()
    {
        // positives: 3 of 4 right; negatives: 1 of 2 right -> (0.75 + 0.5) / 2
        var y = new[] { 1, 1, 1, 1, 0, 0 };
        var p = new[] { 0.9, 0.8, 0.7, 0.1, 0.2, 0.6 };
        Assert.Equal(0.625, LogisticRegression.BalancedAccuracy(y, p), 10);
    }

    [Fact]
    public void Auc_TiesAveraged()
    {
        var y = new[] { 1, 0 };
        Assert.Equal(0.5, LogisticRegression.Auc(y, new[] { 0.5, 0.5 }));
        Assert.Equal(1.0, LogisticRegression.Auc(y, new[] { 0.9, 0.1 }));
        Assert.Equal(0.0, LogisticRegression.Auc(y, new[] { 0.1, 0.9 }));
        // 1 pos > 2 negs, tie with 1 -> (2 + 0.5) / 3
        Assert.Equal(2.5 / 3, LogisticRegression.Auc(new[] { 1, 0, 0, 0 }, new[] { 0.5, 0.5, 0.2, 0.1 }), 10);
    }

    [Fact]
    public void Auc_SingleClass_IsHalf()
    {
        Assert.Equal(0.5, LogisticRegression.Auc(new[] { 1, 1 }, new[] { 0.2, 0.8 }));
    }

    // 20 perfectly correlated columns (one signal repeated) plus a weak second feature: lambda_max of the standardized
    // covariance is about 20, so the fixed 0.5 step overshoots (stable needs a step below 8 / 20)
    private static (double[][] X, int[] Y) Correlated()
    {
        var n = 200;
        var x = new double[n][];
        var y = new int[n];
        for (var i = 0; i < n; i++)
        {
            var label = (int)(RenderAssignments.StableRank($"corr-{i}") % 2);
            // The signal agrees with the label 80% of the time, so the classes overlap and the optimum is finite
            var signal = i % 5 == 0 ? 1 - label : label;
            var row = new double[21];
            for (var j = 0; j < 20; j++)
                row[j] = signal;
            row[20] = i % 3;
            x[i] = row;
            y[i] = label;
        }

        return (x, y);
    }

    [Fact]
    public void WRB04_CorrelatedColumns_LossNeverIncreases_AndTheFitLearns()
    {
        var (x, y) = Correlated();
        var losses = Enumerable.Range(0, 60).Select(k => LogisticRegression.Fit(x, y, iterations: k).Loss(x, y)).ToList();

        for (var k = 1; k < losses.Count; k++)
            Assert.True(losses[k] <= losses[k - 1] + 1e-12, $"loss rose at iteration {k}: {losses[k - 1]} -> {losses[k]}");
        Assert.True(losses[^1] < Math.Log(2) - 0.1, $"final loss {losses[^1]}");

        var model = LogisticRegression.Fit(x, y);
        Assert.All(model.Weights, w => Assert.True(double.IsFinite(w)));
        var p = x.Select(model.PredictProbability).ToArray();
        Assert.InRange(LogisticRegression.BalancedAccuracy(y, p), 0.75, 0.85);
        var again = LogisticRegression.Fit(x, y);
        Assert.Equal(model.Weights, again.Weights);
        Assert.Equal(model.Bias, again.Bias);
    }
}
