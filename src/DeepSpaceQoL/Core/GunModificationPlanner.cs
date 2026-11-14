using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceQoL.Core;

/// <summary>Read-only, bounded search over real accessory tokens. Native movement is separate.</summary>
internal static class GunModificationPlanner
{
    internal sealed class Requirement
    {
        // 0..5 current gun stats; 6 suppression; 7 caliber; 8 occupied shape cells.
        public int Stat;
        public int Value;
        public bool Larger = true;
        public bool Equal;
        public int Reward;
        public bool IsMet(int value) => Equal ? value == Value : Larger ? value > Value : value < Value;
    }

    internal sealed class Option
    {
        public int Token = -1; // -1 is an empty slot, never a generated item.
        public string Identifier = "";
        public string ExclusiveGroup = "";
        public int[] Percent = new int[6];
        public int[] Flat = new int[6];
        public int Suppression;
        public long[] ShapeCells = Array.Empty<long>();
        public bool Special;
        public bool IsNew;
        public int Changes;
        public long Cost;
    }

    internal sealed class Problem
    {
        public int[] BaseStats = new int[6];
        public int ConditionPercent;
        public int Caliber;
        public long[] BaseCells = Array.Empty<long>();
        public Requirement[] Mandatory = Array.Empty<Requirement>();
        public Requirement[] Bonus = Array.Empty<Requirement>();
        public Option[][] Slots = Array.Empty<Option[]>();
        public Func<Option[], bool>? Validate;
        public int MaxNodes = 1_000_000;
    }

    internal sealed class Result
    {
        public Option[]? Choices;
        public int[]? Values;
        public int BonusMet;
        public long BonusReward;
        public long AddedCost;
        public int AddedCount;
        public int Changes;
        public bool SearchLimited;
        public int Nodes;
    }

    /// <summary>Each Step visits a bounded number of nodes, on the calling thread.</summary>
    internal sealed class Search
    {
        private readonly Problem problem;
        private readonly Option[] choices;
        private readonly int[] next;
        private readonly long[,] minPercent, maxPercent, minFlat, maxFlat;
        private int depth;
        public Result Best { get; } = new();
        public bool Complete { get; private set; }

        public Search(Problem problem)
        {
            this.problem = problem;
            if (problem.BaseStats.Length != 6 || problem.Slots.Length > 16 || problem.MaxNodes < 1 ||
                problem.Slots.Any(s => s.Length == 0 || s.Any(o => o.Percent.Length != 6 || o.Flat.Length != 6)) ||
                problem.Mandatory.Concat(problem.Bonus).Any(r => r.Stat < 0 || r.Stat > 8))
                throw new ArgumentException("Invalid gun-modification problem.");
            choices = new Option[problem.Slots.Length];
            next = new int[problem.Slots.Length];
            minPercent = new long[choices.Length + 1, 6]; maxPercent = new long[choices.Length + 1, 6];
            minFlat = new long[choices.Length + 1, 6]; maxFlat = new long[choices.Length + 1, 6];
            for (int slot = choices.Length - 1; slot >= 0; slot--) for (int stat = 0; stat < 6; stat++)
            {
                minPercent[slot, stat] = minPercent[slot + 1, stat] + problem.Slots[slot].Min(o => o.Percent[stat]);
                maxPercent[slot, stat] = maxPercent[slot + 1, stat] + problem.Slots[slot].Max(o => o.Percent[stat]);
                minFlat[slot, stat] = minFlat[slot + 1, stat] + problem.Slots[slot].Min(o => o.Flat[stat]);
                maxFlat[slot, stat] = maxFlat[slot + 1, stat] + problem.Slots[slot].Max(o => o.Flat[stat]);
            }
            if (choices.Length == 0) { Evaluate(); Complete = true; }
        }

        public void Step(int nodeBudget)
        {
            int visited = 0;
            while (!Complete && visited++ < Math.Max(1, nodeBudget))
            {
                if (Best.Nodes >= problem.MaxNodes)
                { Best.SearchLimited = true; Complete = true; break; }
                if (next[depth] >= problem.Slots[depth].Length)
                {
                    next[depth] = 0;
                    if (depth-- == 0) Complete = true;
                    continue;
                }
                var option = problem.Slots[depth][next[depth]++];
                Best.Nodes++;
                if (!Compatible(option)) continue;
                choices[depth] = option;
                if (depth == choices.Length - 1) Evaluate();
                else if (CanStillMeetMandatory(depth + 1)) { depth++; next[depth] = 0; }
            }
        }

        private bool Compatible(Option option)
        {
            if (option.Token < 0) return true;
            for (int i = 0; i < depth; i++)
            {
                var other = choices[i];
                if (other.Token < 0) continue;
                if (other.Token == option.Token) return false;
                if (option.ExclusiveGroup.Length != 0 && option.ExclusiveGroup == other.ExclusiveGroup) return false;
                if ((option.Special || other.Special) && option.Identifier == other.Identifier) return false;
            }
            return true;
        }

        private bool CanStillMeetMandatory(int assigned)
        {
            foreach (var requirement in problem.Mandatory)
            {
                if (requirement.Stat == 7)
                { if (!requirement.IsMet(problem.Caliber)) return false; continue; }
                if (requirement.Stat >= 6) continue;
                int index = requirement.Stat;
                long lowPercent = 100 + problem.ConditionPercent, highPercent = lowPercent;
                long lowFlat = 0, highFlat = 0;
                for (int i = 0; i < assigned; i++)
                {
                    lowPercent += choices[i].Percent[index]; highPercent += choices[i].Percent[index];
                    lowFlat += choices[i].Flat[index]; highFlat += choices[i].Flat[index];
                }
                lowPercent += minPercent[assigned, index]; highPercent += maxPercent[assigned, index];
                lowFlat += minFlat[assigned, index]; highFlat += maxFlat[assigned, index];
                long a = problem.BaseStats[index] * lowPercent / 100;
                long b = problem.BaseStats[index] * highPercent / 100;
                // Unknown third-party values that overflow native int math cannot use this bound.
                if (problem.BaseStats[index] * lowPercent < int.MinValue || problem.BaseStats[index] * lowPercent > int.MaxValue ||
                    problem.BaseStats[index] * highPercent < int.MinValue || problem.BaseStats[index] * highPercent > int.MaxValue) continue;
                long low = Math.Min(a, b) + lowFlat, high = Math.Max(a, b) + highFlat;
                if (requirement.Equal ? requirement.Value < low || requirement.Value > high :
                    requirement.Larger ? high <= requirement.Value : low >= requirement.Value) return false;
            }
            return true;
        }

        private void Evaluate()
        {
            var values = Calculate(problem.BaseStats, problem.ConditionPercent, problem.Caliber, problem.BaseCells, choices);
            if (problem.Mandatory.Any(r => !r.IsMet(values[r.Stat]))) return;
            int met = 0, added = 0, changes = 0;
            long reward = 0, cost = 0;
            foreach (var requirement in problem.Bonus)
                if (requirement.IsMet(values[requirement.Stat])) { met++; reward += requirement.Reward; }
            foreach (var option in choices)
            { if (option.IsNew && option.Token >= 0) { added++; cost += option.Cost; } changes += option.Changes; }
            bool better = Best.Choices == null || reward > Best.BonusReward ||
                reward == Best.BonusReward && (met > Best.BonusMet || met == Best.BonusMet &&
                (cost < Best.AddedCost || cost == Best.AddedCost && (added < Best.AddedCount ||
                added == Best.AddedCount && changes < Best.Changes)));
            if (!better || problem.Validate != null && !problem.Validate(choices)) return;
            Best.Choices = (Option[])choices.Clone(); Best.Values = values;
            Best.BonusMet = met; Best.BonusReward = reward; Best.AddedCost = cost;
            Best.AddedCount = added; Best.Changes = changes;
        }
    }

    internal static int[] Calculate(int[] baseStats, int condition, int caliber, long[] baseCells, IReadOnlyList<Option> choices)
    {
        var result = new int[9];
        for (int stat = 0; stat < 6; stat++)
        {
            int percent = 100 + condition, flat = 0;
            foreach (var option in choices) { percent += option.Percent[stat]; flat += option.Flat[stat]; }
            // Native GunHelper.UpdateCurrentGunStats uses integer truncation, with no clamping.
            result[stat] = baseStats[stat] * percent / 100 + flat;
        }
        var cells = new HashSet<long>(baseCells);
        foreach (var option in choices)
        {
            result[6] = Math.Max(result[6], option.Suppression);
            cells.UnionWith(option.ShapeCells);
        }
        result[7] = caliber; result[8] = cells.Count;
        return result;
    }
}
