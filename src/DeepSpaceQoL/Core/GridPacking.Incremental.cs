using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DeepSpaceQoL.Core
{
    internal static partial class GridPacking
    {
        /// <summary>Resumable version of the same block / multi-start search. Every native
        /// predicate runs on the caller's thread. CPU budget excludes time between frames.</summary>
        internal static IEnumerable<object?> SolveSteps(PackingProblem problem, Action<PackingResult> completed,
            Func<PackingPlacement, bool>? nativeAllowed = null, Func<bool>? cancelled = null,
            int milliseconds = 220, int nodeLimit = 240000, double sliceMilliseconds = 3)
        {
            CheckProblem(problem);
            var original = problem.Items.Select(i => i.Original).ToArray();
            if (!Validate(problem, original)) throw new ArgumentException("Original layout is not a complete valid layout.");
            var result = new PackingResult { Complete = true, Placements = original,
                OriginalScore = Score(problem, original), Score = Score(problem, original) };
            var cpu = new Stopwatch();
            var slice = Stopwatch.StartNew();
            bool Expired() => result.Nodes >= nodeLimit || cpu.Elapsed.TotalMilliseconds >= Math.Max(1, milliseconds) || (cancelled?.Invoke() ?? false);
            var allowed = new Dictionary<(int, int, int, int), bool>();
            bool Native(PackingPlacement p)
            {
                if (nativeAllowed == null) return true;
                var key = (p.Item, p.X, p.Y, p.Shape);
                if (!allowed.TryGetValue(key, out bool ok)) allowed[key] = ok = nativeAllowed(p);
                return ok;
            }
            void Keep(List<PackingPlacement> placed)
            {
                if (placed.Count != problem.Items.Length || !Validate(problem, placed)) return;
                long score = Score(problem, placed);
                if (score < result.Score) { result.Score = score; result.Placements = placed.OrderBy(p => p.Item).ToArray(); }
            }
            IEnumerable<int> CandidatesSteps(int item, HashSet<int> occupied, List<PackingPlacement> placed,
                bool firstFit, Action<List<PackingPlacement>> done)
            {
                var ranked = new List<(PackingPlacement placement, long rank)>();
                for (int y = 0; y < problem.Height; y++)
                for (int x = 0; x < problem.Width; x++)
                for (int s = 0; s < problem.Items[item].Shapes.Length; s++)
                {
                    if (Expired()) { done(ranked.OrderBy(v => v.rank).Select(v => v.placement).Take(8).ToList()); yield break; }
                    result.Nodes++;
                    if ((result.Nodes & 15) == 0) yield return 0;
                    var candidate = new PackingPlacement(item, x, y, s);
                    if (!Fits(problem, occupied, candidate) || !Native(candidate)) continue;
                    if (firstFit) { done(new List<PackingPlacement> { candidate }); yield break; }
                    var shape = problem.Items[item].Shapes[s];
                    int sameContact = 0, allContact = 0;
                    foreach (var c in shape.Cells.Where(c => c.Layer == 1))
                        foreach (var d in Neighbours)
                        {
                            int nx = x + c.X + d.X, ny = y + c.Y + d.Y;
                            if (nx < 0 || ny < 0 || nx >= problem.Width || ny >= problem.Height || occupied.Contains(Key(problem, nx, ny, 1))) allContact++;
                        }
                    foreach (var other in placed)
                    {
                        if (problem.Items[other.Item].Group != problem.Items[item].Group) continue;
                        var os = problem.Items[other.Item].Shapes[other.Shape];
                        if ((other.Y == y && (other.X + os.Width == x || x + shape.Width == other.X)) ||
                            (other.X == x && (other.Y + os.Height == y || y + shape.Height == other.Y))) sameContact++;
                    }
                    ranked.Add((candidate, (long)y * problem.Width * 100 + x * 100L - sameContact * 160L - allContact * 3L + s));
                }
                done(ranked.OrderBy(v => v.rank).ThenBy(v => v.placement.Shape).Take(8).Select(v => v.placement).ToList());
            }
            IEnumerable<int> BlockSteps(int[] items, HashSet<int> occupied, Action<List<List<PackingPlacement>>> done)
            {
                if (items.Length < 2) { done(new List<List<PackingPlacement>>()); yield break; }
                string Signature(PackingShape s) => s.Width + ":" + s.Height + ":" +
                    string.Join(",", s.Cells.OrderBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Layer).Select(c => c.X + "." + c.Y + "." + c.Layer));
                var patterns = new List<(int[] shapes, int columns, int width, int height, int waste)>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var shape in problem.Items[items[0]].Shapes)
                {
                    if (shape.Cells.Length != shape.Width * shape.Height || shape.Cells.Any(c => c.Layer != 1)) continue;
                    string key = Signature(shape);
                    if (!seen.Add(key)) continue;
                    int[] shapes = items.Select(i => Array.FindIndex(problem.Items[i].Shapes, s => Signature(s) == key)).ToArray();
                    if (shapes.Any(s => s < 0)) continue;
                    for (int columns = 1; columns <= Math.Min(items.Length, problem.Width / shape.Width); columns++)
                    {
                        int rows = (items.Length + columns - 1) / columns;
                        if (rows * shape.Height <= problem.Height)
                            patterns.Add((shapes, columns, columns * shape.Width, rows * shape.Height, (columns * rows - items.Length) * shape.Width * shape.Height));
                    }
                }
                var alternatives = new List<(List<PackingPlacement> block, long rank)>();
                foreach (var pattern in patterns.OrderBy(v => v.waste).ThenBy(v => v.height).ThenBy(v => v.width))
                {
                    List<PackingPlacement>? best = null; long bestRank = long.MaxValue;
                    for (int y = 0; y + pattern.height <= problem.Height; y++)
                    for (int x = 0; x + pattern.width <= problem.Width; x++)
                    {
                        if (Expired()) { done(alternatives.OrderBy(a => a.rank).Take(3).Select(a => a.block).ToList()); yield break; }
                        result.Nodes++;
                        yield return 0;
                        long rank = y * problem.Width * 1000L + pattern.waste * 200L + pattern.height * 20L + x * 10L;
                        if (rank >= bestRank) continue;
                        var block = new List<PackingPlacement>(items.Length);
                        var tile = problem.Items[items[0]].Shapes[pattern.shapes[0]];
                        for (int j = 0; j < items.Length; j++)
                        {
                            var candidate = new PackingPlacement(items[j], x + j % pattern.columns * tile.Width,
                                y + j / pattern.columns * tile.Height, pattern.shapes[j]);
                            if (!Fits(problem, occupied, candidate) || !Native(candidate)) break;
                            block.Add(candidate);
                            if ((j & 7) == 7) yield return 0;
                        }
                        if (block.Count == items.Length) { best = block; bestRank = rank; }
                    }
                    if (best != null) alternatives.Add((best, bestRank));
                }
                done(alternatives.OrderBy(a => a.rank).Take(3).Select(a => a.block).ToList());
            }
            IEnumerable<int> Search()
            {
                var indices = Enumerable.Range(0, problem.Items.Length).ToArray();
                int Area(int i) => problem.Items[i].Shapes[0].Cells.Count(c => c.Layer == 1);
                int Extent(int i) => problem.Items[i].Shapes[0].Width * problem.Items[i].Shapes[0].Height;
                var groupArea = indices.GroupBy(i => problem.Items[i].Group, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(Area), StringComparer.Ordinal);
                var orders = new[] {
                    indices.OrderBy(i => problem.Items[i].Group, StringComparer.Ordinal).ThenByDescending(Extent).ThenBy(i => i).ToArray(),
                    indices.OrderByDescending(i => groupArea[problem.Items[i].Group]).ThenBy(i => problem.Items[i].Group, StringComparer.Ordinal).ThenByDescending(Extent).ThenBy(i => i).ToArray(),
                    indices.OrderByDescending(Extent).ThenBy(i => problem.Items[i].Group, StringComparer.Ordinal).ThenBy(i => i).ToArray(),
                    indices.OrderByDescending(i => problem.Items[i].Shapes[0].Height).ThenBy(i => problem.Items[i].Group, StringComparer.Ordinal).ThenByDescending(Area).ThenBy(i => i).ToArray()
                };
                foreach (var order in orders.Take(2))
                {
                    if (Expired()) break;
                    var occupied = FixedOccupancy(problem); var placed = new List<PackingPlacement>();
                    var groups = order.GroupBy(i => problem.Items[i].Group, StringComparer.Ordinal).Select(g => g.ToArray()).ToArray();
                    if (groups.Length > 96) continue;
                    int completedLayouts = 0;
                    IEnumerable<int> Blocks(int depth)
                    {
                        if (Expired() || completedLayouts >= 6) yield break;
                        if (depth == groups.Length) { Keep(placed); completedLayouts++; yield return 0; yield break; }
                        var group = groups[depth]; var blocks = new List<List<PackingPlacement>>();
                        foreach (int step in BlockSteps(group, occupied, b => blocks = b)) yield return step;
                        if (blocks.Count != 0)
                        {
                            foreach (var block in blocks.Take(depth < 5 ? 3 : 1))
                            {
                                foreach (var p in block) { placed.Add(p); Mark(problem, occupied, p, true); }
                                foreach (int step in Blocks(depth + 1)) yield return step;
                                foreach (var p in block) { Mark(problem, occupied, p, false); placed.RemoveAt(placed.Count - 1); }
                                if (Expired() || completedLayouts >= 6) break;
                            }
                        }
                        else
                        {
                            int before = placed.Count;
                            foreach (int item in group)
                            {
                                var choices = new List<PackingPlacement>();
                                foreach (int step in CandidatesSteps(item, occupied, placed, true, c => choices = c)) yield return step;
                                if (choices.Count == 0) break;
                                var p = choices[0]; placed.Add(p); Mark(problem, occupied, p, true);
                            }
                            if (placed.Count - before == group.Length) foreach (int step in Blocks(depth + 1)) yield return step;
                            while (placed.Count > before) { Mark(problem, occupied, placed[placed.Count - 1], false); placed.RemoveAt(placed.Count - 1); }
                        }
                    }
                    foreach (int step in Blocks(0)) yield return step;
                }
                int pass = 0;
                foreach (var order in orders)
                {
                    if (Expired()) break;
                    var occupied = FixedOccupancy(problem); var placed = new List<PackingPlacement>(); bool failed = false;
                    foreach (int item in order)
                    {
                        if (Expired()) { failed = true; break; }
                        var choices = new List<PackingPlacement>();
                        foreach (int step in CandidatesSteps(item, occupied, placed, pass < 2, c => choices = c)) yield return step;
                        if (choices.Count == 0) { failed = true; break; }
                        var p = choices[0]; placed.Add(p); Mark(problem, occupied, p, true);
                    }
                    if (!failed) Keep(placed);
                    pass++;
                    yield return 0;
                }
                if (problem.Items.Length <= 60)
                foreach (var order in orders.Take(2))
                {
                    if (Expired()) break;
                    var occupied = FixedOccupancy(problem); var placed = new List<PackingPlacement>();
                    IEnumerable<int> Backtrack(int depth)
                    {
                        if (Expired()) yield break;
                        if (depth == order.Length) { Keep(placed); yield return 0; yield break; }
                        var choices = new List<PackingPlacement>();
                        foreach (int step in CandidatesSteps(order[depth], occupied, placed, false, c => choices = c)) yield return step;
                        foreach (var p in choices.Take(depth < 8 ? 3 : 1))
                        {
                            placed.Add(p); Mark(problem, occupied, p, true);
                            foreach (int step in Backtrack(depth + 1)) yield return step;
                            Mark(problem, occupied, p, false); placed.RemoveAt(placed.Count - 1);
                            if (Expired()) yield break;
                        }
                    }
                    foreach (int step in Backtrack(0)) yield return step;
                }
            }
            using (var work = Search().GetEnumerator())
                while (true)
                {
                    if (cancelled?.Invoke() ?? false) yield break;
                    cpu.Start(); bool more;
                    try { more = work.MoveNext(); }
                    finally { cpu.Stop(); }
                    if (!more) break;
                    if (slice.Elapsed.TotalMilliseconds >= Math.Max(0.05, sliceMilliseconds))
                    { yield return null; slice.Restart(); }
                }
            if (cancelled?.Invoke() ?? false) yield break;
            result.Improved = result.Score < result.OriginalScore;
            completed(result);
        }
    }
}
