using DeepSpaceQoL.Core;

namespace DeepSpaceQoL;

internal static class ControlPanel
{
    private const string WindowId = ModEntry.Id + ".panel";
    private static CustomUIWindow? window;
    private static GameGridInventory? selected;
    private static GameItem? waterSource;
    private static bool reclaimOffer;
    private static string status = "准备就绪。先选择储物页设置分类与自动操作锁，再使用一键操作。";

    public static void Close() { window?.Close(); window = null; selected = null; waterSource = null; }
    public static void Toggle() { if (window?.IsAlive == true && window.IsVisible) Close(); else Open(); }
    public static void Status(string message)
    {
        status = message;
        ModEntry.Log?.Log(message);
        NativeFeedback.Show(message);
        if (window?.IsAlive == true) window.Get("status")?.SetText(message);
    }

    public static void Open()
    {
        if (!ModEntry.Enabled || CustomUIManager.Instance == null) return;
        var pages = StorageCatalog.GetPages();
        if (pages.Count == 0) { Status("请先载入存档并进入店铺。"); return; }
        int selectedIndex = pages.ToList().FindIndex(p => ReferenceEquals(p.Inventory, selected));
        if (selectedIndex < 0) selectedIndex = pages.Count > 1 ? 1 : 0;
        selected = pages[selectedIndex].Inventory;
        var page = pages[selectedIndex];
        var b = CustomUIManager.Instance.CreateWindow(WindowId, "深空管家 · 高级设置 · 0.3.1", CustomUIManager.LAYER_OVERLAY,
            style: CustomUIManager.Style.Machinery);
        if (b == null) return;
        window = b.Handle;
        float availableHeight = 1920f * UnityEngine.Screen.height / Math.Max(1, UnityEngine.Screen.width);
        b.SetSize(680, Math.Min(690, Math.Max(300, availableHeight - 48))).Center().SetDraggable(true).SetCloseOnEscape();
        // A single flexible scroll keeps all controls reachable on short/ultrawide screens.
        b.BeginScrollFlexible();
        b.AddLabel("常用操作请使用原生窗口按钮、水源右键菜单和标签打印机。此处为高级设置与备用入口。");
        b.BeginRow().AddButton("一键上货", () => ModEntry.Execute(StockingService.Run))
            .AddButton("一键收纳整理", () => ModEntry.Execute(() => StowService.Run(reclaimOffer)))
            .AddButton("刷新容器列表", Open).End();
        b.AddToggle("收纳时同时收回交易称重区的自有商品", reclaimOffer, value => reclaimOffer = value);
        b.AddLabel("上货采用有限搜索，尽量贴近预算。备货后由你成交；柜台满时腾空后再点一次。");
        b.AddLabel(status, "status");
        b.AddHeader("储物页标签与自动操作锁");
        b.AddDropdown(pages.Select(p => (StorageRules.IsLocked(p.Inventory) ? "[锁] " : "") + p.Name).ToArray(), selectedIndex, index =>
        {
            if (index < 0 || index >= pages.Count) return;
            selected = pages[index].Inventory;
            ModHelper.RunOnMainThread(ModEntry.Id, Open);
        });
        b.AddButton("整理所选页（同物品集中，保留固定物品）", () => ModEntry.Execute(() => ArrangeService.Run(page.Inventory)));
        if (page.Owner != null)
        {
            var owner = page.Owner;
            b.AddButton("打开原生标签打印机（勾选实际游戏分类）", () => NativeStorageLabels.OpenFor(owner, page.Index));
            var policy = StorageRules.Policy(owner);
            var rule = policy.Page(page.Index);
            string label = rule.Label, categories = rule.Categories;
            bool restrict = rule.Restrict, locked = rule.SaleLocked, boxLocked = policy.ContainerLocked;
            b.AddInput("页签显示名称", label, value => label = value, "label", value => label = value);
            b.AddInput("分类：食物,水,日用品；也支持 tag:FOOD 或 id:apple", categories, value => categories = value,
                "categories", value => categories = value);
            b.AddDropdown(new[] { "添加常用分类…" }.Concat(CategoryMatcher.Names).ToArray(), 0, index =>
            {
                if (index <= 0 || index > CategoryMatcher.Names.Count) return;
                string category = CategoryMatcher.Names[index - 1];
                categories = string.Join(",", CategoryMatcher.Tokens(categories).Concat(new[] { category }).Distinct());
                window?.Get("categories")?.SetString(categories);
                if (string.IsNullOrWhiteSpace(label)) { label = category; window?.Get("label")?.SetString(label); }
            });
            b.AddToggle("只允许手动放入这些分类（剧情/博士自动入库不受影响）", restrict, value => restrict = value);
            b.AddToggle("锁定此页：自动收纳不进不出，上货、装瓶与自动整理跳过", locked, value => locked = value);
            b.AddToggle("锁定整个箱子 / 储物柜（包括全部页签）", boxLocked, value => boxLocked = value);
            b.AddButton("保存此页规则", () => ModEntry.Execute(() =>
            {
                if (!StorageCatalog.GetPages().Any(p => ReferenceEquals(p.Owner, owner) && ReferenceEquals(p.Inventory, page.Inventory)))
                    return "所选容器已变化，请刷新后重新设置。";
                var currentPolicy = StorageRules.Policy(owner);
                var current = currentPolicy.Page(page.Index);
                current.Label = (label ?? "").Replace('\n', ' ').Trim();
                if (current.Label.Length > StorageExpansionHelper.TAB_NAME_MAX_LENGTH)
                    current.Label = current.Label.Substring(0, StorageExpansionHelper.TAB_NAME_MAX_LENGTH);
                current.Categories = string.Join(",", CategoryMatcher.Tokens(categories));
                current.Restrict = restrict;
                current.SaleLocked = locked;
                currentPolicy.ContainerLocked = boxLocked;
                StorageRules.Save(owner, currentPolicy);
                if (StorageCatalog.TabsOf(owner) != null)
                    StorageExpansionHelper.SetTabName(owner, page.Index, current.Label);
                StorageRules.RefreshCaptions(owner);
                ModHelper.RunOnMainThread(ModEntry.Id, Open);
                return "已保存储物规则，随原生存档保存。分类可选多个；空分类表示通用收纳页。";
            }));
        }
        else b.AddLabel("主库存是散放区。标签与锁请设置在储物柜或箱子页上。");
        b.AddSeparator();
        b.AddHeader("一键装瓶");
        var waters = BottlingCatalog.GetItems().Where(BottlingService.IsSource).ToArray();
        int waterIndex = Array.FindIndex(waters, i => ReferenceEquals(i, waterSource));
        if (waterIndex < 0) waterIndex = 0;
        if (waters.Length > 0)
        {
            waterSource = waters[waterIndex];
            b.AddDropdown(waters.Select(i => $"{i.GetDisplayName(true)} #{i.uniqueId}").ToArray(), waterIndex,
                index => { if (index >= 0 && index < waters.Length) waterSource = waters[index]; });
            b.AddButton("从所选水源一键装瓶", () => ModEntry.Execute(() => BottlingService.Run(waterSource!)));
        }
        else b.AddLabel("未找到可用水源。支持储水箱、大水桶和水壶；锁定的储物内容会被跳过。");
        b.AddLabel("只补空瓶或相同品质、成分和近似温度的瓶子。不同品质的水不会混装。");
        b.End().Show();
    }
}
