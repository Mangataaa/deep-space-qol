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
2. **打印机真实 ID。** `FurnitureItemDirectory.cs` 注册 `Add("bottle_printer", () => MachineBottlePrinter.BottlePrinter())`，不是凭翻译名称推断。`MachineBottlePrinter.CreateMachineInventoryWindow` 使用 `MachineHelper.Panel(..., outputGrid, "output")`；输出为直接 `GameGridInventory(4,4)`，升级扩宽至 5/6 格。电源是独立 `power` 槽，控制区角色 `control`。打印按钮产生 `small_bottled_water`、`bottled_water`、`large_bottled_water`。
3. **输出范围严格限定。** 仅 ID 精确为 `bottle_printer`、原生 `MachineryPanelRow` 中 `output` 角色内容为直接 `GameGridInventory`，且存储图回溯的最近所属物品就是该打印机时接受。只检查库存/普通储物页/交易台直接机器，以及隐藏区和展示区的直接自有打印机；不收集那里的散放商品。不会递归打开打印机电源、任意其他机器输入/输出、加工槽，或机器输出中的箱子。
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

- **原生访问 74 项检查通过**。测试链接生产 `BottlingAccess`、`BottlingCatalog`、`BottlingService`、`StorageCatalog`、`StorageRules` 和纯策略文件，使用实际安装游戏程序集的数据对象；执行真实 `MayTarget/CanTarget/MayRemove`、归属、图父级、目录、锁、打印机结构识别等方法。唯一替代的是不相关模组日志接口。
- 固定且 `MayRemove=false` 的源和瓶子可原地访问，原生配对谓词也确实允许；使用封锁与原生物品/库存否决仍生效。
- 交易台、售出归属变化、打印机输出、真实父级验证、设备损毁、错误设备/输入槽/输出内嵌套箱排除、原生封锁及祖先页锁、箱锁、非自有祖先、逮捕/交火锁均有覆盖。
- **纯液体策略 6,027 项检查仍通过**。
- 专用原生接口编译与完整生产项目编译均 **0 警告、0 错误**。为旧 `LiquidPolicy.NativeCheck` 补上新访问/目录的链接依赖；其存储契约仍只用于编译，不是生产实现或运行测试。

这些是离线数据测试，未构造 Unity 场景、运行 `TransferLiquid`、模拟 UI 点击或测试存档。真实装瓶后的音效/精灵、当前游戏画面、报价 UI 取消确认、打印机成品格保持不变仍需游戏内验收。没有修改游戏安装目录或版本/打包脚本。
