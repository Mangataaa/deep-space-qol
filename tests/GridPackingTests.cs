using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceQoL.Core;

internal static class GridPackingTests
{
    private static int checks;
    public static void Main()
    {
        GroupAndCompact(); FullDenseGrouping(); PinnedAndIrregular(); LayeredAndIrregularMasks(); Layers(); NativePredicate(); FailedAndTimedOut(); Randomized();
        Console.WriteLine($"GridPacking: {checks} checks passed.");
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
