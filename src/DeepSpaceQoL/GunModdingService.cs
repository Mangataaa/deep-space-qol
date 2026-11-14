using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using DeepSpaceQoL.Core;
using UnityEngine;
using Option = DeepSpaceQoL.Core.GunModificationPlanner.Option;
using Problem = DeepSpaceQoL.Core.GunModificationPlanner.Problem;
using Requirement = DeepSpaceQoL.Core.GunModificationPlanner.Requirement;

namespace DeepSpaceQoL;

/// <summary>Plans without touching live items, then uses fresh native slots for whole-item moves.</summary>
internal static class GunModdingService
{
    private static readonly string[] StatTags = { "GUN_FIREPOWER_TAG", "GUN_STYLE_TAG", "GUN_RELIABILITY_TAG", "GUN_RECOIL_TAG", "GUN_ERGONOMICS_TAG", "GUN_ACCURACY_TAG" };
    private static readonly string[] CurrentTags = { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_STYLE_TAG", "GUN_CURRENT_RELIABILITY_TAG", "GUN_CURRENT_RECOIL_TAG", "GUN_CURRENT_ERGONOMICS_TAG", "GUN_CURRENT_ACCURACY_TAG" };
    private static readonly string[] FlatTags = { "MOD_BASE_FIREPOWER_TAG", "MOD_BASE_STYLE_TAG", "MOD_BASE_RELIABILITY_TAG", "MOD_BASE_RECOIL_TAG", "MOD_BASE_ERGONOMICS_TAG", "MOD_BASE_ACCURACY_TAG" };
    private static readonly string[] SlotRoles = { "mech_gun_tag_stock", "mech_gun_tag_rail", "mech_gun_tag_magazine", "mech_gun_tag_underbarrel", "mech_gun_tag_barrel", "mech_gun_tag_special1", "mech_gun_tag_special2", "mech_gun_tag_special3" };
    private static readonly string[] SlotTags = { "MOD_STOCK_TAG", "MOD_RAIL_TAG", "MOD_MAGAZINE_TAG", "MOD_UNDER_RAIL_TAG", "MOD_BARREL_TAG", "MOD_SPECIAL_TAG", "MOD_SPECIAL_TAG", "MOD_SPECIAL_TAG" };
    private static int generation;
    internal static bool IsRunning { get; private set; }
    internal static string CurrentStatus { get; private set; } = "";

    private sealed class Part
    {
        internal GameItem Item = null!;
        internal GameInventory Source = null!;
        internal GridShape Shape = null!;
        internal string Fingerprint = "";
        internal List<ItemFeature> Features = null!;
        internal TagSystem State = null!;
        internal bool Installed;
        internal bool Movable;
    }

    private sealed class Session
    {
        internal StoreClient Client = null!;
        internal GunOrderData Order = null!;
        internal GameItem Gun = null!;
        internal GameInventory GunSource = null!;
        internal GridShape GunShape = null!;
        internal string GunFingerprint = "", OrderFingerprint = "";
        internal GameSlotInventory[] Slots = null!;
        internal GameItem?[] Original = null!;
        internal List<Part> Parts = new();
        internal Dictionary<Option, List<GridShape>> Shapes = new();
        internal Problem Problem = null!;
    }

    private sealed class ReturnPlace
    {
        internal Part Part = null!;
        internal GameGridInventory Inventory = null!;
        internal GridShape Shape = null!;
    }

    internal static bool HasOrder() => ModEntry.Enabled && PlayerStore.HasInstance &&
        PlayerStore.Instance.isClientArrived && PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() is StoreClient client &&
        client.clientIntent == StoreClient.ClientIntent.GUNSMITH && !client.isBusinessComplete && client.gunOrderData != null;

    internal static string Start()
    {
        if (IsRunning) return CurrentStatus;
        if (!ModEntry.Ready(out string reason)) return reason;
        if (StowService.IsRunning) return "请等待当前收纳完成后再改装。";
        if (!TrySession(out var session, out reason)) return reason;
        int ticket = ++generation, saveSlot = PlayerStore.Instance.saveSlotId;
        var entry = EmporiumEntry.Instance;
        IsRunning = true; CurrentStatus = "正在匹配当前委托与仓库配件……可按 Esc 取消。";
        try
        {
            if (ModHelper.StartCoroutine(ModEntry.Id, RunGuarded(session!, entry, saveSlot, ticket)) == null)
                throw new InvalidOperationException("模组尚未进入运行状态，请稍后重试。 ");
        }
        catch { IsRunning = false; throw; }
        return CurrentStatus;
    }

    internal static void Cancel(string? reason = null)
    {
        generation++; IsRunning = false;
        CurrentStatus = reason ?? "改装已取消；未提交的方案没有移动任何配件。";
        if (reason != null && ModEntry.Enabled) ControlPanel.Status(CurrentStatus);
    }

    private static IEnumerator RunGuarded(Session session, EmporiumEntry entry, int saveSlot, int ticket)
    {
        yield return null;
        bool Active()
        {
            if (generation != ticket || !IsRunning) return false;
            if (!ModEntry.Enabled || !ReferenceEquals(EmporiumEntry.Instance, entry) || !PlayerStore.HasInstance ||
                PlayerStore.Instance.saveSlotId != saveSlot || !HasOrder() ||
                !ReferenceEquals(PlayerStore.Instance.currentClientInstance.GetClientBlueprint(), session.Client) ||
                !ReferenceEquals(session.Client.gunOrderData, session.Order) ||
                !ReferenceEquals(GunOrderData.ResolveOrderGun(), session.Gun))
            { CurrentStatus = "改装已停止：存档、顾客或委托枪已变化。未移动配件。"; return false; }
            if (Input.GetKeyDown(KeyCode.Escape))
            { CurrentStatus = "改装已取消；未移动任何配件。"; return false; }
            if (StowService.IsRunning || !ModEntry.Ready(out _))
            { CurrentStatus = "改装已停止：当前正在进行其他库存操作。未移动配件。"; return false; }
            if (!CanModifyGun(session.Gun) || session.Gun.parentInventory != session.GunSource ||
                OrderFingerprint(session.Order) != session.OrderFingerprint ||
                session.Slots.Where((slot, i) => !ReferenceEquals(slot.childItem, session.Original[i])).Any())
            { CurrentStatus = "改装已停止：枪械、配件或保护规则已变化。未移动配件。"; return false; }
            if (session.Parts.Any(p => !ReferenceEquals(p.Item.parentInventory, p.Source) || p.Movable &&
                (ItemPinHelper.IsPinned(p.Item) || !StorageRules.CanAutoAccess(p.Source) || StorageRules.IsQuestItem(p.Item) || StorageRules.Policy(p.Item).IsSaleProtected())))
            { CurrentStatus = "改装已停止：候选配件的位置或保护规则已变化。未移动配件。"; return false; }
            return true;
        }
        void Report(string value) { if (generation == ticket) CurrentStatus = value; }
        var work = RunSteps(session, Active, Report).GetEnumerator();
        try
        {
            while (true)
            {
                bool more;
                try { more = Active() && work.MoveNext(); }
                catch (Exception ex)
                { ModEntry.Log?.Error(ex); CurrentStatus = "改装已停止：" + ex.Message; break; }
                if (!more) break;
                yield return work.Current;
            }
        }
        finally
        {
            work.Dispose();
            if (generation == ticket)
            { IsRunning = false; if (ModEntry.Enabled && ReferenceEquals(EmporiumEntry.Instance, entry)) ControlPanel.Status(CurrentStatus); }
        }
    }

    private static IEnumerable<object?> RunSteps(Session s, Func<bool> active, Action<string> report)
    {
        var problem = s.Problem; var options = new List<Option>[s.Slots.Length];
        var mutable = new bool[s.Slots.Length]; var slice = Stopwatch.StartNew();
        for (int i = 0; i < options.Length; i++)
        {
            options[i] = new List<Option>();
            var installed = s.Original[i];
            mutable[i] = !StorageCatalog.IsNativeAccessLocked(s.Slots[i]) && StorageRules.CanAutoAccess(s.Slots[i]) &&
                (installed == null || CanUsePart(installed));
            if (installed != null)
            {
                var part = Capture(installed, true); int token = s.Parts.Count; s.Parts.Add(part);
                if (!TryOption(s, installed, i, token, false, 0, out var keep))
                    throw new InvalidOperationException("当前枪械含无法安全模拟的原有配件，已保留原状。请手动改装。");
                options[i].Add(keep!);
            }
            else options[i].Add(new Option());
            if (mutable[i] && installed != null) options[i].Add(new Option { Changes = 1 });
        }
        int scanned = 0;
        var seen = new HashSet<GameItem>();
        foreach (var source in StorageCatalog.GetSources())
        {
            if (!active()) yield break;
            foreach (var item in source.childItems.ToArray())
            {
                if (seen.Add(item) && IsLooseCandidate(item, source))
                {
                    int token = s.Parts.Count; var part = Capture(item, false);
                    bool useful = false;
                    for (int slot = 0; slot < options.Length; slot++)
                        if (mutable[slot] && TryOption(s, item, slot, token, true, 1, out var option))
                        { options[slot].Add(option!); useful = true; }
                    if (useful) s.Parts.Add(part);
                }
                scanned++;
                if (slice.Elapsed.TotalMilliseconds >= 3)
                { report($"正在匹配配件：已检查 {scanned} 件。可按 Esc 取消。"); yield return null; slice.Restart(); if (!active()) yield break; }
            }
        }
        // Keep enough independent copies for every slot, including multi-slot third-party parts.
        // Identical special identifiers are still disallowed by the native special-slot rule.
        var normalized = new List<Option[]>();
        foreach (var list in options)
        {
            normalized.Add(list.GroupBy(o => EquivalentKey(o)).SelectMany(g => g.Take(options.Length))
                .OrderByDescending(o => Heuristic(problem, o)).ThenBy(o => o.Cost).ThenBy(o => o.Changes).ToArray());
            if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); if (!active()) yield break; }
        }
        problem.Slots = normalized.ToArray();
        problem.Validate = choices => FitsGun(s, choices);
        var search = new GunModificationPlanner.Search(problem); double cpu = 0;
        while (!search.Complete && cpu < 1200)
        {
            if (!active()) yield break;
            slice.Restart();
            do { search.Step(16); } while (!search.Complete && slice.Elapsed.TotalMilliseconds < 3);
            cpu += slice.Elapsed.TotalMilliseconds;
            report($"正在匹配改装方案：已比较 {search.Best.Nodes:N0} 步。可按 Esc 取消。");
            yield return null;
        }
        var result = search.Best;
        if (!search.Complete) result.SearchLimited = true;
        if (!active()) yield break;
        if (result.Choices == null)
        { report(result.SearchLimited ? "本轮限时搜索未找到完整达标方案，未移动配件。可补充配件后重试，或手动改装。" : "现有可用配件无法满足全部必需条件，或交易台空间不足。未移动配件。请补充兼容配件或手动维修枪况。"); yield break; }
        if (result.Changes == 0)
        { report($"当前枪械已满足全部必需条件，附加条件 {result.BonusMet}/{s.Order.bonusRequirements.Count}。本轮无需更换配件，请手动交付。" + (result.SearchLimited ? "（采用限时搜索结果）" : "")); yield break; }
        var returns = new List<ReturnPlace>();
        foreach (var step in PreflightReturns(s, result.Choices, returns, active)) yield return step;
        if (!active()) yield break;
        ValidateUnchanged(s, result.Choices);
        string status = Commit(s, result.Choices, result.Values!, returns, result.BonusMet);
        report(status + (result.SearchLimited ? " 本轮采用限时搜索所得完整方案。" : ""));
    }

    private static bool TrySession(out Session? session, out string reason)
    {
        session = null; reason = "当前没有枪械改装委托。";
        if (!HasOrder() || NegociationUIManager.IsTradeLocked()) return false;
        var client = PlayerStore.Instance.currentClientInstance.GetClientBlueprint();
        var gun = GunOrderData.ResolveOrderGun();
        if (gun == null) { reason = "请先把本次委托枪放在交易区；只改装当前委托选中的枪械。"; return false; }
        if (!CanModifyGun(gun)) { reason = "当前委托枪已固定、被锁定或受保护，请解除相应限制后再改装。"; return false; }
        if (!TrySlots(gun, out var slots)) { reason = "这把枪没有完整的原生改装槽，暂不支持自动改装。"; return false; }
        var s = new Session { Client = client, Order = client.gunOrderData, Gun = gun, GunSource = gun.parentInventory,
            GunShape = gun.modifiedShape.Clone(), GunFingerprint = Fingerprint(gun), OrderFingerprint = OrderFingerprint(client.gunOrderData),
            Slots = slots, Original = slots.Select(slot => (GameItem?)slot.childItem).ToArray() };
        var children = GraphUtils.FindAllChildrenType<GameItem>(gun);
        if (children.Count != s.Original.Count(i => i != null) || children.Any(i => !s.Original.Contains(i) || !i.IsTag("WEAPON_MOD_TAG")))
        { reason = "枪械内部含非标准内容，已保留原状。请手动改装。"; return false; }
        var mandatory = Convert(client.gunOrderData.mandatoryRequirements);
        var bonus = Convert(client.gunOrderData.bonusRequirements);
        if (mandatory == null || bonus == null)
        { reason = "该委托包含未知条件，暂不支持安全自动改装。"; return false; }
        s.Problem = new Problem { BaseStats = StatTags.Select(t => gun.GetTagReadonly(t).GetInt()).ToArray(),
            ConditionPercent = ConditionPercent(gun), Caliber = gun.GetTagReadonly("GUN_CALIBER_TAG").GetInt(),
            Mandatory = mandatory, Bonus = bonus, BaseCells = Cells(gun.shape) };
        session = s; reason = ""; return true;
    }

    internal static bool CanModifyGun(GameItem gun) => gun != null && gun.parentInventory != null && gun.IsTag("ADV_GUN_TAG") &&
        GunModHelper.MayModGun(gun) && !ItemPinHelper.IsPinned(gun) && !StorageRules.IsQuestItem(gun) &&
        !StorageRules.Policy(gun).IsSaleProtected() && !LockHelper.IsCurrentlyLocked(gun) &&
        StorageRules.CanAutoAccess(gun.parentInventory) && !StorageCatalog.IsNativeAccessLocked(gun.parentInventory) &&
        (gun.contentWindow == null || !gun.contentWindow.isLocked) && !NegociationUIManager.IsTradeLocked();

    internal static bool IsLooseCandidate(GameItem item, GameInventory inventory) => item != null &&
        IsStockInventory(inventory) &&
        ReferenceEquals(item.parentInventory, inventory) && item.IsTag("WEAPON_MOD_TAG") && item.IsGameItemType("GUN_MOD") &&
        item.unitCount == 1 && item.modifiedShape != null && CanUsePart(item) &&
        GraphUtils.FindAllChildrenType<GameItem>(item).Count == 0;

    private static bool IsStockInventory(GameInventory inventory)
    {
        if (inventory is not GameGridInventory) return false;
        var entry = EmporiumEntry.Instance;
        return entry != null && (ReferenceEquals(inventory, entry.invElement) || ReferenceEquals(inventory, entry.frontInvinvElement) ||
            ReferenceEquals(inventory, entry.backInvinvElementCounter) || StorageCatalog.Find(inventory) != null);
    }

    private static bool CanUsePart(GameItem item) => StorageRules.CanAutomate(item) && !StorageRules.HasProtectedContents(item) &&
        item.unitCount == 1 && item.MayRemove() && item.MaxNumRemove() == 1;

    private static bool TrySlots(GameItem gun, out GameSlotInventory[] slots)
    {
        slots = Array.Empty<GameSlotInventory>();
        if (gun.contentWindow?.childElement is not MachineryPanelRow row) return false;
        var found = new List<GameSlotInventory>();
        foreach (string role in SlotRoles)
        {
            var grid = row.GetPanel(role)?.content as GridPixelElement;
            if (grid?.GetElement(0, 0) is not MachineryIntake intake || intake.content is not GameSlotInventory slot || slot.GetParentItem() != gun) return false;
            found.Add(slot);
        }
        slots = found.ToArray(); return slots.Distinct().Count() == SlotRoles.Length;
    }

    private static bool TryOption(Session s, GameItem item, int index, int token, bool isNew, int changes, out Option? option)
    {
        option = null; var slot = s.Slots[index];
        if (!item.IsTag("WEAPON_MOD_TAG") || !item.IsTag(SlotTags[index])) return false;
        if (!GeneralHelper.MayOwnershipAcceptItem(item, slot) ||
            item.mayInventoryAddItemFunc != null && !item.mayInventoryAddItemFunc.InvokeAllReduce(item, slot, InvokeFuncExtensions.BoolReduceOR)) return false;
        var marker = new SlotMarker { item = item, inventory = slot, itemGridShape = new GridShapeBuilder(item.modifiedShape).SetTransform(0, 0, false, slot.defaultOrientation) };
        if (!item.CheckIsStateValid(slot, marker, marker.itemGridShape, item.modifiedState) || item.MaxNumAdd(marker) < 1) return false;
        // Current conflicts can disappear when the chosen old parts are returned. Model the native
        // audited slot tags/exclusive groups now; rerun the complete native slot predicate at commit.
        var stateMods = item.GetParentStateModifier(s.Gun, marker, marker.itemGridShape, item.state);
        if ((stateMods.Item1?.Count ?? 0) != 0 || (stateMods.Item2?.Count ?? 0) != 0) return false;
        var modifiers = item.GetParentShapeModifier(s.Gun, marker, marker.itemGridShape, item.state);
        var shapes = new[] { modifiers.Item1, modifiers.Item2 }.Where(sh => sh != null).ToList();
        if (shapes.Any(sh => !SimpleShape(sh))) return false;
        option = new Option { Token = token, Identifier = item.identifier ?? "", ExclusiveGroup = GunModHelper.GetExclusiveGroup(item) ?? "",
            Percent = StatTags.Select(t => item.GetTagReadonly(t).GetInt()).ToArray(), Flat = FlatTags.Select(t => item.GetTagReadonly(t).GetInt()).ToArray(),
            Suppression = item.IsTag("MOD_SUPPRESSOR_TAG") ? item.IsGameItemType("MAKESHIFT") ? 1 : 2 : 0,
            ShapeCells = shapes.SelectMany(Cells).Distinct().ToArray(), Special = index >= 5,
            IsNew = isNew, Changes = changes, Cost = Math.Max(0, item.GetCurrentValue(false, false, true, true, 0L)) };
        s.Shapes.Add(option, shapes); return true;
    }

    private static Requirement[]? Convert(IEnumerable<GunOrderRequirement> requirements)
    {
        var result = new List<Requirement>();
        foreach (var native in requirements)
        {
            int stat = Array.IndexOf(CurrentTags, native.id);
            if (stat < 0) stat = native.id == "GUN_SUPPRESSED_TAG" ? 6 : native.id == "GUN_CALIBER_TAG" ? 7 : native.id == "GUN_SIZE_TAG" ? 8 : -1;
            if (stat < 0 || native.isCaliber && stat != 7) return null;
            result.Add(new Requirement { Stat = stat, Value = native.value, Larger = native.isMustBeLargerThan, Equal = native.isCaliber, Reward = native.reward });
        }
        return result.ToArray();
    }

    internal static int ConditionPercent(GameItem gun) => gun.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION")?.realCondition.identifier switch
    { "equipment_condition_new" => 30, "equipment_condition_excellent" => 15, "equipment_condition_fair" => -15, "equipment_condition_poor" => -30, _ => 0 };

    private static GridShape FinalShape(Session s, IReadOnlyList<Option> choices)
    {
        var builder = new GridShapeBuilder(s.Gun.shape);
        var a = s.Gun.modifiedShape.ForwardTransform(s.Gun.modifiedXOrigin, s.Gun.modifiedYOrigin);
        var b = s.Gun.modifiedShape.ForwardTransform(s.Gun.modifiedXOrigin + s.Gun.shape.width - 1, s.Gun.modifiedYOrigin + s.Gun.shape.height - 1);
        builder.SetTransform(Math.Min(a.Item1, b.Item1), Math.Min(a.Item2, b.Item2), s.Gun.modifiedShape.flipped, s.Gun.modifiedShape.orientation);
        var shapes = choices.Where(o => o.Token >= 0).SelectMany(o => s.Shapes[o]).ToList();
        builder.MergeWithLocalShapes(shapes); return builder;
    }

    private static bool FitsGun(Session s, Option[] choices) => s.GunSource.CheckShapePlacement(s.Gun, FinalShape(s, choices), new List<GameItem> { s.Gun });

    private static IEnumerable<object?> PreflightReturns(Session s, Option[] choices, List<ReturnPlace> result, Func<bool> active)
    {
        var slice = Stopwatch.StartNew();
        var pages = StorageCatalog.GetPages().Where(p => StorageRules.CanAutoAccess(p.Inventory)).OrderBy(p => p.Owner == null ? 0 : 1).ToArray();
        for (int i = 0; i < choices.Length; i++)
        {
            var old = s.Original[i];
            if (old == null || choices[i].Token >= 0 && ReferenceEquals(s.Parts[choices[i].Token].Item, old)) continue;
            var part = s.Parts.Single(p => ReferenceEquals(p.Item, old));
            ReturnPlace? found = null;
            foreach (var page in pages)
            {
                if (!StorageRules.Allows(page.Inventory, old) || !page.Inventory.MayHaveValidInventorySlot(old)) continue;
                var bounds = page.Inventory.inventoryShape;
                foreach (int orientation in new[] { part.Shape.orientation, (part.Shape.orientation + 1) % 4, (part.Shape.orientation + 2) % 4, (part.Shape.orientation + 3) % 4 })
                {
                    var pose = new GridShapeBuilder(part.Shape).SetOrientation(orientation);
                    for (int y = bounds.minY; y <= bounds.maxY - pose.globalHeight + 1 && found == null; y++)
                        for (int x = bounds.minX; x <= bounds.maxX - pose.globalWidth + 1; x++)
                        {
                            var target = new GridShapeBuilder(pose).SetPosition(x, y);
                            if (!result.Any(r => ReferenceEquals(r.Inventory, page.Inventory) && r.Shape.Intersects(target)))
                            {
                                var slot = page.Inventory.TryInventorySlot(old, 1, target);
                                if (slot != null && slot.targetItem == null && slot.numTransfer == 1)
                                { found = new ReturnPlace { Part = part, Inventory = page.Inventory, Shape = target }; break; }
                            }
                            if (slice.Elapsed.TotalMilliseconds >= 3)
                            { yield return null; slice.Restart(); if (!active()) yield break; }
                        }
                    if (found != null) break;
                }
                if (found != null) break;
            }
            if (found == null) throw new InvalidOperationException("仓库没有足够的安全空位返还旧配件。未进行改装，请先腾出空间。");
            result.Add(found);
        }
    }

    private static string Commit(Session s, Option[] choices, int[] predicted, List<ReturnPlace> returns, int bonus)
    {
        var changed = returns.Select(r => r.Part).Concat(choices.Where(o => o.Token >= 0 && o.IsNew).Select(o => s.Parts[o.Token])).Distinct()
            .OrderBy(p => p.Installed ? 1 : 0).ToArray();
        try
        {
            ValidateUnchanged(s, choices);
            foreach (var place in returns)
            {
                if (!StorageRules.CanAutoAccess(place.Inventory) || !StorageRules.Allows(place.Inventory, place.Part.Item)) throw new InvalidOperationException("旧配件返还区的规则已变化。");
                var slot = place.Inventory.TryInventorySlot(place.Part.Item, 1, place.Shape);
                if (slot == null || slot.numTransfer != 1 || slot.targetItem != null) throw new InvalidOperationException("旧配件返还空位已失效。");
            }
        }
        catch (Exception ex) { return "改装已停止，未移动配件：" + ex.Message; }
        var transaction = WholeItemTransferTransaction.Run(changed, () =>
        {
            foreach (var place in returns)
            {
                var item = place.Part.Item;
                if (!CanUsePart(item)) throw new InvalidOperationException("旧配件已不可移除。");
                Move(item, place.Inventory, place.Shape);
            }
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i].Token < 0) continue;
                var part = s.Parts[choices[i].Token]; var item = part.Item;
                if (ReferenceEquals(s.Slots[i].childItem, item)) continue;
                if (!CanUsePart(item) || !ReferenceEquals(item.parentInventory, part.Source)) throw new InvalidOperationException("来源配件已被移动或锁定。");
                Move(item, s.Slots[i], item.modifiedShape);
            }
            s.Gun.ValidateShapeState(); GunHelper.UpdateCurrentGunStats(s.Gun);
            if (!s.Order.mandatoryRequirements.All(r => r.IsMet(s.Gun)) || s.Order.CountBonusMet(s.Gun) != bonus ||
                CurrentTags.Where((tag, i) => s.Gun.GetTagReadonly(tag).GetInt() != predicted[i]).Any() ||
                s.Gun.GetTagReadonly("GUN_SUPPRESSED_TAG").GetInt() != predicted[6] || s.Gun.GetModifiedItemSize() != predicted[8] ||
                !s.GunSource.CheckInventorySlotStillValid(s.Gun, s.Gun.modifiedShape, s.Gun.modifiedState) ||
                choices.Where((o, i) => !ReferenceEquals(s.Slots[i].childItem, o.Token < 0 ? null : s.Parts[o.Token].Item)).Any() ||
                returns.Any(r => !ReferenceEquals(r.Part.Item.parentInventory, r.Inventory)))
                throw new InvalidOperationException("原生委托或摆放的最终校验未通过。");
            PlayerStore.Instance.OnWeightedAreaChanged();
        }, part =>
        {
            if (part.Item.parentInventory != null && !part.Item.parentInventory.Expel(part.Item))
                throw new InvalidOperationException("无法收回未完成方案中的配件。");
        }, part =>
        {
            part.Item.state = DeepState(part.State); part.Item.modifiedState = part.Item.state;
            part.Item.itemFeatures = new List<ItemFeature>(part.Features); part.Item.modifiedShape = part.Shape.Clone();
            if (!part.Source.UncheckedAccept(part.Item)) throw new InvalidOperationException("无法恢复配件原库存。");
        }, () => VerifyRestore(s, changed));
        if (transaction.Succeeded)
        {
            ModEntry.Log?.Log($"一键改装完成：{s.Gun.identifier} #{s.Gun.uniqueId}，装入 {choices.Count(o => o.IsNew && o.Token >= 0)} 件，返还 {returns.Count} 件；未提交委托。");
            return $"已用现有配件完成改装，满足全部必需条件，附加条件 {bonus}/{s.Order.bonusRequirements.Count}；旧配件已安全收回。请检查后手动交付。";
        }
        if (transaction.Restored) return "改装未通过原生校验，已恢复原配件和库存位置：" + transaction.Error!.Message;
        ModEntry.Log?.Error(transaction.RestoreError!);
        return "改装恢复原状时遇到异常，请勿保存，立即重新载入存档。原因为：" + transaction.RestoreError!.Message;
    }

    private static void Move(GameItem item, GameInventory inventory, GridShape pose)
    {
        var slot = inventory.TryInventorySlot(item, 1, pose);
        if (slot == null || slot.numTransfer != 1 || slot.targetItem != null || slot.TryAcceptOnce(1) != 1 || !ReferenceEquals(item.parentInventory, inventory))
            throw new InvalidOperationException("游戏拒绝配件转移。");
    }

    private static void VerifyRestore(Session s, Part[] changed)
    {
        s.Gun.ValidateShapeState(); GunHelper.UpdateCurrentGunStats(s.Gun);
        foreach (var part in changed)
            if (!ReferenceEquals(part.Item.parentInventory, part.Source) || !SameShape(part.Item.modifiedShape, part.Shape) || Fingerprint(part.Item) != part.Fingerprint)
                throw new InvalidOperationException("配件恢复校验失败。");
        if (s.Slots.Where((slot, i) => !ReferenceEquals(slot.childItem, s.Original[i])).Any() ||
            !SameShape(s.Gun.modifiedShape, s.GunShape) || Fingerprint(s.Gun) != s.GunFingerprint)
            throw new InvalidOperationException("枪械恢复校验失败。");
        PlayerStore.Instance.OnWeightedAreaChanged();
    }

    private static void ValidateUnchanged(Session s, Option[] choices)
    {
        if (!HasOrder() || !ReferenceEquals(PlayerStore.Instance.currentClientInstance.GetClientBlueprint(), s.Client) ||
            !ReferenceEquals(s.Client.gunOrderData, s.Order) || !ReferenceEquals(GunOrderData.ResolveOrderGun(), s.Gun) ||
            !CanModifyGun(s.Gun) || !ReferenceEquals(s.Gun.parentInventory, s.GunSource) ||
            !SameShape(s.Gun.modifiedShape, s.GunShape) || Fingerprint(s.Gun) != s.GunFingerprint ||
            OrderFingerprint(s.Order) != s.OrderFingerprint ||
            s.Slots.Where((slot, i) => !ReferenceEquals(slot.childItem, s.Original[i])).Any())
            throw new InvalidOperationException("委托、枪械或原配件已变化。未提交方案。");
        foreach (var part in s.Parts.Where(p => p.Installed || choices.Any(o => o.Token >= 0 && ReferenceEquals(s.Parts[o.Token], p))))
        {
            if (!ReferenceEquals(part.Item.parentInventory, part.Source) || !SameShape(part.Item.modifiedShape, part.Shape) || Fingerprint(part.Item) != part.Fingerprint)
                throw new InvalidOperationException("候选配件已变化。未提交方案。");
            bool kept = choices.Any(o => o.Token >= 0 && ReferenceEquals(s.Parts[o.Token], part) && !o.IsNew);
            if (!kept && !CanUsePart(part.Item)) throw new InvalidOperationException("候选配件已锁定或不可移除。未提交方案。");
        }
        if (!FitsGun(s, choices)) throw new InvalidOperationException("改装后枪械的空间已失效。未提交方案。");
    }

    private static Part Capture(GameItem item, bool installed) => new() { Item = item, Source = item.parentInventory,
        Shape = item.modifiedShape.Clone(), Fingerprint = Fingerprint(item), Features = new List<ItemFeature>(item.itemFeatures), State = DeepState(item.state), Installed = installed, Movable = CanUsePart(item) };

    internal static TagSystem DeepState(TagSystem source)
    {
        // The game's TagSystem.Clone copies its dictionary but shares mutable TagState values.
        var result = new TagSystem();
        foreach (var pair in source.dict) result.dict.Add(pair.Key, pair.Value.Clone());
        return result;
    }

    private static string EquivalentKey(Option o) => string.Join("/", o.Identifier, o.ExclusiveGroup, string.Join(",", o.Percent), string.Join(",", o.Flat),
        o.Suppression, string.Join(",", o.ShapeCells.OrderBy(c => c)), o.IsNew, o.Changes, o.Cost, o.IsNew ? "loose" : o.Token.ToString(CultureInfo.InvariantCulture));

    private static long Heuristic(Problem p, Option o)
    {
        long value = 0;
        foreach (var r in p.Mandatory.Concat(p.Bonus))
        {
            long change = r.Stat < 6 ? p.BaseStats[r.Stat] * (long)o.Percent[r.Stat] + o.Flat[r.Stat] * 100L : r.Stat == 6 ? o.Suppression * 100L : 0;
            value += (r.Larger ? change : -change) * (p.Mandatory.Contains(r) ? 100 : 1);
        }
        return value;
    }

    private static long[] Cells(GridShape shape)
    {
        var cells = new List<long>();
        for (int y = shape.minY; y <= shape.maxY; y++) for (int x = shape.minX; x <= shape.maxX; x++)
            if (shape.Get(x, y) == 1) cells.Add(((long)x << 32) | (uint)y);
        return cells.ToArray();
    }

    private static bool SimpleShape(GridShape shape)
    {
        if (shape.globalWidth > 64 || shape.globalHeight > 64) return false;
        for (int y = shape.minY; y <= shape.maxY; y++) for (int x = shape.minX; x <= shape.maxX; x++)
            if (shape.Get(x, y) > 1) return false;
        return true;
    }

    private static bool SameShape(GridShape a, GridShape b) => a.minX == b.minX && a.minY == b.minY && a.orientation == b.orientation &&
        a.flipped == b.flipped && a.globalWidth == b.globalWidth && a.globalHeight == b.globalHeight && Cells(a).SequenceEqual(Cells(b));

    private static string OrderFingerprint(GunOrderData o) => string.Join("|", o.isServiceOrder, o.isBestOrder, o.baseReward,
        string.Join(";", o.mandatoryRequirements.Select(r => $"{r.id}:{r.value}:{r.isMustBeLargerThan}:{r.isCaliber}:{r.reward}")),
        string.Join(";", o.bonusRequirements.Select(r => $"{r.id}:{r.value}:{r.isMustBeLargerThan}:{r.isCaliber}:{r.reward}")));

    private static string Fingerprint(GameItem item)
    {
        var b = new StringBuilder(); b.Append(item.identifier).Append('|').Append(item.uniqueId).Append('|').Append(item.unitCount).Append('|').Append(item.unitValue);
        foreach (var pair in item.state.dict.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            var t = pair.Value; b.Append('|').Append(pair.Key.Length).Append(':').Append(pair.Key).Append(':').Append(t.valueEnabled).Append(':').Append(t.valueInt).Append(':')
                .Append(t.valueLong).Append(':').Append(t.valueFloat.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(t.valueDouble.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(t.valueBool).Append(':').Append(t.valueString?.Length ?? 0).Append(':').Append(t.valueString);
        }
        foreach (var feature in item.itemFeatures) b.Append("|feature:").Append(feature.identifier);
        return b.ToString();
    }
}
