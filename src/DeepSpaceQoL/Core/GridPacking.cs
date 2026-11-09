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

    internal static class GridPacking
    {
        // A bounded, deterministic multi-start search. A known complete original
        // is retained throughout; failed/expired searches never yield partial output.
        public static PackingResult Solve(PackingProblem problem,
            Func<PackingPlacement, bool>? nativeAllowed = null, int milliseconds = 120, int nodeLimit = 12000)
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
            for (int s = 0; s < p.Items[item].Shapes.Length; s++)
            {
                var shape = p.Items[item].Shapes[s];
                for (int y = 0; y + shape.Height <= p.Height; y++)
                for (int x = 0; x + shape.Width <= p.Width; x++)
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
            int runs = 0, rotation = 0, last = -1; string? group = null;
            var solid = new HashSet<int>(p.Fixed.Where(c => c.Layer == 1).Select(c => c.Y * p.Width + c.X));
            foreach (var e in entries.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Item))
            {
                var item = p.Items[e.Item];
                if (item.Group != group) { runs++; group = item.Group; }
                if (e.Shape != item.Original.Shape) rotation++;
                foreach (var c in item.Shapes[e.Shape].Cells.Where(c => c.Layer == 1)) solid.Add((e.Y + c.Y) * p.Width + e.X + c.X);
            }
            if (solid.Count != 0) last = solid.Max();
            int holes = 0;
            for (int i = 0; i <= last; i++) if (p.Usable[i] && !solid.Contains(i)) holes++;
            return runs * 100000L + holes * 100L + Math.Max(last, 0) * 5L + rotation;
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
