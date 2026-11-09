using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DeepSpaceQoL.Core;

/// <summary>Pure budget and occupied-cell search. Does not reference Unity or mutate items.</summary>
public static class StockPlanner
{
    public sealed class Option
    {
        public int Token { get; set; }
        public int[] Cells { get; set; } = Array.Empty<int>();
    }

    public sealed class Candidate
    {
        public int Token { get; set; }
        public string VarietyKey { get; set; } = "";
        public long Value { get; set; }
        public IReadOnlyList<Option> Options { get; set; } = Array.Empty<Option>();
    }

    public sealed class Choice
    {
        public int CandidateToken { get; set; }
        public int OptionToken { get; set; }
    }

    public sealed class Result
    {
        public long Value { get; set; }
        public IReadOnlyList<Choice> Choices { get; set; } = Array.Empty<Choice>();
        public bool SearchLimited { get; set; }
    }

    private sealed class State
    {
        public ulong[] Mask = Array.Empty<ulong>();
        public long Value;
        public List<Choice> Choices = new();
        public HashSet<string> Varieties = new(StringComparer.Ordinal);
        public int Area;
    }

    /// <summary>Bounded beam search; an optimum is not guaranteed for arbitrary shaped inventories.</summary>
    public static Result Solve(int cellCount, IEnumerable<int> occupied, IReadOnlyList<Candidate> candidates,
        long budget, bool requireVariety, IEnumerable<string>? stagedVarieties = null,
        int beamWidth = 96, int milliseconds = 700)
    {
        if (cellCount < 0 || cellCount > 65536) throw new ArgumentOutOfRangeException(nameof(cellCount));
        budget = Math.Max(0, budget);
        var first = new State { Mask = new ulong[(cellCount + 63) / 64] };
        foreach (int cell in occupied)
        {
            if (cell < 0 || cell >= cellCount) throw new ArgumentException("Invalid occupied cell.");
            first.Mask[cell >> 6] |= 1UL << (cell & 63);
        }
        if (stagedVarieties != null) first.Varieties.UnionWith(stagedVarieties);
        var beam = new List<State> { first };
        var watch = Stopwatch.StartNew();
        bool limited = false;
        foreach (var candidate in candidates.Where(c => c.Value > 0 && c.Value <= budget)
                     .OrderByDescending(c => c.Value).ThenBy(c => c.Token))
        {
            if (watch.ElapsedMilliseconds >= milliseconds) { limited = true; break; }
            var next = new List<State>(beam);
            foreach (var state in beam)
            {
                if (candidate.Value > budget - state.Value ||
                    (requireVariety && state.Varieties.Contains(candidate.VarietyKey))) continue;
                // Keep several different legal positions: a single first-fit choice misses combinations.
                int alternatives = 0;
                foreach (var option in candidate.Options)
                {
                    bool fits = option.Cells.Length > 0;
                    foreach (int cell in option.Cells)
                    {
                        if (cell < 0 || cell >= cellCount) throw new ArgumentException("Invalid option cell.");
                        if ((state.Mask[cell >> 6] & (1UL << (cell & 63))) != 0) { fits = false; break; }
                    }
                    if (!fits) continue;
                    var s = new State
                    {
                        Mask = (ulong[])state.Mask.Clone(), Value = state.Value + candidate.Value,
                        Area = state.Area + option.Cells.Length,
                        Choices = new List<Choice>(state.Choices),
                        Varieties = new HashSet<string>(state.Varieties, StringComparer.Ordinal)
                    };
                    foreach (int cell in option.Cells) s.Mask[cell >> 6] |= 1UL << (cell & 63);
                    s.Choices.Add(new Choice { CandidateToken = candidate.Token, OptionToken = option.Token });
                    if (requireVariety) s.Varieties.Add(candidate.VarietyKey);
                    next.Add(s);
                    if (++alternatives >= 8) break;
                }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            beam = next.OrderByDescending(s => s.Value).ThenBy(s => s.Area).ThenBy(s => s.Choices.Count)
                .Where(s => seen.Add(s.Value + ":" + string.Join(",", s.Mask) + ":" +
                    (requireVariety ? string.Join(",", s.Varieties.OrderBy(v => v, StringComparer.Ordinal)) : "")))
                .Take(Math.Max(8, beamWidth)).ToList();
            if (beam[0].Value == budget) break;
        }
        var best = beam.OrderByDescending(s => s.Value).ThenBy(s => s.Area).First();
        return new Result { Value = best.Value, Choices = best.Choices, SearchLimited = limited };
    }
}
