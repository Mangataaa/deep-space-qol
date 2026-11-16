namespace DeepSpaceQoL;

internal sealed class ChemicalReagent
{
    internal GameItem? Item = null;
    internal string Name = "", Kind = "";
    internal int[] Parts = Array.Empty<int>();
    internal double Temperature;
}

internal sealed class ChemicalDose
{
    internal ChemicalReagent Reagent = null!;
    internal int Volume;
    internal int[] SourceBefore = Array.Empty<int>(), Before = Array.Empty<int>(), AfterPour = Array.Empty<int>(), AfterReaction = Array.Empty<int>();
}

internal sealed class ChemicalMixPlan
{
    internal bool Finishing;
    internal string Active = "", Note = "";
    internal int[] Initial = Array.Empty<int>(), Baseline = Array.Empty<int>(), Final = Array.Empty<int>();
    internal readonly List<ChemicalDose> Doses = new();
    internal readonly List<string> Warnings = new();
    internal bool Limited;
    internal string Raw = "";
}

/// <summary>Bounded greedy search over real bottle compositions; both stages keep every active ingredient.</summary>
internal static class ChemicalMixPlanner
{
    internal static readonly string[] ActiveIds = { "dream_cap_extract", "nightmare_extract", "caffeine", "zyanide", "contraceptive", "fanta", "nomore" };
    private static readonly HashSet<string> Impurities = new(StringComparer.Ordinal)
    { "water", "alcohol", "heavy_metal", "chemical_contaminant", "physical_contaminant", "microbe", "mineral" };
    private static readonly string[] Kinds = { "acid", "base", "lipobond_agent", "stabilizing_agent", "chlorine", "alcohol" };

    internal static string RawFor(string active) => active switch
    {
        "dream_cap_extract" => "dream_cap_mush", "nightmare_extract" => "nightmare_cap_mush", "caffeine" => "coffee_mush", _ => ""
    };
    internal static bool HasTarget(int[] parts, string active, bool finishing) => ActiveIds.Contains(active) &&
        (ChemicalMixModel.Part(parts, active) > 0 || !finishing && ChemicalMixModel.Part(parts, RawFor(active)) > 0);
    internal static string DefaultActive(int[] parts) => ActiveIds.OrderByDescending(id =>
        (long)ChemicalMixModel.Part(parts, id) + ChemicalMixModel.Part(parts, RawFor(id))).First();

    internal static long SolventTarget(int[] parts)
    {
        // Mushroom formulas divide by all remaining mush; coffee divides by this tick's
        // 15% processing portion. Solvent is shared and is not consumed by extraction.
        long mushrooms = Math.Max((long)ChemicalMixModel.Part(parts, "dream_cap_mush"), ChemicalMixModel.Part(parts, "nightmare_cap_mush")) * 5;
        int coffee = ChemicalMixModel.Part(parts, "coffee_mush");
        long coffeePortion = coffee <= 0 ? 0 : Math.Max(1, (int)Math.Round(coffee * .15));
        return Math.Max(mushrooms, coffeePortion * 5);
    }

    internal static string ReagentKind(int[] parts)
    {
        if (!ChemicalMixModel.Valid(parts) || ChemicalMixModel.Total(parts) <= 0) return "";
        foreach (string kind in Kinds)
        {
            if ((long)ChemicalMixModel.Part(parts, kind) * 2 < ChemicalMixModel.Total(parts)) continue;
            if (Liquid.Liquids.Select((l, i) => parts[i] == 0 || l.identifier == kind || Impurities.Contains(l.identifier)).All(b => b)) return kind;
        }
        return "";
    }

    private static double Need(int[] parts, string kind, bool finishing)
    {
        double acid = ChemicalMixModel.Part(parts, "acid"), alkali = ChemicalMixModel.Part(parts, "base");
        double preparation = finishing ? 0 : ChemicalMixModel.Part(parts, "protein") +
            Math.Ceiling(ChemicalMixModel.Part(parts, "organic_waste") / 40d) + Math.Ceiling(ChemicalMixModel.Part(parts, "microbe") / 20d);
        return kind switch
        {
            "acid" => Math.Max(0, alkali - acid) + preparation,
            "base" => Math.Max(0, acid - alkali) + preparation,
            "lipobond_agent" => finishing ? 0 : ChemicalMixModel.Part(parts, "fat") / 8d,
            "stabilizing_agent" => finishing ? 0 : acid + alkali,
            "chlorine" => ChemicalMixModel.Part(parts, "water") <= 0 ? 0 :
                Math.Max(0, ChemicalMixModel.Part(parts, "chemical_contaminant") - ChemicalMixModel.Total(parts) * .007) / 5.1 + ChemicalMixModel.Part(parts, "microbe") / 5.1,
            "alcohol" => finishing ? 0 : Math.Max(0, SolventTarget(parts) - (double)ChemicalMixModel.Part(parts, "alcohol")),
            _ => 0
        };
    }

    internal static IEnumerable<int> Search(ChemicalMixPlan plan, int[] initial, double temperature, int capacity,
        string active, bool finishing, IReadOnlyList<ChemicalReagent> all)
    {
        plan.Initial = (int[])initial.Clone(); plan.Active = active; plan.Finishing = finishing;
        if (!ChemicalMixModel.SafeInput(initial, temperature) || !HasTarget(initial, active, finishing) ||
            capacity < ChemicalMixModel.Total(initial))
        { plan.Note = "请在 10°C ≤ 温度 < 40°C 配比；第一阶段支持对应菇浆／咖啡浆，第二阶段需已有目标有效成分。专用热合成前体暂不处理。"; yield break; }
        var current = ChemicalMixModel.Settle(initial, temperature);
        if (current == null) { plan.Note = "原生反应未能收敛或出现异常数值，未生成可执行方案。"; yield break; }
        plan.Baseline = (int[])current.Clone(); plan.Final = (int[])current.Clone();
        var chosen = all.Where(r => r.Kind == ReagentKind(r.Parts) && r.Kind.Length > 0 &&
                ChemicalMixModel.SafeInput(r.Parts, r.Temperature))
            .GroupBy(r => r.Kind).SelectMany(g => g.OrderByDescending(r =>
                (double)ChemicalMixModel.Part(r.Parts, r.Kind) / ChemicalMixModel.Total(r.Parts)).ThenBy(r => r.Name, StringComparer.Ordinal).Take(4)).ToArray();
        plan.Limited = all.Count > chosen.Length;
        var remaining = chosen.ToDictionary(r => r, r => (int[])r.Parts.Clone());
        int evaluations = 0;
        plan.Raw = !finishing && ChemicalMixModel.Part(initial, RawFor(active)) > 0 ? RawFor(active) : "";
        // Cold mash is stable below 40°C: first dose real solvent to its native 5:1
        // extraction threshold, then run the ordinary precipitation/neutralization planner.
        // Heating/extraction is NEVER simulated as an already completed live reaction.
        if (plan.Raw.Length > 0)
        {
            foreach (var reagent in chosen.Where(r => r.Kind == "alcohol"))
            {
                double need = Need(current, "alcohol", false);
                if (need <= 0) break;
                var source = remaining[reagent];
                int total = ChemicalMixModel.Total(source), pure = ChemicalMixModel.Part(source, "alcohol");
                int max = Math.Min(total, capacity - ChemicalMixModel.Total(current));
                if (pure <= 0 || max <= 0) continue;
                // Native SplitProportionally rounds; find the smallest whole dose that
                // actually delivers the missing solvent, including bottle impurities.
                int low = 1, high = max;
                while (low < high)
                {
                    int mid = low + (high - low) / 2;
                    if (ChemicalMixModel.Part(WaterHelper.SplitProportionally(source, mid), "alcohol") >= need) high = mid;
                    else low = mid + 1;
                }
                int volume = low;
                var poured = ChemicalMixModel.Add(current, source, volume);
                var settled = ChemicalMixModel.Settle(poured, temperature);
                if (settled == null) { plan.Note = "加酒精后的原生反应未收敛，未生成可执行方案。"; yield break; }
                plan.Doses.Add(new ChemicalDose { Reagent = reagent, Volume = volume, SourceBefore = (int[])source.Clone(),
                    Before = (int[])current.Clone(), AfterPour = poured, AfterReaction = settled });
                var portion = WaterHelper.SplitProportionally(source, volume);
                remaining[reagent] = source.Select((p, i) => p - portion[i]).ToArray();
                current = settled; plan.Final = (int[])current.Clone();
                yield return ++evaluations;
            }
            double solventMissing = Need(current, "alcohol", false);
            if (solventMissing > 0)
                plan.Warnings.Add($"酒精尚缺 {WaterScannerUIManager.FormatAmount((int)Math.Min(int.MaxValue, Math.Ceiling(solventMissing)))} 有效组分；瓶量或容器容量不足。不要提前加热消耗剩余原料浆，补料／分装后重新计算。");
            plan.Warnings.Add("冷浆加药不会自动完成萃取。酒精补足、蛋白沉淀后，还需用原生加热板保持萃取温度：梦帽菇 40～低于60°C、噩梦菇 40～低于50°C、咖啡至少50°C。浆量归零后再过滤／蒸发，不要直接高温破坏菇类有效成分。");
        }
        for (int round = 0; round < 6; round++)
        {
            ChemicalDose? best = null;
            double baselineScore = ChemicalMixModel.Burden(current, active, finishing), bestGain = .5;
            foreach (var reagent in chosen)
            {
                if (reagent.Kind == "alcohol") continue; // solvent was explicitly prepared first
                var source = remaining[reagent];
                int total = ChemicalMixModel.Total(source), pure = ChemicalMixModel.Part(source, reagent.Kind);
                if (pure <= 0 || total <= 0) continue;
                double need = Need(current, reagent.Kind, finishing);
                if (need <= 0) continue;
                int max = Math.Min(total, capacity - ChemicalMixModel.Total(current));
                if (max <= 0) continue;
                double desired = need * total / pure;
                var volumes = new HashSet<int> { Math.Min(max, Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(desired)))) };
                foreach (double fraction in new[] { .125, .25, .5, .75, .9, 1d, 1.1, 1.25, 1.5, 2d, 4d })
                    volumes.Add(Math.Min(max, Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(desired * fraction)))));
                foreach (int amount in volumes.OrderBy(v => v))
                {
                    var poured = ChemicalMixModel.Add(current, source, amount);
                    var settled = ChemicalMixModel.Settle(poured, temperature);
                    if (settled != null && ChemicalMixModel.Total(settled) <= capacity && ActiveIds.All(id =>
                        ChemicalMixModel.Part(settled, id) >= ChemicalMixModel.Part(current, id)))
                    {
                        double gain = baselineScore - ChemicalMixModel.Burden(settled, active, finishing) - amount * .0005;
                        if (gain > bestGain)
                        {
                            bestGain = gain;
                            best = new ChemicalDose { Reagent = reagent, Volume = amount, SourceBefore = (int[])source.Clone(),
                                Before = (int[])current.Clone(), AfterPour = poured, AfterReaction = settled };
                        }
                    }
                    yield return ++evaluations;
                }
            }
            if (best == null) break;
            plan.Doses.Add(best);
            var portion = WaterHelper.SplitProportionally(remaining[best.Reagent], best.Volume);
            remaining[best.Reagent] = remaining[best.Reagent].Select((p, i) => p - portion[i]).ToArray();
            current = best.AfterReaction; plan.Final = (int[])current.Clone();
        }
        plan.Note = plan.Doses.Count == 0 ? "当前来源页没有更有利的安全加药方案，未消耗试剂。" :
            (plan.Raw.Length > 0 ? "先补酒精，再按现有蛋白／酸碱进行沉淀和中和。原料浆不会在冷配比时凭空变成提取物。" :
            "有限搜索的建议方案；按当前组分和原生反应顺序预计。实际反应不同会停止后续加药，不会直接改纯度。");
        if (ChemicalMixModel.Total(current) >= capacity) plan.Warnings.Add("当前容器没有加药余量，请先换更大容器或分装后再计算，不会通过溢出来腾位置。");
        foreach (string kind in Kinds)
        {
            double need = Need(current, kind, finishing);
            long available = remaining.Where(p => p.Key.Kind == kind).Sum(p => (long)ChemicalMixModel.Part(p.Value, kind));
            if (need > available + 1)
                plan.Warnings.Add($"{Liquid.GetLiquidFromId(kind)?.displayName ?? kind}不足：按残留估算还需有效组分 {WaterScannerUIManager.FormatAmount((int)Math.Min(int.MaxValue, Math.Ceiling(need)))}；参与搜索的药剂瓶剩余有效组分 {WaterScannerUIManager.FormatAmount((int)Math.Min(int.MaxValue, available))}。这是估算缺口，补料后需重新计算。");
        }
    }
}
