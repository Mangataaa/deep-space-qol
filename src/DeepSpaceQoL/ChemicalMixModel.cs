namespace DeepSpaceQoL;

/// <summary>Detached data-only prediction. Never registers items or calls rendering/feature callbacks.</summary>
internal static class ChemicalMixModel
{
    internal static int Index(string id) => Liquid.Liquids.FindIndex(l => l.identifier == id);
    internal static int Part(int[] parts, string id) { int i = Index(id); return i < 0 ? 0 : parts[i]; }
    internal static int Total(int[] parts) => checked((int)parts.Sum(p => (long)p));
    internal static int Volatile(int[] parts) => checked((int)Liquid.Liquids.Select((l, i) => l.isEvaporative ? (long)parts[i] : 0).Sum());
    internal static bool Valid(int[] parts) => parts.Length == Liquid.Liquids.Count && parts.All(p => p >= 0) && parts.Sum(p => (long)p) <= int.MaxValue;
    internal static double Purity(int[] parts, string active) => Total(parts) == 0 ? 0 : 100d * Part(parts, active) / Total(parts);
    internal static bool SafeInput(int[] parts, double temperature) => Valid(parts) && Total(parts) > 0 &&
        !double.IsNaN(temperature) && !double.IsInfinity(temperature) && temperature >= 10 && temperature < 40 &&
        new[] { "methylol", "reducing_agent" }.All(id => Part(parts, id) == 0);

    private static readonly Func<GameItem, int>[] Reactions =
    {
        ChemicalHelper.HandleMicrobeHeat, ChemicalHelper.HandleMicrobeAlcohol, ChemicalHelper.HandleOrganicWasteHeat,
        ChemicalHelper.HandleDreamCapMushReaction, ChemicalHelper.HandleNightmareCapMushReaction,
        ChemicalHelper.HandleCoffeeBeanExtractionSolvent, ChemicalHelper.HandleCoffeeBeanExtractionWater,
        ChemicalHelper.HandleNomoreReaction, ChemicalHelper.HandleDreamCapExtractDegradation,
        ChemicalHelper.HandleDreamCapMushDegradation, ChemicalHelper.HandleNightmareCapExtractDegradation,
        ChemicalHelper.HandleNightmareCapMushDegradation, ChemicalHelper.HandleOrganicWasteMineralization,
        ChemicalHelper.HandleAcidBaseMicrobeKill, ChemicalHelper.HandleStabilizingAgentSedimentation,
        ChemicalHelper.HandleProteinPrecipitation, ChemicalHelper.HandleLipobondAgentSedimentation,
        ChemicalHelper.HandleAcidBaseNeutralization, ChemicalHelper.HandleFantaHeat,
        ChemicalHelper.HandleMethylolHeatReaction, ChemicalHelper.HandleChlorine, ChemicalHelper.HandleBoilingReaction
    };

    // Retain the game's actual tag sharing: TagSystem.Clone is a shallow dictionary copy, so existing
    // TagState instances are shared. Do not insert independent deep copies or reorder the native handlers.
    internal static int[] Tick(int[] parts, double temperature)
    {
        var item = new Shadow(parts, temperature);
        foreach (var reaction in Reactions) reaction(item);
        item.SyncModifiedState();
        return WaterHelper.GetLiquidParts(item);
    }

    internal static int[]? Settle(int[] parts, double temperature)
    {
        var current = (int[])parts.Clone();
        for (int tick = 0; tick < 180; tick++)
        {
            var next = Tick(current, temperature);
            if (!Valid(next)) return null;
            if (next.SequenceEqual(current)) return next;
            current = next;
        }
        return null; // Never execute a nonconverging prediction.
    }

    internal static int[] Add(int[] target, int[] source, int volume)
    {
        if (!Valid(target) || !Valid(source) || volume <= 0 || volume > Total(source)) throw new ArgumentException("无效加药量");
        var portion = WaterHelper.SplitProportionally(source, volume);
        return target.Select((p, i) => checked(p + portion[i])).ToArray();
    }

    internal static double Burden(int[] parts, string active, bool finishing)
    {
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var liquid = Liquid.Liquids[i];
            if (liquid.identifier == active || liquid.isEvaporative) continue;
            bool filterable = liquid.identifier != "microbe" && liquid.particleSize >= 22;
            total += parts[i] * (!finishing && filterable ? 0.025 : 1);
        }
        return total;
    }

    private sealed class Shadow : GameItem
    {
        internal Shadow(int[] parts, double temperature) : base("qol_chemistry_prediction")
        {
            for (int i = 0; i < parts.Length; i++) state.GetTag(Liquid.Liquids[i].currentPart).Enable().SetInt(parts[i]);
            state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).Enable().SetDouble(temperature);
            state.GetTag(WaterHelper.LIQUID_CONTAINER_TAG).Enable();
            modifiedState = state.Clone();
        }
        public override void Validate() { }
        public override void Destroy() => throw new InvalidOperationException("预测数据不能作为真实物品销毁");
    }
}
