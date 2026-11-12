using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using DeepSpaceQoL.Core;

internal static class GridPackingTests
{
    private static int checks;
    public static void Main(string[] args)
    {
        if (args.Length > 0) { Replay(args); return; }
        GroupAndCompact(); FullDenseGrouping(); RepeatedBlocks(); MixedRectangles(); PinnedAndIrregular(); LayeredAndIrregularMasks(); Layers(); NativePredicate(); FailedAndTimedOut(); Randomized();
        Console.WriteLine($"GridPacking: {checks} checks passed.");
    }
    private static void Replay(string[] args)
    {
        var results = new List<object>();
        using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
            PackingCell[] Cells(JsonElement e) => e.EnumerateArray().Select(c => new PackingCell(Int(c, "X"), Int(c, "Y"), (byte)Int(c, "Layer"))).ToArray();
            var p = Problem(Int(entry, "Width"), Int(entry, "Height"));
            p.Usable = entry.GetProperty("Usable").EnumerateArray().Select(e => e.GetBoolean()).ToArray();
            p.Fixed = Cells(entry.GetProperty("Fixed"));
            p.Items = entry.GetProperty("Items").EnumerateArray().Select(e => {
                var o = e.GetProperty("Original");
                return new PackingItem { Group = e.GetProperty("Group").GetString()!,
                    Original = new PackingPlacement(Int(o, "Item"), Int(o, "X"), Int(o, "Y"), Int(o, "Shape")),
                    Shapes = e.GetProperty("Shapes").EnumerateArray().Select(s => new PackingShape {
                        Width = Int(s, "Width"), Height = Int(s, "Height"), Orientation = Int(s, "Orientation"), Cells = Cells(s.GetProperty("Cells")) }).ToArray() };
            }).ToArray();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var r = GridPacking.Solve(p);
            Check(r.Complete && GridPacking.Validate(p, r.Placements), "Replay must conserve every real item and avoid collisions");
            Check(r.Score <= r.OriginalScore, "Replay must not degrade baseline");
            var name = entry.GetProperty("Name").GetString();
            Console.WriteLine($"{name}: items={p.Items.Length}, improved={r.Improved}, score={r.OriginalScore}->{r.Score}, nodes={r.Nodes}, ms={watch.ElapsedMilliseconds}");
            results.Add(new { name, r.Complete, r.Improved, r.OriginalScore, r.Score,
                placements = r.Placements.Select(v => new { v.Item, v.X, v.Y, v.Shape }).ToArray() });
        }
        if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(results));
    }
    private static PackingShape Rect(int width, int height, int orientation = 0) => new PackingShape {
        Width = width, Height = height, Orientation = orientation,
        Cells = Enumerable.Range(0, width * height).Select(i => new PackingCell(i % width, i / width)).ToArray()
    };
    private static PackingProblem Problem(int w, int h) => new PackingProblem { Width = w, Height = h, Usable = Enumerable.Repeat(true, w * h).ToArray() };
    private static PackingItem Item(int index, string group, int x, int y, params PackingShape[] shapes) => new PackingItem {
        Group = group, Shapes = shapes, Original = new PackingPlacement(index, x, y, 0)
    };
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static void GroupAndCompact()
    {
        var p = Problem(6, 4);
        p.Items = new[] { Item(0, "food", 0, 0, Rect(1, 1)), Item(1, "water", 4, 0, Rect(1, 1)),
            Item(2, "food", 1, 3, Rect(1, 1)), Item(3, "water", 5, 3, Rect(1, 1)) };
        var result = GridPacking.Solve(p, milliseconds: 1000);
        Check(result.Complete && result.Improved, "scattered items should improve");
        Check(GridPacking.Validate(p, result.Placements), "grouped layout must be valid");
        var ordered = result.Placements.OrderBy(e => e.Y).ThenBy(e => e.X).ToArray();
        Check(p.Items[ordered[0].Item].Group == p.Items[ordered[1].Item].Group, "same identifiers should be adjacent");
        Check(result.Placements.Max(e => e.Y) == 0, "small items should occupy first row");
    }
    private static void FullDenseGrouping()
    {
        var p = Problem(10, 10);
        p.Items = Enumerable.Range(0, 100).Select(i => Item(i, "type" + (i % 5), i % 10, i / 10, Rect(1, 1))).ToArray();
        var result = GridPacking.Solve(p);
        Check(result.Complete && result.Improved, "full dense inventory should group within default budget");
        Check(GridPacking.Validate(p, result.Placements), "full dense output lost an item or overlapped");
    }
    private static void RepeatedBlocks()
    {
        var p = Problem(17, 10);
        p.Items = Enumerable.Range(0, 24).Select(i => Item(i, i % 2 == 0 ? "water" : "food", i % 8 * 2, i / 8 * 3,
            Rect(2, 3), Rect(3, 2, 1))).ToArray();
        var r = GridPacking.Solve(p);
        Check(r.Improved && GridPacking.Validate(p, r.Placements), "Repeated goods must form complete aligned blocks within default budget");
        foreach (var group in r.Placements.GroupBy(e => p.Items[e.Item].Group))
        {
            Check(group.Select(e => (p.Items[e.Item].Shapes[e.Shape].Width, p.Items[e.Item].Shapes[e.Shape].Height)).Distinct().Count() == 1,
                "Same-shape goods should use one consistent orientation");
            var cells = group.SelectMany(e => p.Items[e.Item].Shapes[e.Shape].Cells.Select(c => (X: e.X + c.X, Y: e.Y + c.Y))).ToArray();
            Check((cells.Max(c => c.X) - cells.Min(c => c.X) + 1) * (cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1) == cells.Length,
                "Repeated rectangular goods should fill their group rectangle without internal gaps");
        }
        foreach (var e in r.Placements) p.Items[e.Item].Original = e;
        var second = GridPacking.Solve(p);
        Check(!second.Improved, "An already aligned compact block layout should remain stable on repeat");
    }

    private static void MixedRectangles()
    {
        var random = new Random(23971);
        for (int run = 0; run < 40; run++)
        {
            var p = Problem(17, 10); var stock = new List<PackingItem>();
            // Known complete shelf baseline, with alternating types, long goods and squares.
            for (int y = 0; y < 9; y += 3)
            for (int x = 0; x < 15; x += 3)
            {
                int width = random.Next(1, 4), height = random.Next(1, 4);
                stock.Add(Item(stock.Count, "mixed" + random.Next(4), x, y, Rect(width, height), Rect(height, width, 1)));
            }
            p.Items = stock.ToArray();
            var r = GridPacking.Solve(p, nodeLimit: 60000);
            Check(r.Complete && GridPacking.Validate(p, r.Placements), "Mixed-size packing must conserve every item and never overlap");
            Check(r.Score <= r.OriginalScore, "Mixed-size packing must never reduce measured layout quality");
            Check(r.Nodes <= 60000, "Mixed-size packing must respect its work budget");
        }
    }

    private static void PinnedAndIrregular()
    {
        var p = Problem(5, 4); p.Usable[0] = false; p.Usable[4] = false;
        p.Fixed = new[] { new PackingCell(2, 1), new PackingCell(2, 2) };
        p.Items = new[] { Item(0, "A", 0, 2, Rect(2, 1), Rect(1, 2, 1)), Item(1, "B", 3, 2, Rect(2, 1), Rect(1, 2, 1)) };
        var fixedBefore = p.Fixed.ToArray(); var usableBefore = p.Usable.ToArray();
        var result = GridPacking.Solve(p, milliseconds: 1000);
        Check(GridPacking.Validate(p, result.Placements), "fixed/irregular layout invalid");
        Check(p.Fixed.SequenceEqual(fixedBefore) && p.Usable.SequenceEqual(usableBefore), "solver mutated fixed grid");
        foreach (var entry in result.Placements) foreach (var c in p.Items[entry.Item].Shapes[entry.Shape].Cells)
            Check(!p.Fixed.Any(f => f.X == entry.X + c.X && f.Y == entry.Y + c.Y), "pin moved or collided");
    }
    private static void Layers()
    {
        var p = Problem(2, 2);
        var layer2 = new PackingShape { Width = 1, Height = 1, Cells = new[] { new PackingCell(0, 0, 2) } };
        p.Fixed = new[] { new PackingCell(0, 0, 1) };
        p.Items = new[] { Item(0, "layer", 0, 0, layer2) };
        Check(GridPacking.Validate(p, new[] { p.Items[0].Original }), "different game layers may coexist");
        p.Fixed = new[] { new PackingCell(0, 0, 2) };
        Check(!GridPacking.Validate(p, new[] { p.Items[0].Original }), "same layers must collide");
    }
    private static void LayeredAndIrregularMasks()
    {
        var p = Problem(4, 4);
        p.Items = new[] { Item(0, "bar", 2, 3, Rect(2, 1), Rect(1, 2, 1)) };
        var rotated = GridPacking.Solve(p, e => e.Shape == 1, milliseconds: 1000);
        Check(rotated.Improved && rotated.Placements[0].Shape == 1, "rotation-only native placement was not found");
        Check(GridPacking.Validate(p, rotated.Placements), "rotated geometry invalid");
        var l = new PackingShape { Width = 2, Height = 2, Cells = new[] { new PackingCell(0, 0), new PackingCell(0, 1), new PackingCell(1, 1) } };
        p.Items = new[] { Item(0, "L", 2, 2, l), Item(1, "dot", 3, 2, Rect(1, 1)) };
        Check(GridPacking.Validate(p, p.Items.Select(i => i.Original).ToArray()), "irregular hole must allow another item");
        var irregular = GridPacking.Solve(p, milliseconds: 1000);
        Check(irregular.Complete && GridPacking.Validate(p, irregular.Placements), "irregular layout did not remain complete/valid");
    }
    private static void NativePredicate()
    {
        var p = Problem(4, 3);
        p.Items = new[] { Item(0, "a", 3, 2, Rect(1, 1)), Item(1, "a", 2, 2, Rect(1, 1)) };
        var result = GridPacking.Solve(p, x => x.Y >= 1, milliseconds: 1000);
        Check(result.Placements.All(x => x.Y >= 1), "native state constraint ignored");
        Check(GridPacking.Validate(p, result.Placements), "native-constrained output invalid");
    }
    private static void FailedAndTimedOut()
    {
        var p = Problem(4, 4);
        p.Items = new[] { Item(0, "a", 3, 3, Rect(1, 1)), Item(1, "a", 2, 3, Rect(1, 1)) };
        var original = p.Items.Select(i => i.Original).ToArray();
        var result = GridPacking.Solve(p, _ => false, milliseconds: 1000);
        Check(result.Complete && !result.Improved && result.Placements.SequenceEqual(original), "failed search lost original");
        var expired = GridPacking.Solve(p, nodeLimit: 0);
        Check(expired.Complete && expired.Placements.SequenceEqual(original), "expired search yielded partial layout");
        Check(!GridPacking.Validate(p, new[] { original[0], original[0] }), "duplicate identity accepted");
    }
    private static void Randomized()
    {
        var random = new Random(71937);
        for (int run = 0; run < 100; run++)
        {
            var p = Problem(7, 6);
            var positions = Enumerable.Range(0, 42).OrderBy(_ => random.Next()).ToArray();
            int count = random.Next(2, 29);
            p.Items = positions.Take(count).Select((pos, i) => Item(i, "type" + (i % 4), pos % 7, pos / 7, Rect(1, 1))).ToArray();
            p.Fixed = positions.Skip(count).Take(3).Select(pos => new PackingCell(pos % 7, pos / 7)).ToArray();
            var original = p.Items.Select(i => i.Original).ToArray();
            var result = GridPacking.Solve(p, milliseconds: 1000, nodeLimit: 16000);
            Check(result.Complete && result.Placements.Length == count, "randomized incomplete output");
            Check(GridPacking.Validate(p, result.Placements), "randomized collision or loss");
            Check(result.Score <= result.OriginalScore, "randomized layout quality regressed");
            Check(p.Items.Select(i => i.Original).SequenceEqual(original), "randomized solver mutated input");
        }
    }
}
