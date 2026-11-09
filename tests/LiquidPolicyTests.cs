using System;
using DeepSpaceQoL.Core;

internal static class LiquidPolicyTests
{
    private static int checks;
    public static void Main()
    {
        Check(LiquidQualityPolicy.MayTopUp(new[] { 999000, 1000, 0 }, new[] { 0, 0, 0 }, 0, 26, 10), "empty bottle accepted regardless of empty-container temperature");
        Check(LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000, 0 }, new[] { 495000, 5000, 0 }, 0, 26, 26), "same composition at different volumes");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000, 0 }, new[] { 495000, 0, 5000 }, 0, 26, 26), "same visible purity but different contaminants rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000, 0 }, new[] { 495000, 4999, 1 }, 0, 26, 26), "even a new trace constituent is rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 999000, 1000 }, new[] { 490000, 10000 }, 0, 26, 26), "different grade rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000 }, new[] { 490000, 10000 }, 0, 26, 26), "different actual composition rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000 }, new[] { 495000, 5000 }, 0, 26, 27), "temperature mismatch rejected");
        Check(LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000 }, new[] { 495000, 5000 }, 0, 26, 26.5), "small ambient drift accepted");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990000, 10000 }, new[] { 495000, 5000 }, 0, double.NaN, 26), "unknown source temperature rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 0, 1000000 }, new[] { 0, 0 }, 0, 26, 26), "non-water source rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 0, 0 }, new[] { 0, 0 }, 0, 26, 26), "empty source rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 100, -1 }, new[] { 0, 0 }, 0, 26, 26), "negative native state rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { int.MaxValue, 1 }, new[] { 0, 0 }, 0, 26, 26), "overflow total rejected");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 100, 1 }, new[] { 0 }, 0, 26, 26), "component schema mismatch rejected");
        Check(LiquidQualityPolicy.MayTopUp(new[] { 990001, 9999 }, new[] { 495000, 5000 }, 0, 26, 26), "one microliter integer rounding accepted");
        Check(!LiquidQualityPolicy.MayTopUp(new[] { 990010, 9990 }, new[] { 495000, 5000 }, 0, 26, 26), "meaningful ratio change rejected");
        Check(LiquidQualityPolicy.Grade(new[] { 999000, 1000 }, 0) == 0, "pure threshold inclusive");
        Check(LiquidQualityPolicy.Grade(new[] { 998999, 1001 }, 0) == 1, "threshold below pure");
        Check(LiquidQualityPolicy.Grade(new[] { 990000, 10000 }, 0) == 1, "high quality inclusive");
        Check(LiquidQualityPolicy.Grade(new[] { 980000, 20000 }, 0) == 2, "base threshold inclusive");
        Check(LiquidQualityPolicy.Grade(new[] { 960000, 40000 }, 0) == 3, "ghost threshold inclusive");
        Check(LiquidQualityPolicy.Grade(new[] { 900000, 100000 }, 0) == 4, "rust threshold inclusive");
        Check(LiquidQualityPolicy.Grade(new[] { 899999, 100001 }, 0) == 5, "gutter threshold");
        Check(LiquidQualityPolicy.Conserved(new[] { 990000, 10000 }, new[] { 0, 0 }, new[] { 495000, 5000 }, new[] { 495000, 5000 }), "per-component conservation");
        Check(!LiquidQualityPolicy.Conserved(new[] { 990000, 10000 }, new[] { 0, 0 }, new[] { 495000, 5000 }, new[] { 495001, 4999 }), "total equal but component transformed rejected");
        Check(!LiquidQualityPolicy.Conserved(new[] { 100, 0 }, new[] { 0, 0 }, new[] { 50, 0 }, new[] { 51, 0 }), "duplication rejected");
        Check(!LiquidQualityPolicy.Conserved(new[] { 100, 0 }, new[] { 0, 0 }, new[] { 49, 0 }, new[] { 50, 0 }), "loss rejected");
        var random = new Random(85117);
        for (int test = 0; test < 2000; test++)
        {
            int water = random.Next(10000, 1000000), dirt = random.Next(1, 5000), salt = random.Next(1, 5000);
            int scale = random.Next(1, 100);
            int[] small = { water, dirt, salt }, large = { water * scale, dirt * scale, salt * scale };
            Check(LiquidQualityPolicy.MayTopUp(large, small, 0, 26, 26), "random equivalent ratio");
            Check(LiquidQualityPolicy.MayTopUp(small, large, 0, 26, 26), "random symmetric equivalent ratio");
            int[] remaining = { large[0] - small[0], large[1] - small[1], large[2] - small[2] };
            Check(LiquidQualityPolicy.Conserved(large, new[] { 0, 0, 0 }, remaining, small), "random conservative split");
        }
        Console.WriteLine($"Liquid policy: {checks} checks passed.");
    }

    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception(message);
    }
}
