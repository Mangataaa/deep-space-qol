using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DeepSpaceQoL.Core;

namespace DeepSpaceQoL
{
    internal static class ArrangeService
    {
        private sealed class Snapshot
        {
            public GameItem Item = null!;
            public GridShape Shape = null!;
            public string State = "";
            public bool Movable;
        }

        public static string Run(GameGridInventory inventory)
        { TryArrange(inventory, out string status); return status; }

        public static bool TryArrange(GameGridInventory inventory, out string status)
        {
            status = "此容器当前不能整理。";
            if (inventory == null || inventory.inventoryShape == null || StorageCatalog.IsNativeAccessLocked(inventory) || !GeneralHelper.MayPlayerInsertInto(inventory)) return false;
            Snapshot[]? snapshots = null;
            bool staged = false;
            try
            {
                snapshots = inventory.childItems.Select(i => new Snapshot {
                    Item = i, Shape = i.modifiedShape.Clone(), State = Fingerprint(i),
                    Movable = StowService.IsMovableStock(i) && !ItemPinHelper.IsPinned(i) && !DebrisHelper.IsDebris(i) && i.MayRemove() && i.MaxNumRemove() >= i.unitCount &&
                        (!(i is GameItemElement e) || !e.IsDestroyed())
                }).ToArray();
                var moving = snapshots.Where(s => s.Movable).ToArray();
                if (moving.Length == 0) { status = "没有可整理的物品；固定与不可移除物品保持原位。"; return false; }
                var container = inventory.inventoryShape;
                int width = container.globalWidth, height = container.globalHeight, ox = container.minX, oy = container.minY;
                var problem = new PackingProblem { Width = width, Height = height, Usable = new bool[width * height] };
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) problem.Usable[y * width + x] = container.Get(ox + x, oy + y) == 0;
                problem.Fixed = snapshots.Where(s => !s.Movable).SelectMany(s => ReadCells(s.Shape).Select(c => new PackingCell(c.X + s.Shape.minX - ox, c.Y + s.Shape.minY - oy, c.Layer))).ToArray();
                problem.Items = moving.Select((s, index) => {
                    // Original orientation is first, then legal native rotations.
                    // Flipping is preserved; no mirroring is invented by the mod.
                    var orientations = new[] { s.Shape.orientation, (s.Shape.orientation + 1) % 4, (s.Shape.orientation + 2) % 4, (s.Shape.orientation + 3) % 4 };
                    var shapes = orientations.Select(o => (GridShape)new GridShapeBuilder(s.Shape).SetOrientation(o).SetPosition(0, 0)).Select(sh => new PackingShape {
                        Orientation = sh.orientation, Width = sh.globalWidth, Height = sh.globalHeight, Cells = ReadCells(sh)
                    }).ToArray();
                    return new PackingItem { Group = s.Item.identifier ?? "", Shapes = shapes,
                        Original = new PackingPlacement(index, s.Shape.minX - ox, s.Shape.minY - oy, 0) };
                }).ToArray();
                var ignored = moving.Select(s => s.Item).ToList();
                GridShape Target(PackingPlacement p) => new GridShapeBuilder(moving[p.Item].Shape)
                    .SetOrientation(problem.Items[p.Item].Shapes[p.Shape].Orientation).SetPosition(p.X + ox, p.Y + oy);
                var solved = GridPacking.Solve(problem, p => inventory.CheckShapePlacement(moving[p.Item].Item, Target(p), ignored));
                AssertUnchanged(inventory, snapshots, false);
                if (!solved.Complete || !solved.Improved) { status = "已保留原摆放；本轮未找到更紧凑且同物品更集中的完整方案。"; return false; }
                if (!GridPacking.Validate(problem, solved.Placements)) throw new InvalidOperationException("整理方案几何校验失败。");
                foreach (var p in solved.Placements)
                    if (!inventory.CheckShapePlacement(moving[p.Item].Item, Target(p), ignored)) throw new InvalidOperationException("游戏拒绝整理后的物品位置。");
                AssertUnchanged(inventory, snapshots, false);
                staged = true;
                // Stage all poses together so native slot validation observes the
                // final occupancy, including pieces that exchanged positions.
                foreach (var p in solved.Placements) moving[p.Item].Item.modifiedShape = Target(p);
                foreach (var p in solved.Placements)
                {
                    var item = moving[p.Item].Item;
                    if (!inventory.CheckInventorySlotStillValid(item, item.modifiedShape, item.state)) throw new InvalidOperationException("游戏拒绝整理后的物品状态。");
                }
                inventory.Validate(); inventory.ForceRecomputeDynamicBackground();
                AssertUnchanged(inventory, snapshots, true);
                foreach (var p in solved.Placements)
                    if (!SameShape(moving[p.Item].Item.modifiedShape, Target(p))) throw new InvalidOperationException("整理后的摆放与方案不一致。");
                status = $"已整理 {moving.Length} 件物品；同物品集中摆放，固定物品保持原位。";
                return true;
            }
            catch (Exception ex)
            {
                if (staged && snapshots != null)
                {
                    try
                    {
                        foreach (var s in snapshots) s.Item.modifiedShape = s.Shape.Clone();
                        inventory.Validate(); inventory.ForceRecomputeDynamicBackground();
                        if (snapshots.Any(s => !SameShape(s.Item.modifiedShape, s.Shape)))
                        { status = "整理中止，恢复原位置校验未通过。请立即重新载入存档。"; return false; }
                    }
                    catch (Exception rollbackError)
                    { status = "整理中止，恢复原位置时发生异常，请重新载入存档：" + rollbackError.Message; return false; }
                }
                status = "整理已中止" + (staged ? "并恢复原位置" : "，未移动物品") + "：" + ex.Message;
                return false;
            }
        }

        private static PackingCell[] ReadCells(GridShape shape)
        {
            var cells = new List<PackingCell>();
            for (int y = 0; y < shape.globalHeight; y++) for (int x = 0; x < shape.globalWidth; x++)
            { byte b = shape.Get(shape.minX + x, shape.minY + y); if (b != 0) cells.Add(new PackingCell(x, y, b)); }
            return cells.ToArray();
        }
        private static bool SameShape(GridShape a, GridShape b)
        {
            if (a.minX != b.minX || a.minY != b.minY || a.orientation != b.orientation || a.flipped != b.flipped || a.globalWidth != b.globalWidth || a.globalHeight != b.globalHeight) return false;
            for (int y = 0; y < a.globalHeight; y++) for (int x = 0; x < a.globalWidth; x++)
                if (a.Get(a.minX + x, a.minY + y) != b.Get(b.minX + x, b.minY + y)) return false;
            return true;
        }
        private static void AssertUnchanged(GameGridInventory inventory, Snapshot[] snapshots, bool allowMoved)
        {
            var current = inventory.childItems;
            if (current.Count != snapshots.Length || current.Distinct().Count() != snapshots.Length) throw new InvalidOperationException("容器成员发生变化。");
            foreach (var s in snapshots)
                if (!current.Contains(s.Item) || s.Item.parentInventory != inventory || Fingerprint(s.Item) != s.State ||
                    ((!allowMoved || !s.Movable) && !SameShape(s.Item.modifiedShape, s.Shape)))
                    throw new InvalidOperationException("物品内容、锁定状态或位置发生变化。");
        }
        private static string Fingerprint(GameItem item)
        {
            var b = new StringBuilder();
            b.Append(item.identifier).Append('|').Append(item.uniqueId).Append('|').Append(item.unitCount).Append('|').Append(item.unitValue).Append('|').Append(item.name);
            if (item.state?.dict != null) foreach (var pair in item.state.dict.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                var t = pair.Value;
                b.Append('|').Append(pair.Key.Length).Append(':').Append(pair.Key).Append(':').Append(t.valueEnabled).Append(':').Append(t.valueInt).Append(':')
                    .Append(t.valueLong).Append(':').Append(t.valueFloat.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(t.valueDouble.ToString("R", CultureInfo.InvariantCulture))
                    .Append(':').Append(t.valueBool).Append(':').Append(t.valueString?.Length ?? 0).Append(':').Append(t.valueString);
            }
            return b.ToString();
        }
    }
}
