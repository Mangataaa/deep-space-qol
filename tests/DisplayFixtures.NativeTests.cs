using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static DataItem Item(string id, int uid)
    {
        var item = (DataItem)RuntimeHelpers.GetUninitializedObject(typeof(DataItem));
        item.Nodes = new(); item.identifier = id; item.uniqueId = uid; item.unitCount = 1;
        item.state = new TagSystem(); item.modifiedState = item.state; item.itemFeatures = new(); item.itemTypes = new();
        item.shape = new GridShapeBuilder(2, 2).SetDataFill(1).SetTransform(uid % 4, uid % 3, false, uid % 4).Build();
        item.modifiedShape = item.shape; item.spriteAtlasPath = "native"; item.spritePath = id;
        return item;
    }
    private static DataGrid Grid()
    {
        var grid = (DataGrid)RuntimeHelpers.GetUninitializedObject(typeof(DataGrid)); grid.Nodes = new(); return grid;
    }
    internal static void Run()
    {
        // Execute native storage discovery on the new UI wrapper without initializing
        // rendering. Non-storage controls must not add a node or duplicate the live grid.
        var wrappedGrid = Grid();
        var wrapper = (MachineryStack)RuntimeHelpers.GetUninitializedObject(typeof(MachineryStack));
        typeof(MachineryStack).GetField("children", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(wrapper,
            new List<PixelElement> { wrappedGrid,
                (MachineryRadioButton)RuntimeHelpers.GetUninitializedObject(typeof(MachineryRadioButton)),
                (MachineryText)RuntimeHelpers.GetUninitializedObject(typeof(MachineryText)) });
        var editorWindow = (PixelWindow)RuntimeHelpers.GetUninitializedObject(typeof(PixelWindow));
        typeof(PixelWindow).GetField("<childElement>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editorWindow, wrapper);
        var directStorage = MachineryContainerUtils.Collect(wrappedGrid);
        var wrappedStorage = MachineryContainerUtils.Collect(wrapper);
        Check(wrappedStorage.SequenceEqual(directStorage), "native UI wrapper preserves the same direct storage grid");
        Check(editorWindow.children.Count == 1 && ReferenceEquals(editorWindow.children[0], wrappedGrid), "native window exposes the grid once despite toggle and text controls");
        Check(DisplayFixtureLayout.Hanging(0, 0, Array.Empty<DisplayFixtureLayout.Size>()).Length == 0, "unavailable canvas produces no visual poses");
        var layoutRandom = new Random(410);
        foreach (var viewport in new[] { (320f, 180f), (576f, 324f), (640f, 480f), (960f, 540f), (1280f, 540f) })
        for (int count = 0; count <= 96; count += 3)
        {
            var sizes = Enumerable.Range(0, count).Select(_ => new DisplayFixtureLayout.Size(layoutRandom.Next(15, 41), layoutRandom.Next(15, 31))).ToArray();
            var poses = DisplayFixtureLayout.Hanging(viewport.Item1, viewport.Item2, sizes);
            Check(poses.Length == count, "every stored sign has a hanging presentation");
            var again = DisplayFixtureLayout.Hanging(viewport.Item1, viewport.Item2, sizes);
            for (int i = 0; i < poses.Length; i++)
            {
                var p = poses[i]; float w = viewport.Item1, h = viewport.Item2;
                Check(p.Width > 0 && p.Height > 0 && p.Scale > 0 && p.Scale <= 1, "valid scale without cumulative shrinking");
                Check(p.X >= 0 && p.Y >= p.Cord && p.X + p.Width <= w + .01f && p.Y + p.Height <= h * .131f, "art and cord stay above the clock in upper visible banks");
                Check(p.X + p.Width <= w * .291f || p.X >= w * .699f, "central shutter kept clear");
                Check(p.X + p.Width <= w * .186f || p.X >= w * .699f, "calendar's full horizontal extent kept clear, including tall bottle signs");
                Check(Math.Abs(p.Width / p.Height - sizes[i].Width / sizes[i].Height) < .001f, "native artwork aspect ratio preserved");
                Check(p.X == again[i].X && p.Y == again[i].Y && p.Scale == again[i].Scale, "refresh does not change the placement or scale");
                for (int j = 0; j < i; j++)
                {
                    var q = poses[j];
                    Check(p.X >= q.X + q.Width - .01f || q.X >= p.X + p.Width - .01f ||
                        p.Y >= q.Y + q.Height - .01f || q.Y >= p.Y + p.Height - .01f, "hanging sprites never overlap");
                }
            }
            var lamp = DisplayFixtureLayout.Lamps(viewport.Item1, viewport.Item2, 80, 30);
            Check(lamp.X >= 0 && lamp.Y >= 0 && lamp.X + 80 <= viewport.Item1 && lamp.Y + 30 <= viewport.Item2 * .611f,
                "lamp grid fits the outlined desktop area above main inventory");
        }
        Check(DisplayFixturePolicy.For(null) == DisplayFixturePolicy.Region.None, "null not accepted");
        foreach (string id in new[]{"cert1","cert2","cert3","cert4","business_permit","permit_gun_1","permit_gun_2","permit_gun_3","sign_food","sign_household","sign_water","sign_berry","sign_expedition","sign_firearm","sign_firearm_heavy","sign_material","sign_medical"})
            Check(DisplayFixturePolicy.For(Item(id, 1)) == DisplayFixturePolicy.Region.Signs, id + " eligible");
        foreach (string id in new[]{"lou_toy_box","lou_locker","water_bottle","machine_bay","dossier","fake_cert1","sign_unknown_mod"})
            Check(DisplayFixturePolicy.For(Item(id, 1)) == DisplayFixturePolicy.Region.None, id + " not silently accepted");
        Check(DisplayFixturePolicy.For(Item("black_lamp", 1)) == DisplayFixturePolicy.Region.Lamps, "lamp separate from signs");
        var store = (PlayerStore)RuntimeHelpers.GetUninitializedObject(typeof(PlayerStore)); store.savedItemFeatureList = new();
        typeof(PlayerStore).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, store);
        // Execute the installed game's real save encoder on a data-only graph matching its
        // native bag -> window -> case -> our two tray inventories layout.
        for (int scenario = 0; scenario < 24; scenario++)
        {
            var bag = Item("showcase_save_bag", -1); var root = Grid(); var signs = Grid(); var lamps = Grid(); var foreign = Grid();
            var window = new Branch(); window.Nodes.Add(root); bag.Nodes.Add(window);
            root.Nodes.Add(signs); root.Nodes.Add(lamps);
            if ((scenario & 1) != 0) root.Nodes.Insert(0, foreign);
            var goods = Item("screwdriver", 100); goods.parentInventory = root; root.Nodes.Insert(0, goods);
            var sign = Item("sign_household", 101); sign.parentInventory = signs; signs.Nodes.Add(sign);
            sign.shape = new GridShapeBuilder(2 + scenario % 3, 1 + scenario % 4).SetDataFill(1)
                .SetTransform(scenario % 6, scenario % 5, scenario % 2 == 0, scenario % 4).Build();
            sign.modifiedShape = sign.shape;
            var lamp = Item("black_lamp", 102); lamp.parentInventory = lamps; lamps.Nodes.Add(lamp);
            var nested = Item("certificate_container_fixture", 103); nested.parentInventory = signs;
            var inside = Grid(); nested.Nodes.Add(new Branch { Nodes = new() { inside } }); signs.Nodes.Add(nested);
            var innerItem = Item("water_bottle", 104); innerItem.parentInventory = inside; inside.Nodes.Add(innerItem);
            string region = DisplayFixturePolicy.Key(DisplayFixturePolicy.For(sign));
            sign.state.dict[DisplayFixturePolicy.RegionTag] = new TagState(DisplayFixturePolicy.RegionTag, "") { valueEnabled = true, valueString = region };
            var map = DisplayFixturePolicy.InventoryMap(bag, root, new[] { signs, lamps });
            var encoded = SaveManager.EncodeNodes(bag);
            Check(encoded.Count == 6 && encoded.Select(n => n.uuid).Distinct().Count() == 6, "native encoder saves each item exactly once");
            var savedRoot = encoded.Single(n => n.identifier == "showcase_save_bag");
            var nestedNode = encoded.Single(n => n.uniqueId == 103);
            int nestedIndex = nestedNode.childItemInventoryNode.Single();
            var shapeBefore = new GridShapeBuilder(sign.shape);
            DisplayFixturePolicy.PrepareNativeSave(encoded, savedRoot, map, 9, 7);
            Check(savedRoot.childItems.Count == 4, "root links all original and displayed objects");
            Check(savedRoot.childItemInventoryNode.All(i => i == 1), "every display item decodes into existing vanilla case index 1");
            Check(nestedNode.childItemInventoryNode.Single() == nestedIndex, "nested inventories never flattened");
            Check(encoded.All(n => n.identifier != "dsq_signs" && n.identifier != "dsq_lamps"), "no unknown mod item IDs required by saved data");
            Check(sign.parentInventory == signs && lamp.parentInventory == lamps && goods.parentInventory == root, "saving does not move any live item");
            Check(shapeBefore.width == sign.shape.width && shapeBefore.height == sign.shape.height && shapeBefore.orientation == sign.shape.orientation && shapeBefore.minX == sign.shape.minX, "native sizes rotations and positions preserved");
            var savedSign = encoded.Single(n => n.uniqueId == 101);
            Check(savedSign.itemShape.minX >= 0 && savedSign.itemShape.minY >= 0 &&
                savedSign.itemShape.maxX < 9 && savedSign.itemShape.maxY < 7, "without the mod saved item stays within vanilla case bounds");
            Check(!sign.state.dict.ContainsKey(DisplayFixturePolicy.LayoutTag), "save-only recovery tag cannot leak into or mutate live item state");
            var restored = Item("sign_household", 101);
            restored.state = savedSign.itemState.Clone(); restored.modifiedState = restored.state;
            restored.shape = savedSign.itemShape.Build(); restored.modifiedShape = savedSign.itemModifiedShape.Build();
            Check(DisplayFixturePolicy.TryRestoreLayout(restored, out var restoredShape, out var restoredModified) &&
                restoredShape.minX == shapeBefore.minX && restoredShape.minY == shapeBefore.minY &&
                restoredModified.minX == shapeBefore.minX && restoredModified.minY == shapeBefore.minY &&
                restoredShape.flipped == shapeBefore.flipped && restoredShape.orientation == shapeBefore.orientation,
                "reload recovers the exact tray position, rotation and flip from vanilla item data");
            Check(savedSign.itemState.dict[DisplayFixturePolicy.RegionTag].GetString() == "signs", "recovery marker is in vanilla itemState");
            Check(DisplayFixturePolicy.IsSaved(sign, DisplayFixturePolicy.Region.Signs) && !DisplayFixturePolicy.IsSaved(sign, DisplayFixturePolicy.Region.Lamps), "recovery never cross-routes lamp and sign");
            var other = new SaveItemNode { childItems = new() { 1 }, childItemInventoryNode = new() { 99 } };
            DisplayFixturePolicy.FlattenSavedRoot(other, map);
            Check(other.childItemInventoryNode.Single() == 99, "foreign inventory indices unchanged");
        }
        bool rejected = false;
        try { DisplayFixturePolicy.FlattenSavedRoot(new SaveItemNode { childItems = new() { 1 } }, new Dictionary<int, int>()); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "malformed save associations rejected");
        foreach (string bad in new[]{"2:1,2,3,4", "1:1,2", "1:-1,0,0,0", "1:2147483647,0,0,0", "1:x,0,0,0"})
        {
            var broken = Item("cert1", 8);
            broken.state.dict[DisplayFixturePolicy.LayoutTag] = new TagState(DisplayFixturePolicy.LayoutTag, "") { valueEnabled = true, valueString = bad };
            Check(!DisplayFixturePolicy.TryRestoreLayout(broken, out _, out _), "invalid layout rejected without moving item");
        }
        Console.WriteLine($"DisplayFixtures native checks passed: {checks}; real SaveManager.EncodeNodes executed on isolated data, no live saves or Unity UI.");
    }
}
sealed class DataItem : GameItem
{
    public List<GraphNodeStorage> Nodes = new();
    public DataItem() : base("fixture") { }
    public override List<GraphNodeStorage> children => Nodes;
    public override void Destroy() => throw new NotSupportedException();
    public override void Validate() { }
}
sealed class DataGrid : GameGridInventory
{
    public List<GraphNodeStorage> Nodes = new();
    public DataGrid() : base(1, 1) { }
    public override List<GraphNodeStorage> children => Nodes;
    public override List<GameItem> childItems => Nodes.OfType<GameItem>().ToList();
}
sealed class Branch : GraphNodeStorage
{
    public List<GraphNodeStorage> Nodes = new();
    public List<GraphNodeStorage> parents => new(); public List<GraphNodeStorage> children => Nodes;
    public void Validate() { } public bool MayHaveValidInventorySlot(GameItem item) => false;
    public SlotMarker TryFindOneValidInventorySlot(GameItem item) => throw new NotSupportedException();
    public bool AddParent(GraphNodeStorage node) => false; public bool RemoveParent(GraphNodeStorage node) => false;
}
