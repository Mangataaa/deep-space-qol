using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceQoL.Core;

internal static class StockPlannerTests
{
    private static StockPlanner.Candidate Item(int token, long value, string kind, params int[][] cells) =>
        new() { Token = token, Value = value, VarietyKey = kind, Options = cells.Select((c, i) =>
            new StockPlanner.Option { Token = i, Cells = c }).ToArray() };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Validate(StockPlanner.Result result, IReadOnlyList<StockPlanner.Candidate> items,
        IEnumerable<int> occupied, long budget, bool variety)
    {
        long sum = 0;
        var used = new HashSet<int>(occupied);
        var kinds = new HashSet<string>();
        var instances = new HashSet<int>();
        foreach (var choice in result.Choices)
        {
            var item = items.Single(i => i.Token == choice.CandidateToken);
            Check(instances.Add(item.Token), "One original instance selected twice.");
            Check(!variety || kinds.Add(item.VarietyKey), "Duplicate variety selected.");
            foreach (int cell in item.Options.Single(o => o.Token == choice.OptionToken).Cells)
                Check(used.Add(cell), "Planned overlap.");
            sum += item.Value;
        }
        Check(sum == result.Value && sum <= budget, "Budget mismatch or excess.");
    }

    public static int Main()
    {
        var pack = new[]
        {
            Item(0, 70, "large", new[]{0,1,2}, new[]{3,4,5}),
            Item(1, 60, "a", new[]{0,1}, new[]{2,3}, new[]{4,5}),
            Item(2, 40, "b", new[]{0,1}, new[]{2,3}, new[]{4,5})
        };
        var exact = StockPlanner.Solve(6, Array.Empty<int>(), pack, 100, false);
        Check(exact.Value == 100, "Must choose 60+40 instead of largest item greedily.");
        Validate(exact, pack, Array.Empty<int>(), 100, false);

        var tinyPack = new[] { Item(0, 70, "large", new[]{0,1,2}), Item(1, 60, "a", new[]{0,1}), Item(2, 40, "b", new[]{1,2}) };
        var tinyCounter = StockPlanner.Solve(3, Array.Empty<int>(), tinyPack, 150, false);
        Check(tinyCounter.Value == 70, "Limited space should select the greatest sale value.");
        Validate(tinyCounter, tinyPack, Array.Empty<int>(), 150, false);

        var duplicates = new[]
        {
            Item(0, 55, "same", new[]{0}, new[]{1}, new[]{2}),
            Item(1, 45, "same", new[]{0}, new[]{1}, new[]{2}),
            Item(2, 40, "other", new[]{0}, new[]{1}, new[]{2})
        };
        var variety = StockPlanner.Solve(3, Array.Empty<int>(), duplicates, 100, true);
        Check(variety.Value == 95, "Variety buyer may select one per identifier.");
        Validate(variety, duplicates, Array.Empty<int>(), 100, true);
        var alreadyStaged = StockPlanner.Solve(3, new[]{0}, duplicates, 45, true, new[]{"same"});
        Check(alreadyStaged.Value == 40, "Already staged variety must be reserved.");
        Validate(alreadyStaged, duplicates, new[]{0}, 45, true);

        var highBits = new[] { Item(0, 10, "a", new[]{64,129}), Item(1, 20, "b", new[]{129}), Item(2, 5, "c", new[]{128}) };
        var wide = StockPlanner.Solve(130, new[]{64}, highBits, 25, false);
        Check(wide.Value == 25, "Occupancy across 64-bit word boundaries failed.");
        Validate(wide, highBits, new[]{64}, 25, false);
        Check(StockPlanner.Solve(3, Array.Empty<int>(), pack, 0, false).Value == 0, "Exhausted budget must return empty.");
        Check(StockPlanner.Solve(6, new[]{0,1,2,3,4,5}, pack, 100, false).Value == 0, "Full counter must return empty.");
        Check(StockPlanner.Solve(6, Array.Empty<int>(), Array.Empty<StockPlanner.Candidate>(), 100, false).Choices.Count == 0,
            "Empty inventory must return an empty plan.");

        var random = new Random(917);
        for (int run = 0; run < 120; run++)
        {
            var items = Enumerable.Range(0, 12).Select(i => Item(i, random.Next(1, 80), "kind" + (i % 4),
                new[]{random.Next(0, 16)}, new[]{random.Next(0, 16)})).ToArray();
            var occupied = Enumerable.Range(0, 3).Select(_ => random.Next(0, 16)).Distinct().ToArray();
            long budget = random.Next(0, 250);
            bool requireVariety = (run % 2) == 0;
            var result = StockPlanner.Solve(16, occupied, items, budget, requireVariety, milliseconds: 3000);
            Validate(result, items, occupied, budget, requireVariety);
        }
        Console.WriteLine("StockPlanner: 8 deterministic scenarios + 120 seeded invariant scenarios passed.");
        return 0;
    }
}
