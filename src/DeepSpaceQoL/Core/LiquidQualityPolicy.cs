using System;

namespace DeepSpaceQoL.Core
{
    /// <summary>Pure comparisons in the game's integer microliter units.</summary>
    public static class LiquidQualityPolicy
    {
        public static long Total(int[] parts)
        {
            if (parts == null) return -1;
            long total = 0;
            foreach (int part in parts)
            {
                if (part < 0) return -1;
                total += part;
            }
            return total <= int.MaxValue ? total : -1;
        }

        public static int Grade(int[] parts, int waterIndex)
        {
            long total = Total(parts);
            if (total <= 0 || waterIndex < 0 || waterIndex >= parts.Length) return -1;
            long purity = (long)parts[waterIndex] * 10000 / total;
            if (purity >= 9990) return 0;
            if (purity >= 9900) return 1;
            if (purity >= 9800) return 2;
            if (purity >= 9600) return 3;
            if (purity >= 9000) return 4;
            return 5;
        }

        public static bool MayTopUp(int[] source, int[] target, int waterIndex,
            double sourceTemperature, double targetTemperature)
        {
            if (source == null || target == null || source.Length != target.Length) return false;
            long sourceTotal = Total(source), targetTotal = Total(target);
            if (sourceTotal <= 0 || targetTotal < 0 || waterIndex < 0 || waterIndex >= source.Length || source[waterIndex] <= 0)
                return false;
            if (!Finite(sourceTemperature)) return false;
            // A genuinely empty bottle has no liquid quality or temperature to mix.
            if (targetTotal == 0) return true;
            if (!Finite(targetTemperature) || Math.Abs(sourceTemperature - targetTemperature) > 0.5)
                return false;
            if (Grade(source, waterIndex) != Grade(target, waterIndex)) return false;
            // Compare every constituent, not a translated/possibly forged quality label.
            // One microliter accounts for native proportional integer rounding.
            long roundingTolerance = Math.Max(sourceTotal, targetTotal);
            for (int i = 0; i < source.Length; i++)
                if ((source[i] == 0) != (target[i] == 0) ||
                    Math.Abs((long)source[i] * targetTotal - (long)target[i] * sourceTotal) > roundingTolerance)
                    return false;
            return true;
        }

        public static bool Conserved(int[] sourceBefore, int[] targetBefore, int[] sourceAfter, int[] targetAfter)
        {
            if (sourceBefore == null || targetBefore == null || sourceAfter == null || targetAfter == null ||
                sourceBefore.Length != targetBefore.Length || sourceBefore.Length != sourceAfter.Length ||
                sourceBefore.Length != targetAfter.Length || Total(sourceAfter) < 0 || Total(targetAfter) < 0)
                return false;
            for (int i = 0; i < sourceBefore.Length; i++)
                if ((long)sourceBefore[i] + targetBefore[i] != (long)sourceAfter[i] + targetAfter[i]) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
