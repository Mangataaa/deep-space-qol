using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DeepSpaceQoL.Core;

namespace DeepSpaceQoL
{
    internal static partial class ArrangeService
    {
        /// <summary>Snapshot, search and validate across frames; only the final complete
        /// pose commit is atomic. No item is removed or temporarily moved during search.</summary>
        internal static IEnumerable<object?> TryArrangeSteps(GameGridInventory inventory, Func<bool> keepRunning,
            Action<bool, string> completed)
        {
            if (!keepRunning()) yield break;
            if (!Eligible(inventory)) { completed(false, "此容器当前不能整理。"); yield break; }
            var slice = Stopwatch.StartNew();
            var originalItems = inventory.childItems.ToArray();
            var snapshots = new List<Snapshot>();
            foreach (var item in originalItems)
            {
                if (!keepRunning()) yield break;
                snapshots.Add(new Snapshot { Item = item, Shape = item.modifiedShape.Clone(), State = Fingerprint(item),
                    Movable = StorageRules.CanAutomate(item) && StowService.IsMovableStock(item) && !ItemPinHelper.IsPinned(item) && !DebrisHelper.IsDebris(item) &&
                        item.MayRemove() && item.MaxNumRemove() >= item.unitCount && (!(item is GameItemElement e) || !e.IsDestroyed()) });
                if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); }
            }
            var all = snapshots.ToArray(); var moving = all.Where(s => s.Movable).ToArray();
            if (moving.Length == 0) { completed(false, "没有可整理的物品；固定与不可移除物品保持原位。"); yield break; }
            var container = inventory.inventoryShape.Clone();
            int width = container.globalWidth, height = container.globalHeight, ox = container.minX, oy = container.minY;
            var problem = new PackingProblem { Width = width, Height = height, Usable = new bool[width * height] };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) problem.Usable[y * width + x] = container.Get(ox + x, oy + y) == 0;
                if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); if (!keepRunning()) yield break; }
            }
            problem.Fixed = all.Where(s => !s.Movable).SelectMany(s => ReadCells(s.Shape)
                .Select(c => new PackingCell(c.X + s.Shape.minX - ox, c.Y + s.Shape.minY - oy, c.Layer))).ToArray();
            problem.Items = new PackingItem[moving.Length];
            for (int index = 0; index < moving.Length; index++)
            {
                var s = moving[index];
                var orientations = new[] { s.Shape.orientation, (s.Shape.orientation + 1) % 4, (s.Shape.orientation + 2) % 4, (s.Shape.orientation + 3) % 4 };
                var shapes = orientations.Select(o => (GridShape)new GridShapeBuilder(s.Shape).SetOrientation(o).SetPosition(0, 0))
                    .Select(sh => new PackingShape { Orientation = sh.orientation, Width = sh.globalWidth, Height = sh.globalHeight, Cells = ReadCells(sh) }).ToArray();
                problem.Items[index] = new PackingItem { Group = s.Item.identifier ?? "", Shapes = shapes,
                    Original = new PackingPlacement(index, s.Shape.minX - ox, s.Shape.minY - oy, 0) };
                if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); if (!keepRunning()) yield break; }
            }
            if (!keepRunning()) yield break;
            var ignored = moving.Select(s => s.Item).ToList();
            GridShape Target(PackingPlacement p) => new GridShapeBuilder(moving[p.Item].Shape)
                .SetOrientation(problem.Items[p.Item].Shapes[p.Shape].Orientation).SetPosition(p.X + ox, p.Y + oy);
            PackingResult? solved = null;
            string? snapshotError = null;
            try { AssertUnchanged(inventory, all, false); }
            catch (Exception ex) { snapshotError = ex.Message; }
            if (snapshotError != null) { completed(false, "容器已变化，本页保留原摆放：" + snapshotError); yield break; }
            foreach (var step in GridPacking.SolveSteps(problem, r => solved = r,
                p => inventory.CheckShapePlacement(moving[p.Item].Item, Target(p), ignored), sliceMilliseconds: 3))
            {
                yield return step;
                if (!keepRunning()) yield break;
            }
            if (!keepRunning()) yield break;
            if (solved == null || !solved.Complete || !solved.Improved)
            { completed(false, "已保留原摆放；本轮未找到更紧凑且同物品更集中的完整方案。"); yield break; }
            // Recheck the full snapshot and policy at the last safe boundary. A drag,
            // lock, scene/save change or game mutation can invalidate a pending plan.
            bool staged = false; bool success = false; string status;
            try
            {
                if (!Eligible(inventory) || !keepRunning()) { completed(false, "容器状态已变化，本页未整理。"); yield break; }
                AssertUnchanged(inventory, all, false);
                foreach (var s in moving)
                    if (!StorageRules.CanAutomate(s.Item) || !StowService.IsMovableStock(s.Item) || ItemPinHelper.IsPinned(s.Item) ||
                        !s.Item.MayRemove() || s.Item.MaxNumRemove() < s.Item.unitCount)
                        throw new InvalidOperationException("物品的锁定、固定或原生移除权限已变化。");
                if (!SameShape(container, inventory.inventoryShape)) throw new InvalidOperationException("容器尺寸发生变化。");
                if (!GridPacking.Validate(problem, solved.Placements)) throw new InvalidOperationException("整理方案几何校验失败。");
                foreach (var p in solved.Placements)
                    if (!inventory.CheckShapePlacement(moving[p.Item].Item, Target(p), ignored)) throw new InvalidOperationException("游戏拒绝整理后的物品位置。");
                AssertUnchanged(inventory, all, false);
                staged = true;
                foreach (var p in solved.Placements) moving[p.Item].Item.modifiedShape = Target(p);
                foreach (var p in solved.Placements)
                {
                    var item = moving[p.Item].Item;
                    if (!inventory.CheckInventorySlotStillValid(item, item.modifiedShape, item.state)) throw new InvalidOperationException("游戏拒绝整理后的物品状态。");
                }
                inventory.Validate(); inventory.ForceRecomputeDynamicBackground();
                AssertUnchanged(inventory, all, true);
                foreach (var p in solved.Placements)
                    if (!SameShape(moving[p.Item].Item.modifiedShape, Target(p))) throw new InvalidOperationException("整理后的摆放与方案不一致。");
                success = true; status = $"已整理 {moving.Length} 件物品。";
            }
            catch (Exception ex)
            {
                if (staged)
                {
                    try
                    {
                        foreach (var s in all) s.Item.modifiedShape = s.Shape.Clone();
                        inventory.Validate(); inventory.ForceRecomputeDynamicBackground();
                        if (all.Any(s => !SameShape(s.Item.modifiedShape, s.Shape))) throw new InvalidOperationException("原位置校验失败。");
                    }
                    catch (Exception rollback) { throw new InvalidOperationException("整理恢复失败，请保留日志并重新读档：" + rollback.Message, ex); }
                }
                status = "本页整理已中止" + (staged ? "并恢复原位置" : "，未移动物品") + "：" + ex.Message;
            }
            completed(success, status);
        }

        private static bool Eligible(GameGridInventory inventory) => inventory != null && inventory.inventoryShape != null &&
            StorageRules.CanAutoAccess(inventory) && GeneralHelper.MayPlayerInsertInto(inventory);
    }
}
