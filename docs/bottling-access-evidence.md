# 固定水罐、交易台和打印机输出：原地装瓶访问

## 接口与边界

- `BottlingService.CanUseSource(GameItem? source, out string reason)`：供原生右键菜单和面板使用；执行专用来源访问检查，并检查水是否非空、组分合法、没有药剂/其他液体、没有超容量。
- `BottlingCatalog.GetItems()`：面板来源/装瓶目标候选；只是读取范围，调用方仍须检查实际用途权限。
- `BottlingAccess.CanUseSource/CanUseTarget/CanTransfer`：供装瓶服务使用的原地液体权限。逐瓶检查当前归属、实际库存成员身份和目录、锁、设备合法性；`CanTransfer` 调用真实 `source.MayTarget(target)` 与 `source.CanTarget(target)`。
- `BottlingAccess.RefreshTradeQuote(source,target)`：仅当任一方根植交易台时刷新原生报价；同时通过已打开交易界面的公开 `OpenUI()` 路径取消旧的超预算确认。没有接受交易，没有移动或重新上架物品，没有重播顾客放置回调。

装瓶既不移动来源也不移动瓶子，因此专用访问层不调用 `StorageRules.CanAutomate`，不要求 `MayRemove`，不检查 `PINNED_TAG`。固定源和固定目标均允许原地装水。通用上货、收纳与整理的固定物品规则没有因此放宽。

所有权使用 `GeneralHelper.IsItemEffectivelyOwned`，包括所有祖先容器。保留 `StorageRules.CanAutoAccess` 的祖先箱/页签锁与原生库存/窗口封锁；还检查物品自身销售保护、`LOCKED_TAG`、`LockHelper.IsCurrentlyLocked`、`ITEM_BEHIND_LOCK_TAG`、已销毁物品/设备、原生来源 `forceDisableUse/canUseOutsideCombat` 等。任何原生交互谓词的否决仍会阻止分装。

## 已核实的原生来源

同一目标程序集 SHA-256：`725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。接口依据是从该程序集反编译的本地 `reference/GameCode`；沿用 `game-assembly-evidence.json` 的记录。反编译文件不随发行包分发。

1. **固定只保护位置。** `ItemPinHelper.IsPinned` 只读取 `PINNED_TAG`；`GameItem.MayTarget` / `CanTarget` 没有固定或 `MayRemove` 条件。它们执行有效所有权与物品/库存目标谓词，`MayTarget` 另外执行来源使用封锁。`ItemSelectHandler.OnEventPress` 的原生互动也是 `MayTarget && CanTarget` 后 `Target`，无固定检查。本实现仍调用专用 `WaterHelper.TransferLiquid`，避免重播不相关的 `Target` 回调。
2. **打印机真实 ID 与包装。** `FurnitureItemDirectory.cs` 注册 `Add("bottle_printer", () => MachineBottlePrinter.BottlePrinter())`。`MachineBottlePrinter.CreateMachineInventoryWindow` 使用 `MachineHelper.Panel(..., outputGrid, "output")`；其底层网格为 `GameGridInventory(4,4)`，升级扩宽至 5/6 格。**0.2.1 漏查了 `MachineHelper.Panel` 会调用 `WrapInIntake`，把网格包装为 `MachineryIntake`，因此直接转型会遗漏真实成品格。** 0.2.2 精确解开一层该原生包装；电源 `power`、控制区 `control` 均不开放。
3. **输出范围严格限定。** 仅 ID 为 `bottle_printer`、原生 `MachineryPanelRow` 中 `output` 内容为直接 `GameGridInventory` 或单层 `MachineryIntake` 包裹的网格，且存储图最近所属物品为该打印机时接受。检查普通库存/储物页/交易台/隐藏区/展示区的直接打印机；0.2.2 额外检查这些位置的原生 `machine_bay` / `machine_bay_ext` 直接网格内安装的打印机。两种机器舱在 `ShipSystemDirectory` 中通过 `DirectoryUtils.CreateInventoryWindow` 创建直接网格，且有已核实 ID。机器舱仅作定位路径，不返回其网格或散货，不收集隐藏区/展示区散货，不递归打开电源、其他机器、加工槽、未知包装、输出中的箱子或嵌套机器舱。
4. **交易区与工作台不同。** `EmporiumEntry` 的 `backInvinvElement` 是实际交易称重区；`backInvinvElementCounter` 是另一个非交易工作台。新范围只添加交易台直接子物品；每次倒水前重新检查归属，拒绝尚未买下的顾客瓶子和售出后仍暂留台面的瓶子。常规 `StorageCatalog` 并未因此纳入交易区或机器输出。
5. **原生交易锁。** `NegociationUIManager.IsTradeLocked` 读取逮捕、交火、店铺被击中状态。交易台及其嵌套打印机输出在此状态下拒绝液体变化并给出原因；谈价面板打开本身不是这个锁，原有百分比谈价特征不被清除。
6. **报价刷新。** `GameItem.GetNegociatedValue` 动态计算当前水价/特征与瓶价；`WaterHelper.TransferLiquid` 通过 `WaterFeatureHelper` 更新水价与水特征。原生精灵更新链同时更新空瓶 `HOUSEHOLD_GOOD` / 含水 `WATER` 类别，直接放在交易区的类别变动会同步事件特征。本实现不另外清空特征或重播顾客 `OnItemPlaced`。
7. **陈旧确认。** `NegociationUIManager.UpdateUI()` 只排队 `RefreshUI()`；重建签名读取台上当前 `GetNegociatedValue`、归属、顾客拒收标签。超预算确认会显示旧差额，而确认按钮按当前列表 `ignoreBudget:true` 出售。因此成功的交易台液体变化后，如果界面已开，调用公开 `OpenUI()`（原生内部先 `HideConfirmPanel`，已打开时无匹配音效），再 `UpdateUI()`。仅可能把交易面板带到前景，不会打开原先关闭的界面。恢复当前瓶失败/成功边界也保留原检查；恢复成功后再次执行此报价刷新，刷新本身再次异常时提示重新打开交易界面。

原液体品质、全部组分比例、温度、容量、逐组分守恒、逐对深标签快照及失败恢复逻辑保留。未生成、复制、移动、拆分、销毁瓶子或水源。

## 验证

```powershell
dotnet run --project tests/BottlingAccess.NativeTests.csproj -c Release
dotnet run --project tests/LiquidPolicy.Tests.csproj -c Release
dotnet build tests/LiquidPolicy.NativeCheck.csproj -c Release
dotnet build src/DeepSpaceQoL/DeepSpaceQoL.csproj -c Release --no-restore
```

结果：

- **0.2.2 原生访问 101 项检查通过**（0.2.1 为 74 项）。测试链接生产 `BottlingAccess`、`BottlingCatalog`、`BottlingService`、`StorageCatalog`、`StorageRules` 和纯策略文件，使用实际游戏程序集的数据对象；执行真实 `MayTarget/CanTarget/MayRemove`、归属、图父级、目录、锁和打印机结构识别。唯一替代的是不相关模组日志接口。
- 固定且 `MayRemove=false` 的源和瓶子可原地访问，原生配对谓词也确实允许；使用封锁与原生物品/库存否决仍生效。
- 交易台、售出归属变化、打印机输出、真实父级验证、设备损毁、错误设备/输入槽/输出内嵌套箱排除、原生封锁及祖先页锁、箱锁、非自有祖先、逮捕/交火锁均有覆盖。
- **纯液体策略 6,027 项检查仍通过**。
- 专用原生接口编译与完整生产项目编译均 **0 警告、0 错误**。为旧 `LiquidPolicy.NativeCheck` 补上新访问/目录的链接依赖；其存储契约仍只用于编译，不是生产实现或运行测试。

0.2.2 修改测试的打印机夹具，默认包含真实的单层 intake，并调用原生 `MachineryPanelRow.RelinkStorage` / `MachineryContainerUtils.Collect` / `GameInventory.AddParent` 建立图连接，替代旧夹具直接指定网格父级。新测试先在旧生产代码复现“找不到打印机输出”，修复包装后再复现“机器舱里的三瓶未发现”，最后两处修复均通过。保留对直接输出的兼容测试，额外排除多层包装、未连接／损毁／非自有机器舱、机器舱散货、其他设备和输入；外层锁仍会拒绝。

用户第五张截图显示 0.2.1 只装了交易台两件容器，水源仍有余量，打印机里三瓶仍空；这是目录遗漏证据，不作为打印机已通过的展示图。只读核对已有存档结构也确认水瓶打印机安装于扩展机器舱中，未修改存档或把存档纳入仓库。

这些是离线数据测试，未构造 Unity 场景、运行 `TransferLiquid`、模拟 UI 点击或测试存档。打印机真实装瓶及音效／精灵、报价 UI 仍需游戏内复测；截图已观察到的交易台装瓶不代表打印机修正通过。
