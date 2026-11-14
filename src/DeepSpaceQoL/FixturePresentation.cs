using UnityEngine;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Visuals only. The real sign items stay in the native editor inventory.
/// Lamp items remain the actual draggable native elements on the desktop.</summary>
internal sealed class FixturePresentation
{
    private readonly TreeNodeRender root;
    private readonly Dictionary<GameItemElement, HangingVisual> hanging = new();
    private readonly Dictionary<GameItemElement, TreeNodeRender> lampShadows = new();
    private readonly GameGridInventory signs, lamps;
    private readonly Action openEditor;
    private readonly Sprite shadowSprite;
    private readonly Texture2D shadowTexture;

    internal FixturePresentation(GameGridInventory signs, GameGridInventory lamps, Action openEditor)
    {
        this.signs = signs; this.lamps = lamps; this.openEditor = openEditor;
        root = TreeNodeRender.Instantiate().SetParent(RenderHandler.current.windowRoot);
        root.gameObject.name = "DeepSpaceQoL hanging fixtures (visuals only)";
        root.image.enabled = false; root.image.raycastTarget = false;
        shadowTexture = new Texture2D(32, 10, TextureFormat.RGBA32, false) { name = "DeepSpaceQoL desktop contact shadow", filterMode = FilterMode.Point };
        var pixels = new Color[320];
        for (int y = 0; y < 10; y++) for (int x = 0; x < 32; x++)
        {
            float radius = (x - 15.5f) * (x - 15.5f) / 256 + (y - 4.5f) * (y - 4.5f) / 25;
            pixels[y * 32 + x] = new Color(.08f, .07f, .10f, Mathf.Clamp01(1 - radius) * .36f);
        }
        shadowTexture.SetPixels(pixels); shadowTexture.Apply();
        shadowSprite = Sprite.Create(shadowTexture, new Rect(0, 0, 32, 10), new Vector2(.5f, .5f), 1);
    }

    internal void Refresh(bool visible, bool editorVisible, float width, float height)
    {
        if (root == null) return;
        TopLeft(root, Vector2.zero, new Vector2(width, height));
        root.gameObject.SetActive(visible && !editorVisible);
        var items = signs.childItems.OfType<GameItemElement>()
            .Where(i => i.handler != null && !i.IsDestroyed() && ReferenceEquals(i.parentInventory, signs))
            .OrderBy(i => i.identifier, StringComparer.Ordinal).ThenBy(i => i.uniqueId).ToArray();
        var live = new HashSet<GameItemElement>(items);
        foreach (var old in hanging.Keys.Where(i => !live.Contains(i)).ToArray()) { hanging[old].Destroy(); hanging.Remove(old); }
        if (visible && !editorVisible)
        {
            var sizes = items.Select(i => new DisplayFixtureLayout.Size(i.background.rectTransform.rect.width, i.background.rectTransform.rect.height)).ToArray();
            var poses = DisplayFixtureLayout.Hanging(width, height, sizes);
            for (int i = 0; i < items.Length && i < poses.Length; i++)
            {
                if (!hanging.TryGetValue(items[i], out var visual)) hanging[items[i]] = visual = new HangingVisual(root, openEditor);
                visual.Refresh(items[i], poses[i]);
            }
        }
        RefreshLampShadows(visible);
    }

    private void RefreshLampShadows(bool visible)
    {
        var items = lamps.childItems.OfType<GameItemElement>().Where(i => i.handler != null && !i.IsDestroyed()).ToArray();
        var live = new HashSet<GameItemElement>(items);
        foreach (var old in lampShadows.Keys.Where(i => !live.Contains(i)).ToArray())
        { if (lampShadows[old] != null) Object.Destroy(lampShadows[old].gameObject); lampShadows.Remove(old); }
        foreach (var item in items)
        {
            if (!lampShadows.TryGetValue(item, out var shadow) || shadow == null)
            {
                shadow = TreeNodeRender.Instantiate().SetParent(lamps.inner);
                shadow.gameObject.name = "DeepSpaceQoL lamp contact shadow";
                shadow.image.sprite = shadowSprite; shadow.image.raycastTarget = false;
                shadow.rectTransform.SetAsFirstSibling(); lampShadows[item] = shadow;
            }
            int grid = UISettings.current.gridSize;
            var shape = item.modifiedShape;
            TopLeft(shadow, new Vector2((shape.minX + shape.globalWidth * .12f) * grid, -(shape.minY + shape.globalHeight - .20f) * grid),
                new Vector2(shape.globalWidth * .85f * grid, grid * .34f));
            shadow.gameObject.SetActive(visible);
        }
    }

    internal void Destroy()
    {
        foreach (var visual in hanging.Values) visual.Destroy(); hanging.Clear();
        foreach (var shadow in lampShadows.Values) if (shadow != null) Object.Destroy(shadow.gameObject); lampShadows.Clear();
        if (root != null) Object.Destroy(root.gameObject);
        Object.Destroy(shadowSprite); Object.Destroy(shadowTexture);
    }

    private static void TopLeft(TreeNodeRender node, Vector2 position, Vector2 size)
    {
        var rect = node.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private sealed class HangingVisual
    {
        private readonly TreeNodeRender holder, leftCord, rightCord, leftPin, rightPin, shadow, glow, artwork;
        private readonly List<TreeNodeRender> overlays = new();
        internal HangingVisual(TreeNodeRender parent, Action click)
        {
            holder = TreeNodeRender.Instantiate().SetParent(parent);
            holder.image.enabled = false; holder.image.raycastTarget = false;
            leftCord = Part(holder); rightCord = Part(holder); leftPin = Part(holder); rightPin = Part(holder);
            shadow = Part(holder); glow = Part(holder); artwork = Part(holder);
            artwork.InitEventHandler();
            artwork.eventHandler.mouseClickFunc.Add(_ => { click(); return true; });
        }
        private static TreeNodeRender Part(TreeNodeRender parent)
        {
            var part = TreeNodeRender.Instantiate().SetParent(parent);
            part.image.raycastTarget = false; return part;
        }
        internal void Refresh(GameItemElement item, DisplayFixtureLayout.Pose pose)
        {
            TopLeft(holder, new Vector2(pose.X, -pose.Y), new Vector2(pose.Width, pose.Height));
            holder.rectTransform.localScale = Vector3.one * pose.Scale;
            var size = item.background.rectTransform.rect.size;
            float cord = pose.Cord / pose.Scale;
            var rope = new Color32(91, 82, 91, 210); var pin = new Color32(151, 151, 141, 255);
            float a = Mathf.Round(size.x * .22f), b = Mathf.Round(size.x * .78f);
            Line(leftCord, a, cord, .65f, cord, rope); Line(rightCord, b, cord, .65f, cord, rope);
            Line(leftPin, a - .7f, cord - 1, 2, 1.2f, pin); Line(rightPin, b - .7f, cord - 1, 2, 1.2f, pin);
            // Upright presentation has its own transforms. Never scale or rotate the
            // real item, which prevents cumulative shrinking and mismatched hitboxes.
            TopLeft(shadow, new Vector2(1.1f, -1.4f), size);
            shadow.image.sprite = item.background.image.sprite; shadow.image.color = new Color(.07f, .05f, .10f, .42f);
            bool neon = item.identifier.StartsWith("sign_", StringComparison.Ordinal);
            TopLeft(glow, new Vector2(-.7f, .7f), size + Vector2.one * 1.4f);
            glow.image.sprite = item.background.image.sprite; glow.image.color = new Color(1, 1, 1, neon ? .19f : 0);
            TopLeft(artwork, Vector2.zero, size);
            artwork.image.sprite = item.background.image.sprite;
            artwork.image.color = item.background.image.color;
            artwork.image.enabled = true;
            // Reuse the native emissive artwork too; copies have no inventory or events.
            var mods = item.GetActiveInnateSpriteMods().Where(n => n.image != null && n.image.enabled && n.image.sprite != null).ToArray();
            for (int i = 0; i < mods.Length; i++)
            {
                if (i == overlays.Count) overlays.Add(Part(holder));
                var target = overlays[i]; var source = mods[i];
                target.gameObject.SetActive(true); target.image.sprite = source.image.sprite; target.image.color = source.image.color;
                TopLeft(target, source.rectTransform.anchoredPosition - item.background.rectTransform.anchoredPosition, source.rectTransform.sizeDelta);
                target.rectTransform.pivot = source.rectTransform.pivot;
                target.rectTransform.localScale = source.rectTransform.localScale;
                target.rectTransform.localEulerAngles = source.rectTransform.localEulerAngles;
            }
            for (int i = mods.Length; i < overlays.Count; i++) overlays[i].gameObject.SetActive(false);
        }
        private static void Line(TreeNodeRender line, float x, float up, float width, float height, Color color)
        {
            TopLeft(line, new Vector2(x, up), new Vector2(width, height));
            line.image.sprite = RenderHandler.current.fill; line.image.color = color;
        }
        internal void Destroy() { if (holder != null) Object.Destroy(holder.gameObject); }
    }
}
