# Genuine storage discovery and automatic stowing

Verified against the installed game on 2026-11-09. This change fixes automatic storage discovery treating machinery expansion bays as ordinary storage, narrows automatic stowing to loose stock, and preserves both native and mod locks.

## Local evidence

The authoritative binary is `D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll`.

SHA256: `725CB999D019DFF1FF31BED353C7F9BB8D81A7605A15DE1F80F5A3A6CF7B1D05`.

The local `reference/GameCode` sources are an ILSpy inspection aid from this binary. They are not a dependency to ship with the mod and must not be distributed.

| Native evidence | Consequence |
| --- | --- |
| `ShipSystemDirectory.cs:110`, `:155`: `MachineBay` and `MachineBayExt` create free grids, call `ContainerHelper.InitContainerItem`, and set type `STORAGE` plus system tags. They are not type `MACHINE`. | `STORAGE`, `CONTAINER_TAG`, and a grid are insufficient to identify ordinary storage. Both `machine_bay` and `machine_bay_ext` are excluded explicitly by the strict allowlist. |
| `ShipSystemDirectory.cs:8` registers the actual cabinet IDs; `:22`, `:39`, `:73`, `:91`, `:225` create direct storage grids. | Permit the audited ordinary cabinet identities and verify their actual window structure. |
| `ShipSystemDirectory.cs:188` creates HyperTech storage with the `storage` role panel, `MachineryTabs`, and `MachineryIntake` wrapping each grid. | Permit this exact page shape only for known tab-capable cabinets. |
| `StorageExpansionHelper.cs:46`, `:120`, `:125`, `:156` restrict expansions to four cabinet IDs and use the same storage panel / tabs / intake / grid path. | Preserve each native tab's original index; do not invent page numbers from graph traversal order. |
| `ContainerItemDirectory.cs:11` through `:28` register backpacks, boxes, and `beerCase`; `:317` gives the beer case `PRE_CONTAINER_TAG` without general container/storage tags. | Permit real backpacks and boxes by identity, including the exact case-sensitive `beerCase` ID. Broad `PRE_CONTAINER_TAG` acceptance remains unsafe. |
| `UnusedDirectory.cs:105`, `:117`, `:129`, `:191`, `:391`, `:406`, `:728`, `:742` define still-registered legacy lootboxes, first-aid box, general pouches, safe and large storage bay with direct grids. | Keep these ordinary storage containers usable under the same strict structure check. |
| `ToolDirectory.cs` / `ToolboxHelper.cs` implement functional tool slots; `AmenitiesItemDirectory.cs` includes grid-backed testing/tablet packages; `MiscItemDirectory.cs` defines grid-backed dossier/trashcan. | Never recursively collect every child inventory of an arbitrary tagged item. Functional containers and machine input/output/attachment slots are excluded. |
| `LockHelper.cs:49` sets `contentWindow.isLocked`, tags children `ITEM_BEHIND_LOCK_TAG`, and disallows additions, but does not set an inventory removal lock. `:138` provides `IsCurrentlyLocked`. | UI-only native locks must also be checked before automatic extraction or arrangement; native `MayRemove()` alone is insufficient here. |
| `LiquidContainerHelper.cs:16` stores normal bottle contents as tags and type `WATER`; no storage window is created. | Ordinary bottles remain movable loose goods. Protecting functional container windows does not disable ordinary water-bottle stowing. |
| `MachineryHelper.cs:117` sets type `MACHINE` and native machinery tags. `ContainerHelper.cs:113` recognizes nestable devices. | Prevent automatic relocation of machinery/equipment shells even if a device lacks one of the usual markers. |

## Implemented rules

`StorageCatalog.PagesOf` returns only:

- A direct `PixelWindow.childElement` that is a `GameGridInventory`, for an audited storage identity.
- A grid tab, directly or wrapped once in `MachineryIntake`, inside the exact `storage` role panel of a known tab-capable cabinet.

There is no recursive visual or storage-graph fallback. Non-grid functional tabs are ignored, duplicate grids are deduplicated, and retained pages keep native indices.

Allowed cabinet identities:

`makeshift_storage_bay`, `storage_bay`, `storage_bay_large`, `storage_bay_gun`, `storage_bay_chem`, `smuggler_bay_mini`, `smuggler_bay`, `smuggler_bay_mod`, `hypertech_bay`.

Allowed ordinary bags/boxes:

`fanny_pack`, `satchel`, `kids_backpack`, `kids_backpack_large`, `backpack_small`, `backpack_medium`, `backpack_medium_military`, `backpack_large`, `backpack_large_military`, `sec_box`, `evidence_box`, `lou_locker`, `med_box`, `eng_box`, `service_box`, `beerCase`, `lootbox_small`, `lootbox_medium`, `lootbox_large`, `safe`, `large_storage_bay`, `pouch_general`, `pouch_general_large`, `firstaid_box`.

Only `storage_bay`, `storage_bay_large`, `storage_bay_gun`, `storage_bay_chem`, and `hypertech_bay` may use the audited tab structure. Unknown future or third-party container IDs fail closed until separately audited. Complex legacy modular/technician backpacks remain excluded because their structure mixes storage with functional attachment/power slots.

`GetPages()` retains main inventory, owned front counter, and the non-trade rear work counter as loose-stock roots. It recursively discovers genuine owned bags/boxes, including owned boxes located in hidden/showcase areas, without treating loose hidden/showcase goods as source stock. It does not descend into machine bays or other excluded functional containers.

`GetStoragePages()` returns only pages with a real container owner, for storage UI and automatic destinations. Native locked pages remain identifiable for UI/policy purposes; automatic callers must additionally check access locks.

`IsNativeAccessLocked(inventory)` checks:

- Current and ancestor inventory insert/remove locks.
- Any ancestor `PixelWindow.isLocked`.
- Any ancestor native container for `LockHelper.IsCurrentlyLocked` / `LOCK_STATUS`.

`GetSources()` applies native access protection and existing `StorageRules.IsLocked`, preserving mod whole-container locks, page locks and ancestral locks on nested boxes. Native locks and mod sale locks remain independent.

`StowService.IsMovableStock(item)` excludes characters, storage/equipment shells, system items, nestable devices, native machinery tags/types, and items behind native locks. Audited consumable packages are a narrow exception described below: they can move whole without exposing their internal grids as source/target storage. Existing `StorageRules.CanAutomate` still determines ownership, pinning, removability and mod-lock eligibility. Normal food, tools, daily goods, medical supplies and bottles remain eligible. Automatic arrangement at the end of stowing visits genuine unlocked storage targets only, not root areas containing installed equipment. Explicitly requested current-page arrangement retains its separate manual semantics.

## Follow-up: screenshot packages and bidirectional automation locks

The reported water-page failure was investigated against the two provided screenshots, the native definitions and a **read-only** inspection of the latest actual `save_1.es3` under the game's verified `Questing Goose Studio/Probably Stolen` persistent-data directory. No user save was copied into the deliverable or modified.

The circled narrow blue items correspond to `water_test_container` (unique ID 7984, saved main-grid position x13/y8, 1×2) and `water_tablet_container` (unique ID 7995, x12/y7, 1×3). These are the water-testing kit and a bottle of purification tablets, not the ordinary `bottled_water` item. Both have native types `HOUSEHOLD_GOOD` and `WATER_PURIFICATION_SUPPLY`, plus `PRE_CONTAINER_TAG` and a direct grid content window. Native evidence is `AmenitiesItemDirectory.cs:150` and `:337`.

The saved water page on cabinet 1267 was unlocked and included `tag:WATER_PURIFICATION_SUPPLY`. Its category therefore matched both packages. The previous blanket rejection of every content-window/PRE-container shell removed the two packages before candidate counting, explaining the repeated “0 moved / 0 arranged / 0 remaining” messages despite a matching target. The read-only save evidence supports the cause; it is not a claim of inspecting live scene memory.

Four audited portable-consumable identities may now move as complete items: `water_test_container`, `water_tablet_container`, `nutrient_tablet_container` and `nutrifruit_seed_packet`. Acceptance requires the real direct-grid shape and the expected PRE-container or storable-package marker. All machinery/system/device exclusions still apply first. Unknown package IDs, real storage shells and arbitrary functional equipment remain excluded. Package contents do not enter storage discovery or source scanning, and whole-package movement is rejected if the package or any contained item/window/inventory carries a mod/native protection lock. The stocking service's separate nonempty-descendant-item exclusion continues to prevent selling a filled package's contents.

Ordinary native bottles are defined by `FoodItemDirectory.cs:164`, `:174`, `:184`, `:303` and `:313`, using `WaterHelper.InitLiquidContainerItem`. They have no storage-content window and are not PRE-containers. `WaterHelper.UpdateContainerGoodType` (`WaterHelper.cs:586`) uses `WATER` for filled bottles and `HOUSEHOLD_GOOD` for empty bottles. Tests preserve that distinction: an empty bottle remains movable, but a strict `tag:WATER` label does not fabricate a match for an empty native household-good bottle.

`StorageRules.CanAutoAccess(inventory)` is the new shared guard for **automatic access in either direction**. It combines existing mod/ancestor locks with native access locks, without changing `Allows` (manual placement/category restrictions) or the signature of `CanAutomate`. Stow source scanning, automatic target discovery, destination rechecks and final automatic arrangement all use this guard. A locked page or whole cabinet therefore cannot be extracted from, filled, or automatically arranged; sibling unlocked tabs remain available. Explicit manual placement and explicit current-page arrangement retain their existing semantics. `HasProtectedContents(item)` provides the separate whole-package subtree protection check.

`StowService.ScanSources` counts pinned, locked, equipment/storage-shell, unowned and native-nonremovable items separately. Final status also reports already-stored items, so empty candidate sets, properly stored stock and actual placement failures no longer collapse into an unexplained “0/0/0”. Explicit offer reclamation remains opt-in and must still honor native/mod source locks.

## Regression verification

Command:

```powershell
dotnet run --project tests/StorageCatalog.NativeTests.csproj -c Release
```

Result after screenshot/lock regression additions: **342 checks passed**, with no compiler warnings/errors. Coverage retains explicit loose `MODULE`, `MODULE_TAG` and `SHIP_MODULE_TAG` eligibility cases, so fixing machine-bay routing does not exclude ordinary module goods.

The test project references the real installed `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll` and `Newtonsoft.Json.dll`. It links the actual mod `StorageCatalog`, `StowService`, `StorageRules` and `StoragePolicy` sources. Native objects are data-only fixtures allocated without Unity constructors. Reflection initializes their actual native fields; no replacement native item/inventory classes are used.

Only unrelated mod integration surfaces are test doubles: logging and `ArrangeService.TryArrange`. The arrangement stub throws unless an explicit capture-only assertion is active; capture records calls and performs no arrangement. Tests execute the actual `StowService.Run` only for an all-locked early return and for an empty-stock case that proves the automatic arrangement loop visits the unlocked target and never its locked sibling/root. No native item-transfer actions, rendering, Harmony patches, scene initialization or game save operations run.

The full source-pipeline tests also execute the real native `GameItemElement.IsDestroyed`, `MayRemove`, `GeneralHelper`, `ItemPinHelper` and actual `StorageRules.CanAutomate`. For the native liveness null comparison only, a data-only uninitialized render handler is given a nonzero cached-pointer field. That pointer is never dereferenced and no Unity engine method is invoked. This avoids replacing game item/inventory classes while testing actual eligibility decisions.

Coverage includes every allowed cabinet/bag/box ID; machine bay/ext false positives with all broad storage tags; real beer-case support; slot/wrapper/output-panel rejection; all native expanded-cabinet variants; original page indices and duplicate grids; recursive real-box discovery; exclusion of machine contents, unowned boxes and loose display/hidden stock; preserved main/counter sources; mod nested page/container locks; native window/status/current/ancestor inventory locks; lock release; ordinary/empty water-bottle eligibility; actual screenshot package types/markers/structure; whole-package internal-lock protection; source skip diagnostics; bidirectional target locks; capture-only automatic-arrangement scope; and opt-in trade-offer source handling.

No live gameplay validation is claimed. This work did not modify the game directory, stop the running game or alter a save. Full mod build/package/install and live acceptance are handled separately.
