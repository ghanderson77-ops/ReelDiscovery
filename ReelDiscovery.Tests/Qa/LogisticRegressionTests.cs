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
}
