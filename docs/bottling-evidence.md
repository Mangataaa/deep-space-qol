# 一键装瓶：实现与证据

目标：本机 `Assembly-CSharp.dll`，SHA-256 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。证据来自工作区 `reference/GameCode` 的 ILSpy 静态反编译；未把静态检查描述为游戏内执行。游戏反编译文件仅用于本地接口核对，不应随模组发行。

## 对外接口

- `DeepSpaceQoL.BottlingService.IsSource(GameItem item)`：官方储水箱 `water_tank`、大水桶 `water_jug`、滤水壶 `water_pitcher`。要求新液体系统容量/温度标签、单件物品；拒绝酒类和滴管。暂不把未知机器处理槽当作水源。
- `DeepSpaceQoL.BottlingService.CanUseSource(GameItem? source, out string reason)`：菜单与操作共享的来源访问及液体状态检查。
- `DeepSpaceQoL.BottlingService.Run(GameItem source)`：同步在 Unity 主线程操作，返回中文结果。由水源原生右键菜单或中文控制面板调用。
- 使用独立的 `BottlingCatalog` / `BottlingAccess`，保留自有、祖先箱/页签锁、原生封锁及原生液体交互条件。装瓶只改变原地液体，**允许固定的水源和瓶子，不要求 MayRemove**。新增交易台自有瓶与严格识别的水瓶打印机成品输出；不扩大通用收纳/上货范围。详见 `bottling-access-evidence.md`。
- 在店外、无存档、拖动单件或拖动分组时停止。

## 已确认的原生行为

1. **使用 `WaterHelper`，不用同名旧系统。** `WaterTankHelper.cs:75-88` 创建官方水箱，调用 `WaterHelper.InitLiquidContainerItem`。旧 `LiquidContainerHelper` 使用 `LIQUID_CONTAINER_CURRENT`，不是此版本的真实水质容器系统。
2. **容量与组分都以微升保存。** `WaterHelper.cs:51-93` 将毫升容量乘以 1000，逐个初始化 `Liquid.Liquids[i].currentPart`。`GetLiquidParts` 在 `WaterHelper.cs:257-265` 读取全部组分。`GetTotalVolume` 在 `WaterHelper.cs:576-584` 汇总全部组分。
3. **原生倒水入口。** `WaterHelper.cs:267-316` 的 `TransferLiquid(sourceContainer,targetContainer)` 根据目标剩余容量截断，调用 `SplitProportionally` 分配整数微升，逐组分增加目标和扣减来源，同时处理温度、滤芯和水质特征。调用方向是来源在前。
4. **原生过滤会消耗/变换组分。** `WaterHelper.cs:318-395`。为了装瓶时逐组分守恒，目标排除过滤容器和存在滤芯的容器。滤水壶可作为已经装有水的来源，倒出时不触发目标滤芯过滤。
5. **水质标签可能是伪装。** `WaterFeatureHelper.cs:94-147` 根据来源鉴定状态设置真实与伪装水质；`WaterFeatureHelper.cs:277-296` 可设置伪装水质。因此自动装瓶比较真实液体组分，而不是翻译后的显示名称或假水质。
6. **真实档位。** `WaterFeatureHelper.cs:63-77,451-461` 使用纯度整数阈值 `9990/9900/9800/9600/9000`，其余为最低档。`WaterHelper.cs:557-574` 计算万分比水纯度。
7. **温度。** `ChemicalHelper.cs:1125-1143` 在原生倒水时按组分体积混合温度。非空瓶只接受与水源温差不超过 0.5°C 的液体；空瓶没有已有液体温度需要匹配。
8. **官方瓶型。** `WaterHelper.cs:24` 的 `WaterBottleIds` 列出 `mini_bottle/small_bottled_water/bottled_water/large_bottled_water/water_jug`；`FoodItemDirectory.cs:166-190,303-320` 确认容量 250/450/750/2000/6000 ml。目标必须比所选来源容量小。
9. **原生可交互判定。** `GameItem.cs:1043-1083` 的 `CanTarget` 和前面的 `MayTarget` 检查有效所有权及物品/库存的交互谓词。代码调用它们，但不调用通用 `Target`，避免执行不相关的额外模组交互；真正的水操作调用专用倒水入口。
10. **快照必须深拷贝标签。** `TagSystem.cs:14-26` 的 `Clone` 仅浅拷贝字典，内部 `TagState` 对象仍共享。恢复快照逐个调用 `TagState.Clone()`。

## 水质规则

只把水及原生自然水杂质当作自动装瓶水源：水、重金属、微生物、有机废物、矿物质、化学污染物、物理污染物、微塑料。含有酒精、药剂、营养剂等其他组分的来源拒绝自动操作。

空瓶：所有组分必须为 0；按原生比例分配，并预检装入后真实档位与水源一致。

非空瓶：真实水质档位一致、温度相近，而且**每种组分比例**一致。允许已有同类组分在原生整数分配中的 1 微升舍入误差，但来源没有的任何痕量组分仍拒绝。由此不会把“同名同档但污染物不同”的水混在一起。此规则比只匹配界面水质档位更保守。

排序：先补满兼容的非空瓶，再用空瓶；优先剩余容量小的瓶，以便尽可能多地获得满瓶。

## 执行和失败边界

每一瓶先重验锁定/原生交互条件，预测该次分配，然后保存来源及当前瓶的深标签快照、物品类型/特征列表、价格字段及水质特征状态。只调用原生倒水，没有生成、复制、销毁物品，也不使用 `AddWater/AddLiquid` 伪造水。

每次倒水后读取双方全部组分，确认每一组分的总量守恒、目标不超容量，实际转移量等于原生容量预测。异常时恢复当前一对物品并停止；此前已经成功装好的瓶子保留。恢复失败会明确提示不要保存并重新载入。

静态发现一个原生边界：来源刚好倒空时，`OnLiquidContainerPourInto` 在扣水后读取来源纯度，已鉴定来源可能被读成 0%（`WaterFeatureHelper.cs:110-118`，调用位置在 `WaterHelper.cs:314-315`）。对“空目标、来源原先已鉴定、这次刚好倒空”使用 `InitWaterFeature(target,true)` 重新同步目标已知水质。这只继承来源已有知识，不鉴定未知来源，也不修改任何组分。

未对游戏原版倒水的其他行为做全局补丁。未知/第三方容器与其他模组同时修改液体的情况仍需游戏内兼容性验证。

## 已执行验证

```powershell
dotnet run --project tests/LiquidPolicy.Tests.csproj -c Release --property:BaseIntermediateOutputPath=obj/LiquidPolicyTests/
dotnet build tests/LiquidPolicy.NativeCheck.csproj -c Release --property:BaseIntermediateOutputPath=obj/LiquidPolicyNative/
```

结果：液体策略 **6,027 项检查通过**，包含空瓶、同液不同体积、相同纯度但不同污染物、痕量新组分、档位阈值、温差、舍入误差、无效/溢出数据、组分守恒与 2,000 组随机体积缩放/分配。

真实游戏程序集引用下的 `netstandard2.1` 编译通过，**0 警告、0 错误**。`LiquidNativeStubs.cs` 只用于单独编译时声明父模块契约，不属于生产模组。

后续原地访问修复增加 `dotnet run --project tests/BottlingAccess.NativeTests.csproj -c Release`：0.2.2 扩展至 **101 项检查通过**，执行实际游戏程序集和生产目录/访问/锁定代码，涵盖单层原生 intake 及两种机器舱中的打印机；不执行 Unity 场景或液体动作。原液体策略 6,027 项检查仍通过。

## 尚需游戏内验收

在可回滚的测试存档中放置一个水箱、数个空瓶、同水质未满瓶、不同污染物/水质瓶和锁定页中的空瓶，运行一次并保存前后截图。核对来源扣水、瓶子增水、不同水质/锁定瓶保持原状、物品计数不变。另测不足一瓶水、来源恰好倒空、已鉴定/未知来源、水壶带滤芯、跨页箱子、店外和拖动中按钮。

目前没有宣称完成游戏内运行测试。
