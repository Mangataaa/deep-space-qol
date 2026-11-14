using System.Diagnostics;
using DeepSpaceQoL.Core;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
PackingShape Rect(int w, int h, int orientation = 0) => new() { Width = w, Height = h, Orientation = orientation,
    Cells = Enumerable.Range(0, w * h).Select(i => new PackingCell(i % w, i / w)).ToArray() };
PackingProblem Problem(int width, int height) => new() { Width = width, Height = height, Usable = Enumerable.Repeat(true, width * height).ToArray() };
PackingItem Item(int i, int x, int y, string group, params PackingShape[] shapes) => new() {
    Group = group, Original = new PackingPlacement(i, x, y, 0), Shapes = shapes };

// At an equal deterministic node limit, splitting a search into rendered frames
// must preserve its algorithm, all identities, quality, and native vetoes.
var random = new Random(904783);
for (int run = 0; run < 60; run++)
{
    var p = Problem(9, 8);
    var locations = Enumerable.Range(0, 72).OrderBy(_ => random.Next()).ToArray();
    int count = random.Next(3, 45);
    p.Items = locations.Take(count).Select((pos, i) => Item(i, pos % 9, pos / 9, "g" + (i % 5), Rect(1, 1))).ToArray();
    p.Fixed = locations.Skip(count).Take(3).Select(pos => new PackingCell(pos % 9, pos / 9)).ToArray();
    var baseline = p.Items.Select(i => i.Original).ToArray();
    var synchronous = GridPacking.Solve(p, milliseconds: 10000, nodeLimit: 18000);
    PackingResult? incremental = null;
    int frames = 0;
    foreach (var _ in GridPacking.SolveSteps(p, r => incremental = r, milliseconds: 10000, nodeLimit: 18000, sliceMilliseconds: 0.05)) frames++;
    Check(incremental != null && incremental.Complete, "Incremental search must complete with a full layout.");
    Check(GridPacking.Validate(p, incremental!.Placements), "Incremental search lost an item or produced collisions.");
    Check(incremental.Score == synchronous.Score, "Yielding at the same node budget must not change the selected score.");
    Check(incremental.Nodes <= 18000, "Incremental search exceeded its node budget.");
    Check(p.Items.Select(i => i.Original).SequenceEqual(baseline), "Search mutated the live snapshot input.");
    Check(frames > 0, "Nontrivial search must yield for painting/UI input.");
}

var blocks = Problem(17, 10);
blocks.Items = Enumerable.Range(0, 24).Select(i => Item(i, i % 8 * 2, i / 8 * 3, i % 2 == 0 ? "food" : "water", Rect(2, 3), Rect(3, 2, 1))).ToArray();
PackingResult? result = null;
int blockFrames = 0;
foreach (var _ in GridPacking.SolveSteps(blocks, r => result = r, milliseconds: 10000, nodeLimit: 90000, sliceMilliseconds: 0.1)) blockFrames++;
Check(result != null && result.Improved && GridPacking.Validate(blocks, result.Placements), "Repeated block grouping regressed.");
Check(blockFrames > 1, "A larger search must continue over multiple frame slices.");
foreach (var group in result!.Placements.GroupBy(e => blocks.Items[e.Item].Group))
{
    var cells = group.SelectMany(e => blocks.Items[e.Item].Shapes[e.Shape].Cells.Select(c => (X: e.X + c.X, Y: e.Y + c.Y))).ToArray();
    Check((cells.Max(c => c.X) - cells.Min(c => c.X) + 1) * (cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1) == cells.Length,
        "Repeated rectangular goods no longer fill a compact group rectangle.");
}

var veto = Problem(6, 6);
veto.Items = Enumerable.Range(0, 8).Select(i => Item(i, i % 6, 4 + i / 6, "same", Rect(1, 1))).ToArray();
result = null;
foreach (var _ in GridPacking.SolveSteps(veto, r => result = r, p => p.Y >= 2, milliseconds: 10000, nodeLimit: 10000, sliceMilliseconds: 0.1)) { }
Check(result != null && result.Placements.All(p => p.Y >= 2), "A native placement veto must survive frame splits.");
Check(GridPacking.Validate(veto, result!.Placements), "Native-veto output is invalid.");

// Cancelling a pending plan never invokes its commit callback or changes input.
bool cancelled = false, callback = false;
var original = blocks.Items.Select(i => i.Original).ToArray();
using (var pending = GridPacking.SolveSteps(blocks, _ => callback = true, cancelled: () => cancelled,
    milliseconds: 10000, nodeLimit: 100000, sliceMilliseconds: 0.05).GetEnumerator())
{
    Check(pending.MoveNext(), "Cancellation fixture should still be searching.");
    cancelled = true;
    Check(!pending.MoveNext(), "A cancelled search must stop at its next frame boundary.");
}
Check(!callback && blocks.Items.Select(i => i.Original).SequenceEqual(original), "Cancellation committed or mutated a pending plan.");

// A caller can spend far longer between frames than the entire CPU budget.
// Waiting time is not charged to the solver, so a slow frame doesn't discard work.
result = null;
int waits = 0;
var wall = Stopwatch.StartNew();
foreach (var _ in GridPacking.SolveSteps(veto, r => result = r, milliseconds: 12, nodeLimit: 5000, sliceMilliseconds: 0.08))
{
    waits++;
    var wait = Stopwatch.StartNew();
    while (wait.Elapsed.TotalMilliseconds < 0.6) { }
}
Check(result != null && result.Complete && result.Nodes > 0 && GridPacking.Validate(veto, result.Placements), "Frame waits discarded the complete fallback.");
Check(waits > 0, "CPU-budget fixture must exercise frame yields.");

// Native calls execute only on the thread advancing the coroutine, never a worker.
int callerThread = Environment.CurrentManagedThreadId;
result = null;
foreach (var _ in GridPacking.SolveSteps(veto, r => result = r, p => {
    Check(Environment.CurrentManagedThreadId == callerThread, "Native predicate moved to a background thread."); return true;
}, milliseconds: 10000, nodeLimit: 1000, sliceMilliseconds: 0.1)) { }
Check(result != null && GridPacking.Validate(veto, result.Placements), "Single-thread native validation failed.");

// Warmed-up performance smoke check at the production 3 ms slice. Simulate a
// modest native validator cost; one MoveNext must not consume the whole 60 ms.
result = null; int perfFrames = 0; double maxSlice = 0;
using (var pending = GridPacking.SolveSteps(blocks, r => result = r, p => {
    var cost = Stopwatch.StartNew(); while (cost.Elapsed.TotalMilliseconds < 0.04) { } return true;
}, milliseconds: 60, nodeLimit: 90000, sliceMilliseconds: 3).GetEnumerator())
    while (true)
    {
        var frame = Stopwatch.StartNew(); bool more = pending.MoveNext();
        maxSlice = Math.Max(maxSlice, frame.Elapsed.TotalMilliseconds);
        if (!more) break; perfFrames++;
    }
Check(perfFrames > 4, "Production slice should distribute slow-native search across several frames.");
Check(maxSlice < 50, "A single warmed-up solver slice consumed nearly the whole search budget.");
Check(result != null && result.Complete && GridPacking.Validate(blocks, result.Placements), "Performance slicing returned an incomplete layout.");
Console.WriteLine($"GridPacking incremental: {checks} checks passed; repeated-block frames={blockFrames}, CPU-budget frames={waits}, production-slice frames={perfFrames}, max observed solver slice={maxSlice:F2} ms.");
