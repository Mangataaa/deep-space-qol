# Improved arrangement: implementation and evidence

This subsystem is an independent implementation. The downloaded Smart Inventory Arrange source was reviewed for feature context; none of its solver or transaction code is bundled or copied. Game decompilation is local inspection material and must not be shipped.

## Shipped interfaces inspected

- `InventorySortHelper.cs` (local decompilation), `MaySort`, `CanSort`, `IsAnchored`: insertion/player ownership constraints, pins/debris, and full-stack removability are native restrictions. The mod additionally treats each individually nonremovable item as a fixed obstacle rather than failing the entire container.
- `GridShape.cs`: shape transforms and occupancy layers; collisions compare equal nonzero layer values. Layer 1 must fit the container's zero-valued usable cells. Layer masks are retained, not approximated with rectangles.
- `GridShapeBuilder.cs`: native `SetOrientation`, `SetPosition`, preserved flipping, and `Get` are used to derive each orientation and final target. No invented mirror orientation.
- `GameGridInventory.cs`, `CheckShapePlacement`: validates shape/container/fixed-item overlap and `GameItem.CheckIsStateValid`; every candidate is checked against this method with all movable items ignored (their complete final geometry is checked independently).
- `GameGridInventory.cs`, `CheckInventorySlotStillValid`: final native parent/state/slot checks. All poses are staged simultaneously before this test so old positions do not falsely block exchanged positions.
- `GameGridInventory.cs`, `Expel`/`UncheckedAccept`: these fire remove/add callbacks. Arrangement deliberately avoids them and mutates only `modifiedShape`, retaining the original item instances, parent links, quantities and container contents.
- `GameGridInventory.Validate` and `ForceRecomputeDynamicBackground` refresh the rendering after either a commit or rollback.

## Behavior

Same identifiers are grouped; top-left packing and same-type edge alignment are preferred. Four deterministic orderings plus bounded backtracking handle alternative shapes and rotations. Only a complete, geometry-valid layout with a lower grouping/hole score than the original can be committed. Search is bounded to 120 ms / 12,000 candidate visits per container. This is a heuristic, not a proof of optimal packing.

Pins, debris and individually immovable objects remain fixed. A user may explicitly rearrange inside a sale-locked page; sale locks are a source-selection rule and do not turn into native insertion/removal locks.

The snapshot verifies item identity, parent inventory, counts, names, values and all native state-tag scalar values before committing and afterward. Fixed poses and final movable poses are verified. A failed final validation restores all original cloned shapes and refreshes the container. Items are never expelled, created, split, deleted or accepted into another inventory by this subsystem.

## Verification

`dotnet run --project tests/GridPacking.Tests.csproj -c Release` passes 423 checks for group adjacency/compaction, full 100-item dense grouping within the default budget, fixed and unusable cells, irregular masks, rotation-only solutions, legal occupancy layers, injected native constraints, failed/expired search preservation, duplicate identities, and deterministic randomized completeness/overlap/input-immutability checks. `dotnet build tests/GridPacking.NativeCheck.csproj -c Release` compiles the full integration against the installed game assemblies with zero warnings/errors. Live native acceptance and visual layout still require testing in the game.

## Known boundaries

- A bounded search can retain the original layout even when a better layout exists. It reports that explicitly.
- It groups by native identifier, not localized display-name coincidence or user category names.
- It never compacts across page boundaries or empties nested containers.
- A container larger than 65,536 cells or with shapes extending outside the declared bounds fails closed.
- State-changing third-party validation callbacks are detected, but this subsystem only restores poses; it cannot reverse arbitrary external mod side effects.
