using System.Reflection;

namespace DeepSpaceQoL;

/// <summary>Deep state rollback for bounded native inventory/liquid transactions.</summary>
internal sealed class ItemStateSnapshot
{
    private static readonly FieldInfo[] FeatureFields = typeof(ItemFeature).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private readonly TagSystem state = new();
    private readonly List<string> types;
    private readonly List<ItemFeature> features;
    private readonly string name, sprite, atlas;
    private readonly long baseValue, value, lateValue, backupValue;
    private readonly GridShape shape, modifiedShape;

    internal ItemStateSnapshot(GameItem item)
    {
        foreach (var p in item.state.dict) state.dict.Add(p.Key, p.Value.Clone());
        types = item.itemTypes.ToList();
        features = item.itemFeatures.Select(CloneFeature).ToList();
        name = item.name; sprite = item.spritePath; atlas = item.spriteAtlasPath;
        baseValue = item.unitBaseValue; value = item.unitValue; lateValue = item.lateUnitValue; backupValue = item.backupUnitValue;
        shape = item.shape; modifiedShape = item.modifiedShape;
    }

    internal void Restore(GameItem item)
    {
        item.state = state; item.itemTypes = types; item.itemFeatures = features;
        item.name = name; item.spritePath = sprite; item.spriteAtlasPath = atlas;
        item.unitBaseValue = baseValue; item.unitValue = value; item.lateUnitValue = lateValue; item.backupUnitValue = backupValue;
        item.shape = shape; item.modifiedShape = modifiedShape;
        item.SyncModifiedState();
    }

    private static ItemFeature CloneFeature(ItemFeature feature)
    {
        var copy = new ItemFeature();
        foreach (var f in FeatureFields) f.SetValue(copy, f.GetValue(feature));
        copy.fakeCondition = CloneCondition(feature.fakeCondition); copy.realCondition = CloneCondition(feature.realCondition);
        return copy;
    }

    private static ItemCondition CloneCondition(ItemCondition condition)
    {
        if (condition == null) return null!;
        var copy = new ItemCondition();
        foreach (var field in typeof(ItemCondition).GetFields(BindingFlags.Instance | BindingFlags.Public)) field.SetValue(copy, field.GetValue(condition));
        return copy;
    }
}
