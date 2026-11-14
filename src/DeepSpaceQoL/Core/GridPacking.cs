using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DeepSpaceQoL.Core
{
    // Geometry only. No Unity objects, item mutations or storage transfers occur here.
    internal readonly struct PackingCell
    {
        public readonly int X, Y;
        public readonly byte Layer;
        public PackingCell(int x, int y, byte layer = 1) { X = x; Y = y; Layer = layer; }
    }

    internal sealed class PackingShape
    {
        public int Orientation, Width, Height;
        public PackingCell[] Cells = Array.Empty<PackingCell>();
    }

    internal readonly struct PackingPlacement
    {
        public readonly int Item, X, Y, Shape;
        public PackingPlacement(int item, int x, int y, int shape)
        { Item = item; X = x; Y = y; Shape = shape; }
    }

    internal sealed class PackingItem
    {
        public string Group = "";
        public PackingShape[] Shapes = Array.Empty<PackingShape>();
        public PackingPlacement Original;
    }

    internal sealed class PackingProblem
    {
        public int Width, Height;
        public bool[] Usable = Array.Empty<bool>();
        public PackingCell[] Fixed = Array.Empty<PackingCell>();
        public PackingItem[] Items = Array.Empty<PackingItem>();
    }

    internal sealed class PackingResult
    {
        public bool Complete, Improved;
        public PackingPlacement[] Placements = Array.Empty<PackingPlacement>();
        public int Nodes;
        public long OriginalScore, Score;
    }

    internal static partial class GridPacking
    {
        // A bounded, deterministic multi-start search. A known complete original
        // is retained throughout; failed/expired searches never yield partial output.
        public static PackingResult Solve(PackingProblem problem,
            Func<PackingPlacement, bool>? nativeAllowed = null, int milliseconds = 220, int nodeLimit = 240000)
        {
            CheckProblem(problem);
            var baseline = problem.Items.Select(i => i.Original).ToArray();
            if (!Validate(problem, baseline))
                throw new ArgumentException("Original layout is not a complete valid layout.");
            var result = new PackingResult { Complete = true, Placements = baseline,
                OriginalScore = Score(problem, baseline), Score = Score(problem, baseline) };
            if (problem.Items.Length == 0) return result;
            var watch = Stopwatch.StartNew();
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
            var allowed = new Dictionary<(int, int, int, int), bool>();
            bool Expired() => result.Nodes >= nodeLimit || watch.ElapsedMilliseconds >= Math.Max(1, milliseconds);
            bool Native(PackingPlacement p)
            {
                if (nativeAllowed == null) return true;
                var key = (p.Item, p.X, p.Y, p.Shape);
                if (!allowed.TryGetValue(key, out bool ok)) allowed[key] = ok = nativeAllowed(p);
                return ok;
            }
            int pass = 0;
            // Pack repeated rectangular goods in aligned blocks before general polyomino
            // search. This produces visible rows/columns instead of scattered first fits.
            // No code is imported from other mods; all identities and native vetoes survive.
            foreach (var order in orders.Take(2))
            {
                if (Expired()) break;
                var occupied = FixedOccupancy(problem);
                var placed = new List<PackingPlacement>();
                var groups = order.GroupBy(i => problem.Items[i].Group, StringComparer.Ordinal).Select(g => g.ToArray()).ToArray();
                if (groups.Length > 96) continue; // Bound recursive depth even in oversized modded grids.
                int completed = 0;
                void Blocks(int depth)
                {
                    if (Expired() || completed >= 6) return;
                    if (depth == groups.Length) { Keep(placed); completed++; return; }
                    var group = groups[depth];
                    var blocks = BlockCandidates(problem, group, occupied, Native, Expired, () => result.Nodes++);
                    if (blocks.Count != 0)
                    {
                        foreach (var block in blocks.Take(depth < 5 ? 3 : 1))
                        {
                            foreach (var p in block) { placed.Add(p); Mark(problem, occupied, p, true); }
                            Blocks(depth + 1);
                            foreach (var p in block) { Mark(problem, occupied, p, false); placed.RemoveAt(placed.Count - 1); }
                            if (Expired() || completed >= 6) break;
                        }
                    }
                    else
                    {
                        int before = placed.Count;
                        foreach (int item in group)
                        {
                            var choices = Candidates(problem, item, occupied, placed, Native, Expired, () => result.Nodes++, true);
                            if (choices.Count == 0) break;
                            var p = choices[0]; placed.Add(p); Mark(problem, occupied, p, true);
                        }
                        if (placed.Count - before == group.Length) Blocks(depth + 1);
                        while (placed.Count > before) { Mark(problem, occupied, placed[placed.Count - 1], false); placed.RemoveAt(placed.Count - 1); }
                    }
                }
                Blocks(0);
            }
            foreach (var order in orders)
            {
                if (Expired()) break;
                var occupied = FixedOccupancy(problem);
                var placed = new List<PackingPlacement>();
                bool failed = false;
                foreach (int item in order)
                {
                    if (Expired()) { failed = true; break; }
                    // Establish a complete grouping-oriented layout cheaply
                    // before spending the remaining budget on alternate choices.
                    var candidates = Candidates(problem, item, occupied, placed, Native, Expired, () => result.Nodes++, pass < 2);
                    if (candidates.Count == 0) { failed = true; break; }
                    var p = candidates[0]; placed.Add(p); Mark(problem, occupied, p, true);
                }
                if (!failed) Keep(placed);
                pass++;
            }
            // For small fragmented inventories explore alternatives rather than
            // treating the first greedy dead end as proof that no layout exists.
            if (problem.Items.Length <= 60)
            foreach (var order in orders.Take(2))
            {
                if (Expired()) break;
                var occupied = FixedOccupancy(problem);
                var placed = new List<PackingPlacement>();
                void Search(int depth)
                {
                    if (Expired()) return;
                    if (depth == order.Length) { Keep(placed); return; }
                    var candidates = Candidates(problem, order[depth], occupied, placed, Native, Expired, () => result.Nodes++);
                    foreach (var p in candidates.Take(depth < 8 ? 3 : 1))
                    {
                        placed.Add(p); Mark(problem, occupied, p, true);
                        Search(depth + 1);
                        Mark(problem, occupied, p, false); placed.RemoveAt(placed.Count - 1);
                        if (Expired()) return;
                    }
                }
                Search(0);
            }
            result.Improved = result.Score < result.OriginalScore;
            return result;

            void Keep(List<PackingPlacement> placed)
            {
                if (placed.Count != problem.Items.Length || !Validate(problem, placed)) return;
                long score = Score(problem, placed);
                if (score < result.Score) { result.Score = score; result.Placements = placed.OrderBy(p => p.Item).ToArray(); }
            }
        }

        public static bool Validate(PackingProblem p, IReadOnlyList<PackingPlacement> placements)
        {
            if (placements.Count != p.Items.Length) return false;
            var seen = new HashSet<int>(); var occupied = FixedOccupancy(p);
            foreach (var entry in placements)
            {
                if (entry.Item < 0 || entry.Item >= p.Items.Length || !seen.Add(entry.Item) ||
                    entry.Shape < 0 || entry.Shape >= p.Items[entry.Item].Shapes.Length || !Fits(p, occupied, entry)) return false;
                Mark(p, occupied, entry, true);
            }
            return true;
        }

        private static List<PackingPlacement> Candidates(PackingProblem p, int item, HashSet<int> occupied,
            List<PackingPlacement> placed, Func<PackingPlacement, bool> native, Func<bool> expired, Action count, bool firstFit = false)
        {
            var ranked = new List<(PackingPlacement placement, long rank)>();
            // Compare all orientations at a location before going farther down the grid.
            // The old orientation-first loop could fill a column while leaving a row empty.
            for (int y = 0; y < p.Height; y++)
            for (int x = 0; x < p.Width; x++)
            for (int s = 0; s < p.Items[item].Shapes.Length; s++)
            {
                var shape = p.Items[item].Shapes[s];
                {
                    if (expired()) return ranked.OrderBy(v => v.rank).Select(v => v.placement).Take(8).ToList();
                    count(); var candidate = new PackingPlacement(item, x, y, s);
                    if (!Fits(p, occupied, candidate) || !native(candidate)) continue;
                    if (firstFit) return new List<PackingPlacement> { candidate };
                    int sameContact = 0, allContact = 0;
                    foreach (var c in shape.Cells.Where(c => c.Layer == 1))
                    {
                        int gx = x + c.X, gy = y + c.Y;
                        foreach (var d in Neighbours)
                        {
                            int nx = gx + d.X, ny = gy + d.Y;
                            if (nx < 0 || ny < 0 || nx >= p.Width || ny >= p.Height || occupied.Contains(Key(p, nx, ny, 1))) allContact++;
                        }
                    }
                    foreach (var other in placed)
                    {
                        if (p.Items[other.Item].Group != p.Items[item].Group) continue;
                        var os = p.Items[other.Item].Shapes[other.Shape];
                        if ((other.Y == y && (other.X + os.Width == x || x + shape.Width == other.X)) ||
                            (other.X == x && (other.Y + os.Height == y || y + shape.Height == other.Y))) sameContact++;
                    }
                    long rank = (long)y * p.Width * 100 + x * 100L - sameContact * 160L - allContact * 3L + s;
                    ranked.Add((candidate, rank));
                }
            }
            return ranked.OrderBy(v => v.rank).ThenBy(v => v.placement.Shape).Take(8).Select(v => v.placement).ToList();
        }

        private static List<List<PackingPlacement>> BlockCandidates(PackingProblem p, int[] items, HashSet<int> occupied,
            Func<PackingPlacement, bool> native, Func<bool> expired, Action count)
        {
            if (items.Length < 2) return new List<List<PackingPlacement>>();
            string Signature(PackingShape s) => s.Width + ":" + s.Height + ":" +
                string.Join(",", s.Cells.OrderBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Layer).Select(c => c.X + "." + c.Y + "." + c.Layer));
            var patterns = new List<(int[] shapes, int columns, int width, int height, int waste)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shape in p.Items[items[0]].Shapes)
            {
                if (shape.Cells.Length != shape.Width * shape.Height || shape.Cells.Any(c => c.Layer != 1)) continue;
                string key = Signature(shape);
                if (!seen.Add(key)) continue;
                int[] shapes = items.Select(i => Array.FindIndex(p.Items[i].Shapes, s => Signature(s) == key)).ToArray();
                if (shapes.Any(s => s < 0)) continue;
                for (int columns = 1; columns <= Math.Min(items.Length, p.Width / shape.Width); columns++)
                {
                    int rows = (items.Length + columns - 1) / columns;
                    if (rows * shape.Height > p.Height) continue;
                    patterns.Add((shapes, columns, columns * shape.Width, rows * shape.Height,
                        (columns * rows - items.Length) * shape.Width * shape.Height));
                }
            }
            var alternatives = new List<(List<PackingPlacement> block, long rank)>();
            foreach (var pattern in patterns.OrderBy(v => v.waste).ThenBy(v => v.height).ThenBy(v => v.width))
            {
            List<PackingPlacement>? best = null; long bestRank = long.MaxValue;
            for (int y = 0; y + pattern.height <= p.Height; y++)
            for (int x = 0; x + pattern.width <= p.Width; x++)
            {
                if (expired()) return alternatives.OrderBy(a => a.rank).Take(3).Select(a => a.block).ToList();
                count();
                long rank = y * p.Width * 1000L + pattern.waste * 200L + pattern.height * 20L + x * 10L;
                if (rank >= bestRank) continue;
                var block = new List<PackingPlacement>(items.Length);
                var tile = p.Items[items[0]].Shapes[pattern.shapes[0]];
                for (int j = 0; j < items.Length; j++)
                {
                    var candidate = new PackingPlacement(items[j], x + j % pattern.columns * tile.Width,
                        y + j / pattern.columns * tile.Height, pattern.shapes[j]);
                    if (!Fits(p, occupied, candidate) || !native(candidate)) break;
                    block.Add(candidate);
                }
                if (block.Count == items.Length) { best = block; bestRank = rank; }
            }
            if (best != null) alternatives.Add((best, bestRank));
            }
            return alternatives.OrderBy(a => a.rank).Take(3).Select(a => a.block).ToList();
        }

        private static readonly PackingCell[] Neighbours = { new PackingCell(-1, 0), new PackingCell(1, 0), new PackingCell(0, -1), new PackingCell(0, 1) };
        private static int Key(PackingProblem p, int x, int y, byte layer) => checked((y * p.Width + x) * 256 + layer);
        private static HashSet<int> FixedOccupancy(PackingProblem p) => new HashSet<int>(p.Fixed.Where(c => c.Layer != 0).Select(c => Key(p, c.X, c.Y, c.Layer)));
        private static bool Fits(PackingProblem p, HashSet<int> occupied, PackingPlacement placement)
        {
            var shape = p.Items[placement.Item].Shapes[placement.Shape];
            if (placement.X < 0 || placement.Y < 0 || placement.X + shape.Width > p.Width || placement.Y + shape.Height > p.Height) return false;
            foreach (var c in shape.Cells)
            {
                int x = placement.X + c.X, y = placement.Y + c.Y;
                if (c.Layer == 1 && !p.Usable[y * p.Width + x]) return false;
                if (c.Layer != 0 && occupied.Contains(Key(p, x, y, c.Layer))) return false;
            }
            return true;
        }
        private static void Mark(PackingProblem p, HashSet<int> occupied, PackingPlacement entry, bool add)
        {
            foreach (var c in p.Items[entry.Item].Shapes[entry.Shape].Cells)
            {
                if (c.Layer == 0) continue;
                int key = Key(p, entry.X + c.X, entry.Y + c.Y, c.Layer);
                if (add) occupied.Add(key); else occupied.Remove(key);
            }
        }
        private static long Score(PackingProblem p, IReadOnlyList<PackingPlacement> entries)
        {
            int rotation = 0, last = -1, regions = 0, inconsistent = 0;
            long groupWaste = 0;
            var solid = new HashSet<int>(p.Fixed.Where(c => c.Layer == 1).Select(c => c.Y * p.Width + c.X));
            var groups = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                var item = p.Items[e.Item];
                if (e.Shape != item.Original.Shape) rotation++;
                if (!groups.TryGetValue(item.Group, out var cells)) groups[item.Group] = cells = new HashSet<int>();
                foreach (var c in item.Shapes[e.Shape].Cells.Where(c => c.Layer == 1))
                { int cell = (e.Y + c.Y) * p.Width + e.X + c.X; solid.Add(cell); cells.Add(cell); }
            }
            foreach (var pair in groups)
            {
                var cells = pair.Value;
                if (cells.Count == 0) continue;
                groupWaste += (cells.Max(c => c % p.Width) - cells.Min(c => c % p.Width) + 1) *
                    (cells.Max(c => c / p.Width) - cells.Min(c => c / p.Width) + 1) - cells.Count;
                var pending = new HashSet<int>(cells);
                int components = 0;
                while (pending.Count > 0)
                {
                    components++; int first = pending.First(); pending.Remove(first);
                    var queue = new Queue<int>(); queue.Enqueue(first);
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue(), x = current % p.Width, y = current / p.Width;
                        foreach (var d in Neighbours)
                        {
                            int nx = x + d.X, ny = y + d.Y;
                            if (nx >= 0 && nx < p.Width && ny >= 0 && ny < p.Height && pending.Remove(ny * p.Width + nx))
                                queue.Enqueue(ny * p.Width + nx);
                        }
                    }
                }
                regions += Math.Max(0, components - 1);
                var members = entries.Where(e => p.Items[e.Item].Group == pair.Key).ToArray();
                var counts = members.GroupBy(e => {
                    var s = p.Items[e.Item].Shapes[e.Shape]; return (s.Width, s.Height);
                }).Select(g => g.Count()).ToArray();
                inconsistent += members.Length - counts.Max();
            }
            if (solid.Count != 0) last = solid.Max();
            int holes = 0;
            for (int i = 0; i <= last; i++) if (p.Usable[i] && !solid.Contains(i)) holes++;
            return regions * 100000000000L + groupWaste * 10000000L + inconsistent * 1000000L +
                holes * 1000L + Math.Max(last, 0) * 5L + rotation;
        }
        private static void CheckProblem(PackingProblem p)
        {
            if (p.Width <= 0 || p.Height <= 0 || (long)p.Width * p.Height > 65536 || p.Usable.Length != p.Width * p.Height)
                throw new ArgumentException("Invalid grid geometry.");
            foreach (var c in p.Fixed)
                if (c.X < 0 || c.Y < 0 || c.X >= p.Width || c.Y >= p.Height) throw new ArgumentException("Fixed item out of bounds.");
            for (int i = 0; i < p.Items.Length; i++)
            {
                var item = p.Items[i];
                if (item.Shapes.Length == 0 || item.Original.Item != i) throw new ArgumentException("Missing item shapes or identity.");
                foreach (var s in item.Shapes)
                    if (s.Width <= 0 || s.Height <= 0 || s.Cells.Length == 0 || s.Cells.Any(c => c.X < 0 || c.Y < 0 || c.X >= s.Width || c.Y >= s.Height))
                        throw new ArgumentException("Invalid item shape.");
            }
        }
    }
}
