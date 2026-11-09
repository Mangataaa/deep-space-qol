# Native inventory controls: evidence and integration

## Integration API

`NativeInventoryButtons.Init()` enables the controls and attempts an initial refresh. Call it on mod enable, after the native renderer is available when possible. `Refresh()` is safe to call again from game-loaded handling and the existing low-frequency main-thread loop. `Dispose()` removes owned controls, cloned objects, and generated icon assets. It should run on mod disable. The class never changes mod loader state or the native button's listeners.

## Source evidence inspected locally

- `StoreTransferButton` is a scene `TooltipInterface` implementing `IClickable`. Its native click transfers store inventory and invokes a tutorial callback. It is not a Unity UI `Button`.
- `Interactable.OnClick()` retrieves the object's `IClickable`; its non-controllable click path dispatches this method. `TooltipContainer.Awake()` caches the object's `TooltipInterface`.
- `TreeNodeRender`/`MachineryButton` implement the game's own native button rendering, pointer interaction, tooltips, hover/pressed appearance, and locale font selection.
- `PixelWindow.AddHeaderButton()` reparents and arranges native buttons in machinery window headers. Classic windows do not lay out this collection. Small machinery headers also cannot safely contain three extra controls.
- `MachineryTabs.selected` is the authoritative current tab index. `StorageCatalog.GetStoragePages()` enumerates audited true container pages; `PagesOf()` rejects machine bays and grids with an unrelated window structure.

Only mod source is distributed. These evidence descriptions do not include game binaries or decompiled game source.

## Implemented controls

1. **One-click stock:** clone the native transfer object's appearance under an inactive staging parent. Remove `StoreTransferButton` and `TooltipContainer` only from the owned clone using deferred destruction. After at least one rendered frame, recreate its native tooltip container so it caches the new independent `StockButtonAction`. Mirror sprites vertically and place the clone immediately to the original's left using its rendered width. Do not invoke the native transfer callback or tutorial callback. The click invokes `ModEntry.Execute(StockingService.Run)`.
2. **Current-page arrange:** each opened owned storage/backpack window receives a native `MachineryButton`. Resolve the owner, window identity, and live selected tab again at click time, then call `ArrangeService.Run()` only for that grid. The main inventory receives its own arrange button.
3. **Stow:** the main inventory has a nearby native `收纳` button calling the existing `StowService.Run()`, with its own native tooltip. It shares the toolbar with main-inventory arrange.
4. **Locks:** owned storage windows receive `页` and `箱` native controls with separate purpose-made pixel sprites showing an open or closed shackle. The page control toggles only the current page's sale lock; the container control toggles the whole-container sale lock. Both save through `StorageRules.Save()`. The effective closed icon remains visible on the page when its whole container is locked. Tooltips show both the page's own state and effective whole-container protection. Native tab caption markers are refreshed through the shared storage rules implementation.
5. **Labels:** final integration adds a native `标签` button alongside arrange and locks. It resolves the current page and opens `NativeStorageLabels.OpenFor()`, using the original blue label printer rather than F6. Header placement is reserved for at least 200 native pixels of width with four storage controls.

Large machinery windows use native header placement. Classic windows and narrower machinery windows use a small native toolbar immediately outside the grid, preferably above the window and below it when touching the canvas top. They do not change window geometry or grid dimensions. Closed windows lose their owned controls; reopening creates fresh controls. Global scene search for the transfer button occurs only while it is missing, at most every two seconds. No global search runs every frame.

## Validation and remaining runtime checks

The project builds against the installed game's actual managed assemblies with zero errors and zero warnings. Static inspection verified independent callbacks, delayed clone activation, current-tab resolution, native UI ownership, and disposal paths.

Live-game acceptance is still required: inspect the original transfer object's actual scene position and surrounding space, confirm the cloned upward arrow is clickable with the game's physical raycaster, open ordinary and expanded cabinets/backpacks, switch tabs, toggle both locks, and verify only the selected page is arranged. Also check narrow-window toolbars near screen edges and repeated save reload / mod disable cycles. No live-game click test or screenshot was performed by this task, and no running game process was stopped.
