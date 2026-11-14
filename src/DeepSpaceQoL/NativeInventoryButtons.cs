using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Owned native controls; Refresh is intended for the mod's low-frequency main-thread loop.</summary>
internal static class NativeInventoryButtons
{
    private static readonly Dictionary<PixelWindow, WindowButtons> Windows = new();
    private static StoreTransferButton? transfer;
    private static GameObject? stockClone, inactiveRoot;
    private static float nextTransferSearch;
    private static int cloneFrame;
    private static bool cloneHasSpriteRenderer;
    private static bool enabled;
    private static Sprite? lockedIcon, unlockedIcon;
    private static Texture2D? lockedTexture, unlockedTexture;

    public static void Init()
    {
        enabled = true;
        Refresh();
    }

    public static void Refresh()
    {
        if (!enabled || !ModEntry.Enabled) return;
        var entry = EmporiumEntry.Instance;
        if (entry?.invElement == null || RenderHandler.current?.windowRoot == null) return;
        RefreshStockButton();

        var visible = new Dictionary<PixelWindow, GameItem?>();
        if (IsVisible(entry.invWindow)) visible[entry.invWindow] = null;
        foreach (var page in StorageCatalog.GetStoragePages())
        {
            var owner = page.Owner;
            var window = owner?.contentWindow;
            if (owner != null && IsVisible(window)) visible[window!] = owner;
        }

        foreach (var pair in Windows.ToArray())
            if (!visible.ContainsKey(pair.Key) || pair.Key.handler == null)
            {
                pair.Value.Destroy();
                Windows.Remove(pair.Key);
            }
        foreach (var pair in visible)
        {
            if (!Windows.TryGetValue(pair.Key, out var buttons))
            {
                buttons = new WindowButtons(pair.Key, pair.Value);
                Windows.Add(pair.Key, buttons);
            }
            buttons.Refresh();
        }
    }

    public static void Dispose()
    {
        enabled = false;
        foreach (var buttons in Windows.Values) buttons.Destroy();
        Windows.Clear();
        DestroyStockClone();
        transfer = null;
        nextTransferSearch = 0;
        if (lockedIcon != null) Object.Destroy(lockedIcon);
        if (unlockedIcon != null) Object.Destroy(unlockedIcon);
        if (lockedTexture != null) Object.Destroy(lockedTexture);
        if (unlockedTexture != null) Object.Destroy(unlockedTexture);
        lockedIcon = unlockedIcon = null;
        lockedTexture = unlockedTexture = null;
    }

    private static bool IsVisible(PixelWindow? window) => window?.handler != null &&
        window.IsVisible() && window.handler.gameObject.activeInHierarchy;

    private static void RefreshStockButton()
    {
        // The original is a scene Interactable. Search only when absent, at most once every two seconds.
        if (transfer == null)
        {
            DestroyStockClone();
            if (Time.unscaledTime < nextTransferSearch) return;
            nextTransferSearch = Time.unscaledTime + 2f;
            transfer = Object.FindObjectsOfType<StoreTransferButton>(true)
                .FirstOrDefault(b => b != null && b.gameObject.scene.IsValid());
            if (transfer == null) return;
        }
        if (stockClone == null)
        {
            inactiveRoot = new GameObject("DeepSpaceQoL inactive clone staging");
            inactiveRoot.SetActive(false);
            stockClone = Object.Instantiate(transfer.gameObject, inactiveRoot.transform, false);
            stockClone.name = "DeepSpaceQoL one-click stock";
            stockClone.SetActive(false);
            foreach (var original in stockClone.GetComponents<StoreTransferButton>()) Object.Destroy(original);
            // TooltipContainer caches TooltipInterface in Awake. Recreate it only on our inactive clone.
            foreach (var tooltip in stockClone.GetComponents<TooltipContainer>()) Object.Destroy(tooltip);
            var action = stockClone.AddComponent<StockButtonAction>();
            action.Description = "一键上货：按当前买家的需求和剩余预算，将自有未锁定物品放到交易区。不会自动确认出售。";
            var interactable = stockClone.GetComponent<Interactable>();
            if (interactable != null) interactable.isControlable = false;
            var renderers = stockClone.GetComponentsInChildren<SpriteRenderer>(true);
            cloneHasSpriteRenderer = renderers.Length > 0;
            foreach (var sprite in renderers) sprite.flipY = !sprite.flipY;
            cloneFrame = Time.frameCount;
        }
        if (Time.frameCount <= cloneFrame) return;
        if (stockClone.GetComponent<TooltipContainer>() == null) stockClone.AddComponent<TooltipContainer>();
        stockClone.transform.SetParent(transfer.transform.parent, false);
        stockClone.transform.localRotation = transfer.transform.localRotation;
        stockClone.transform.localScale = transfer.transform.localScale;
        // Image-only controls need the entire cloned visual mirrored instead of SpriteRenderer.flipY.
        if (!cloneHasSpriteRenderer)
        {
            var scale = stockClone.transform.localScale;
            stockClone.transform.localScale = new Vector3(scale.x, -scale.y, scale.z);
        }
        float width = LocalButtonWidth(transfer.gameObject);
        var location = transfer.transform.localPosition;
        stockClone.transform.localPosition = new Vector3(location.x - width - Mathf.Max(width * 0.12f, 0.01f), location.y, location.z);
        stockClone.SetActive(transfer.gameObject.activeSelf);
        if (inactiveRoot != null) { Object.Destroy(inactiveRoot); inactiveRoot = null; }
        var sourceControl = transfer.GetComponent<Interactable>();
        var cloneControl = stockClone.GetComponent<Interactable>();
        if (sourceControl != null && cloneControl != null) cloneControl.isAvailable = sourceControl.isAvailable;
    }

    private static float LocalButtonWidth(GameObject original)
    {
        var parent = original.transform.parent;
        var sprites = original.GetComponentsInChildren<SpriteRenderer>(true);
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var sprite in sprites)
        {
            if (sprite.sprite == null) continue;
            var bounds = sprite.bounds;
            var a = parent != null ? parent.InverseTransformPoint(bounds.min) : bounds.min;
            var b = parent != null ? parent.InverseTransformPoint(bounds.max) : bounds.max;
            min = Mathf.Min(min, a.x, b.x); max = Mathf.Max(max, a.x, b.x);
        }
        if (max > min) return max - min;
        if (original.transform is RectTransform rect)
            return Mathf.Abs(rect.rect.width * rect.localScale.x);
        return 0.32f;
    }

    private static void DestroyStockClone()
    {
        if (stockClone != null) { stockClone.SetActive(false); Object.Destroy(stockClone); stockClone = null; }
        if (inactiveRoot != null) { Object.Destroy(inactiveRoot); inactiveRoot = null; }
    }

    private static Sprite LockIcon(bool locked)
    {
        if (lockedIcon == null) lockedIcon = MakeLock(true, out lockedTexture);
        if (unlockedIcon == null) unlockedIcon = MakeLock(false, out unlockedTexture);
        return locked ? lockedIcon! : unlockedIcon!;
    }

    private static Sprite MakeLock(bool locked, out Texture2D texture)
    {
        // Small purpose-made pixel icons, with an open shackle when the sale lock is off.
        texture = new Texture2D(9, 10, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "DeepSpaceQoL sale-lock icon" };
        var pixels = new Color[90];
        Color color = locked ? new Color32(111, 55, 41, 255) : new Color32(68, 85, 74, 255);
        for (int y = 0; y < 5; y++) for (int x = 1; x < 8; x++) pixels[y * 9 + x] = color;
        for (int y = 5; y < 8; y++)
        {
            pixels[y * 9 + 2] = color;
            if (locked) pixels[y * 9 + 6] = color;
            else pixels[y * 9 + 8] = color;
        }
        for (int x = locked ? 3 : 4; x < (locked ? 6 : 8); x++) pixels[8 * 9 + x] = color;
        if (!locked) pixels[7 * 9 + 3] = color;
        pixels[2 * 9 + 4] = Color.clear;
        texture.SetPixels(pixels); texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 9, 10), new Vector2(0.5f, 0.5f), 1f);
    }

    private sealed class WindowButtons
    {
        private readonly PixelWindow window;
        private readonly GameItem? owner;
        private readonly List<MachineryButton> controls = new();
        private readonly MachineryButton? pageLock, containerLock;
        private readonly bool useHeader;
        private bool? oldPageLock, oldContainerLock;
        private int oldPage = -1;

        internal WindowButtons(PixelWindow window, GameItem? owner)
        {
            this.window = window; this.owner = owner;
            // Small containers cannot fit three header controls without covering the native title/close button.
            useHeader = window.style == WindowStyle.Machinery && window.widthPixels >= (owner == null ? 112 : 200);
            Add(new MachineryButton("整理", () => Run(SortCurrent), 32, 16)
                .SetDescription("整理当前打开的这一页：同种物品相邻，紧凑摆放。不会整理其他页。"));
            if (owner == null)
                Add(new MachineryButton("收纳", () => Run(() => StowService.Start()), 32, 16)
                    .SetDescription("一键收纳整理：按分类标签归入储物柜或箱子，缺少标签时使用可用的储物空间。"));
            else
            {
                Add(new MachineryButton("标签", () => Run(OpenLabels), 32, 16)
                    .SetDescription("打开原生标签打印机，为当前页勾选游戏分类。名称和分类分别保存。"));
                pageLock = new MachineryButton("页", () => Run(() => ToggleLock(false)), 32, 16).SetIcon(LockIcon(false));
                containerLock = new MachineryButton("箱", () => Run(() => ToggleLock(true)), 32, 16).SetIcon(LockIcon(false));
                Add(pageLock); Add(containerLock);
            }
        }

        private void Add(MachineryButton button)
        {
            controls.Add(button);
            button.handler.gameObject.name = "DeepSpaceQoL " + button.text;
            if (useHeader) window.AddHeaderButton(button);
            else button.handler.SetParent(window.handler);
        }

        private void Run(Func<string> action)
        {
            ModEntry.Execute(action);
            Refresh();
        }

        private StoragePage? CurrentPage()
        {
            if (owner == null || !IsVisible(window) || !ReferenceEquals(owner.contentWindow, window)) return null;
            var pages = StorageCatalog.PagesOf(owner);
            var tabs = StorageCatalog.TabsOf(owner);
            if (tabs != null) return pages.FirstOrDefault(p => p.Index == tabs.selected);
            return pages.FirstOrDefault(p => p.Inventory.handler != null && p.Inventory.handler.gameObject.activeInHierarchy);
        }

        private string SortCurrent()
        {
            if (!IsVisible(window)) return "目标储物窗口已关闭。";
            if (owner == null)
            {
                var entry = EmporiumEntry.Instance;
                return ReferenceEquals(entry?.invWindow, window) && entry!.invElement != null
                    ? ArrangeService.Run(entry.invElement) : "主库存已变化，请重新打开窗口。";
            }
            var current = CurrentPage();
            return current == null ? "未找到当前打开的储物页，请重新打开储物柜。" : ArrangeService.Run(current.Inventory);
        }

        private string ToggleLock(bool whole)
        {
            var current = CurrentPage();
            if (owner == null || current == null) return "未找到当前打开的储物页。";
            var policy = StorageRules.Policy(owner);
            if (!policy.IsValid()) return "储物规则版本不兼容，已保留锁定和原始数据。";
            bool value;
            if (whole) value = policy.ContainerLocked = !policy.ContainerLocked;
            else value = policy.Page(current.Index).SaleLocked = !policy.Page(current.Index).SaleLocked;
            StorageRules.Save(owner, policy);
            return (whole ? "整箱" : "当前页") + (value ? "已锁定：自动收纳不进不出，上货、装瓶和自动整理跳过；仍可手动操作。" : "已解除自动操作锁定。") +
                (!whole && policy.ContainerLocked ? "整箱锁定仍然生效。" : "");
        }

        private string OpenLabels()
        {
            var current = CurrentPage();
            if (owner == null || current == null) return "未找到当前打开的储物页。";
            NativeStorageLabels.OpenFor(owner, current.Index);
            return "已打开当前储物页的标签打印机。勾选分类立即生效，随原生存档保存。";
        }

        internal void Refresh()
        {
            if (!IsVisible(window)) return;
            if (owner != null)
            {
                var current = CurrentPage();
                var policy = StorageRules.Policy(owner);
                bool page = current != null && policy.Page(current.Index).SaleLocked;
                bool whole = policy.ContainerLocked || !policy.IsValid();
                if (page != oldPageLock || current?.Index != oldPage || whole != oldContainerLock)
                {
                    pageLock!.SetIcon(LockIcon(page || whole));
                    pageLock.SetDescription(current == null ? "未找到当前储物页。" :
                        $"第 {current.Index + 1} 页自动操作锁：{(page ? "已锁定" : "未锁定")}。锁定后自动收纳不进不出，上货、装瓶与自动整理跳过。" +
                        (whole ? "当前整箱锁定也在生效。" : ""));
                    oldPageLock = page; oldPage = current?.Index ?? -1;
                }
                if (whole != oldContainerLock)
                {
                    containerLock!.SetIcon(LockIcon(whole));
                    containerLock.SetDescription("整箱自动操作锁：" + (whole ? "已锁定" : "未锁定") + "。自动收纳不进不出，保护所有页和内部箱子的物品；仍可手动操作。" );
                    // Update the page icon when a whole-container lock changes.
                    pageLock!.SetIcon(LockIcon(page || whole));
                    oldContainerLock = whole;
                }
                pageLock!.SetDisabled(current == null || !policy.IsValid());
                containerLock!.SetDisabled(current == null || !policy.IsValid());
            }
            if (!useHeader) PositionOutsideWindow();
        }

        private void PositionOutsideWindow()
        {
            // Classic windows have no native header-button layout. Put a native toolbar outside the grid.
            // Prefer above the window; if it touches the canvas top, put it immediately below instead.
            var root = RenderHandler.current.windowRoot.rectTransform;
            var position = root.InverseTransformPoint(window.rectTransform.position);
            float y = position.y + 18f > 0 ? -window.heightPixels - 2f : 18f;
            int x = 0;
            foreach (var button in controls)
            {
                button.rectTransform.anchoredPosition = new Vector2(x, y);
                button.rectTransform.SetAsLastSibling();
                x += button.widthPixels + 2;
            }
        }

        internal void Destroy()
        {
            foreach (var button in controls)
            {
                if (button.handler == null) continue;
                if (useHeader && window.handler != null && RenderHandler.current?.tempRoot != null) window.RemoveHeaderButton(button);
                button.handler.gameObject.SetActive(false);
                Object.Destroy(button.handler.gameObject);
            }
            controls.Clear();
        }
    }
}

/// <summary>Own click/tooltip component on the inactive clone; never attached to the game's original.</summary>
public sealed class StockButtonAction : TooltipInterface, IClickable
{
    internal string Description = "一键上货";
    public override string GetTooltip() => Description;
    public override bool IsTooltipEnabled() => ModEntry.Enabled;
    public void OnClick()
    {
        if (!ModEntry.Enabled || Interactable.isPaused) return;
        ModEntry.Execute(StockingService.Run);
    }
}
