using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceQoL.Core;
using UnityEngine;

namespace DeepSpaceQoL
{
    public static class BottlingService
    {
        private static readonly HashSet<string> WaterParts = new HashSet<string>(StringComparer.Ordinal)
        {
            "water", "heavy_metal", "microbe", "organic_waste", "mineral",
            "chemical_contaminant", "physical_contaminant", "microplastic"
        };

        public static bool IsSource(GameItem item)
        {
            if (item == null || item.unitCount != 1 || !item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) ||
                !item.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY) || !item.IsTag("CONTAINER_TEMP_TAG") || item.IsTag("WINE_CONTAINER_TAG") ||
                item.IsTag(WaterHelper.LIQUID_CONTAINER_DROPPER_TAG)) return false;
            // Explicit native containers avoid accidentally draining a machine's process vessel.
            return WaterTankHelper.IsTank(item) || item.identifier == "water_jug" || item.identifier == "water_pitcher";
        }

        /// <summary>Current in-place source permission and liquid state, shared by the menu and action.</summary>
        public static bool CanUseSource(GameItem? source, out string reason)
        {
            if (!BottlingAccess.CanUseSource(source, out reason)) return false;
            int waterIndex = Liquid.Liquids.FindIndex(liquid => liquid.identifier == Liquid.LIQUID_WATER);
            int[] initial = WaterHelper.GetLiquidParts(source!);
            if (waterIndex < 0 || !IsWater(initial, waterIndex) ||
                LiquidQualityPolicy.Total(initial) > WaterHelper.GetTotalCapacity(source!))
            { reason = "水源为空、状态异常或含有药剂/其他液体，未进行自动装瓶。"; return false; }
            return true;
        }

        public static string Run(GameItem source)
        {
            if (PlayerStore.Instance == null || EmporiumEntry.Instance == null) return "请先进入店铺存档。";
            if (MapUIManager.Instance != null && MapUIManager.Instance.IsOutside) return "请回到店内再一键装瓶。";
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem ||
                ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.IsGroupDragging)
                return "请先放下正在拖动的物品，再一键装瓶。";
            if (!CanUseSource(source, out string sourceReason)) return sourceReason;
            var accessible = BottlingCatalog.GetItems();
            int waterIndex = Liquid.Liquids.FindIndex(liquid => liquid.identifier == Liquid.LIQUID_WATER);
            var bottles = accessible.Where(item => item != source && BottlingAccess.IsBottle(item) &&
                    WaterHelper.GetTotalCapacity(item) < WaterHelper.GetTotalCapacity(source))
                // Top up compatible existing stock before opening more empty bottles.
                .OrderBy(item => WaterHelper.IsEmpty(item) ? 1 : 0)
                .ThenBy(item => WaterHelper.GetFreeCapacity(item))
                .ThenBy(item => item.uniqueId).ToArray();

            int filled = 0, skippedQuality = 0, skippedAccess = 0;
            string lastAccessReason = "";
            long transferred = 0;
            foreach (var target in bottles)
            {
                if (WaterHelper.IsEmpty(source)) break;
                if (!BottlingAccess.CanUseSource(source, out sourceReason))
                    return $"装瓶停止：{sourceReason} 已装 {filled} 瓶。";
                if (!BottlingAccess.CanTransfer(source, target, out string targetReason))
                {
                    skippedAccess++;
                    lastAccessReason = targetReason;
                    continue;
                }
                if (WaterHelper.GetFilterItem(target) != null) continue;
                int free = WaterHelper.GetFreeCapacity(target);
                if (free <= 0) continue;

                int[] sourceBefore = WaterHelper.GetLiquidParts(source), targetBefore = WaterHelper.GetLiquidParts(target);
                if (!IsWater(sourceBefore, waterIndex) ||
                    !LiquidQualityPolicy.MayTopUp(sourceBefore, targetBefore, waterIndex,
                        Temperature(source), Temperature(target)))
                {
                    skippedQuality++;
                    continue;
                }
                int amount = (int)Math.Min(free, LiquidQualityPolicy.Total(sourceBefore));
                int[] portion = WaterHelper.SplitProportionally(sourceBefore, amount);
                int[] predicted = targetBefore.Select((part, index) => checked(part + portion[index])).ToArray();
                if (LiquidQualityPolicy.Grade(predicted, waterIndex) != LiquidQualityPolicy.Grade(sourceBefore, waterIndex))
                {
                    skippedQuality++;
                    continue;
                }

                var sourceSnapshot = new Snapshot(source);
                var targetSnapshot = new Snapshot(target);
                try
                {
                    // This is the same transfer used by normal water-container interactions.
                    WaterHelper.TransferLiquid(source, target);
                    source.SyncModifiedState();
                    target.SyncModifiedState();
                    int[] sourceAfter = WaterHelper.GetLiquidParts(source), targetAfter = WaterHelper.GetLiquidParts(target);
                    if (!LiquidQualityPolicy.Conserved(sourceBefore, targetBefore, sourceAfter, targetAfter) ||
                        LiquidQualityPolicy.Total(targetAfter) > WaterHelper.GetTotalCapacity(target) ||
                        LiquidQualityPolicy.Total(sourceAfter) != LiquidQualityPolicy.Total(sourceBefore) - amount ||
                        LiquidQualityPolicy.Total(targetAfter) != LiquidQualityPolicy.Total(targetBefore) + amount)
                        throw new InvalidOperationException("Native liquid transfer failed conservation/capacity verification.");
                    // Native code reads the source purity after subtracting the pour. If that
                    // empties an identified source, it can mark an empty receiver as 0%-pure.
                    // Preserve only knowledge already present on that source; reveal nothing new.
                    if (LiquidQualityPolicy.Total(targetBefore) == 0 && sourceSnapshot.WasExposed &&
                        LiquidQualityPolicy.Total(sourceAfter) == 0)
                        WaterFeatureHelper.InitWaterFeature(target, isExpose: true);
                    WaterHelper.UpdateWaterBottleSound(source);
                    WaterHelper.UpdateWaterBottleSound(target);
                    source.Validate();
                    target.Validate();
                    BottlingAccess.RefreshTradeQuote(source, target);
                    filled++;
                    transferred += amount;
                }
                catch (Exception error)
                {
                    // Previous completed bottles remain valid. Restore only this failed pair.
                    try
                    {
                        sourceSnapshot.Restore();
                        targetSnapshot.Restore();
                        sourceSnapshot.Refresh();
                        targetSnapshot.Refresh();
                    }
                    catch (Exception restoreError)
                    {
                        Debug.LogError("[DeepSpaceQoL] Bottling pair restore failed: " + restoreError);
                        return "装瓶异常，恢复未完成；请不要保存，重新载入存档。";
                    }
                    try { BottlingAccess.RefreshTradeQuote(source, target); }
                    catch (Exception refreshError)
                    {
                        Debug.LogError("[DeepSpaceQoL] Liquids restored but trade UI refresh failed: " + refreshError);
                        return $"装瓶中止，当前瓶已恢复；此前已装 {filled} 瓶。交易界面刷新失败，请关闭并重新打开交易面板。";
                    }
                    Debug.LogError("[DeepSpaceQoL] Bottling stopped; current pair restored: " + error);
                    return $"装瓶中止，当前瓶已恢复。此前已装 {filled} 瓶，共 {transferred / 1000.0:0.###} 毫升。";
                }
            }
            if (filled == 0)
                return skippedQuality > 0 ? $"没有可装瓶：已跳过 {skippedQuality} 个水质/温度不兼容的瓶子。" :
                    skippedAccess > 0 ? $"没有可装瓶：{lastAccessReason}" : "没有可用的空瓶或同水质未满瓶（锁定容器内的物品会跳过）。";
            return $"已装 {filled} 瓶，共 {transferred / 1000.0:0.###} 毫升；水源剩余 {WaterHelper.GetTotalVolume(source) / 1000.0:0.###} 毫升。" +
                (skippedQuality > 0 ? $"已跳过 {skippedQuality} 个水质/温度不兼容的瓶子。" : "") +
                (skippedAccess > 0 ? $"已跳过 {skippedAccess} 个不可操作瓶子；{lastAccessReason}" : "");
        }

        private static double Temperature(GameItem item) => item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();

        private static bool IsWater(int[] parts, int waterIndex)
        {
            if (parts.Length != Liquid.Liquids.Count || LiquidQualityPolicy.Total(parts) <= 0 || parts[waterIndex] <= 0) return false;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] > 0 && !WaterParts.Contains(Liquid.Liquids[i].identifier)) return false;
            return true;
        }

        // No item is cloned: capture tags and the mutable water feature for failure recovery.
        // TagSystem.Clone() is deliberately not used; it shallow-copies its TagState objects.
        private sealed class Snapshot
        {
            private readonly GameItem item;
            private readonly Dictionary<string, TagState> tags;
            private readonly List<string> types;
            private readonly List<ItemFeature> features;
            private readonly long unitValue, lateUnitValue, baseValue, backupValue;
            private readonly ItemFeature water;
            private readonly ItemCondition fakeCondition, realCondition;
            private readonly int presumedPurity;
            private readonly bool disabled, match, exposed;
            public bool WasExposed => exposed;

            public Snapshot(GameItem item)
            {
                this.item = item;
                tags = item.state.dict.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
                types = item.itemTypes.ToList();
                features = item.itemFeatures.ToList();
                unitValue = item.unitValue;
                lateUnitValue = item.lateUnitValue;
                baseValue = item.unitBaseValue;
                backupValue = item.backupUnitValue;
                water = WaterFeatureHelper.GetWaterFeature(item);
                fakeCondition = water.fakeCondition;
                realCondition = water.realCondition;
                presumedPurity = water.customIntValue1;
                disabled = water.isDisabled;
                match = water.isFeatureMatch;
                exposed = water.IsExposed();
            }

            public void Restore()
            {
                item.state.dict.Clear();
                foreach (var pair in tags) item.state.dict.Add(pair.Key, pair.Value.Clone());
                item.itemTypes = types.ToList();
                item.itemFeatures = features.ToList();
                water.fakeCondition = fakeCondition;
                water.realCondition = realCondition;
                water.customIntValue1 = presumedPurity;
                water.isDisabled = disabled;
                water.isFeatureMatch = match;
                water.SetExposed(exposed);
                item.unitValue = unitValue;
                item.lateUnitValue = lateUnitValue;
                item.unitBaseValue = baseValue;
                item.backupUnitValue = backupValue;
                item.SyncModifiedState();
            }

            public void Refresh()
            {
                SpriteHelper.UpdateWaterContainerSprite(item);
                WaterHelper.UpdateWaterBottleSound(item);
                item.ValidateShapeState();
            }
        }
    }
}
