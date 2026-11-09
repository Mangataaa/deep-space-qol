# 一键上货：本机接口与边界

以下均来自本机 `Assembly-CSharp.dll` 的静态反编译，运行行为尚需游戏内验收。

- 出售台为 `EmporiumEntry.backInvinvElement`：`NegociationUIManager.GetSellableBackInvItems`、`GetMultiSellValue`、`IsClientHaveEnoughToBuy` 只读取后柜台的直属自有物品。前柜台不计入批量出售报价。
- `StoreClient.GetBudget()` 是实时剩余预算，实际出售时 `PlayerStore.SellItem` 才扣除。上货从预算再减当前台上满足购买条件的报价；模组不持久化已上货金额，因此反复按键不会重复扣预算，出售后读取新预算。
- 需求使用 `IsClientBuyingThisItem`，保留黑名单、违禁品、水品质、化学纯度、品种需求和容器内容物判定。待揭露特征由 `CanClientExposeAnyFeatureDeep` 排除，让用户先鉴定。
- 只处理 `BUY`/`SELLNBUY` 且有明确预算、已经到店、未完成且交易未锁定的买家。剧情、采购单、交换、整批卖给玩家等专门交易界面不适用。
- 价格预览使用与 `OnAddedToWeightedArea` 同样的官方特征工厂，暂时替换特征列表后用 `GetCurrentValue` 求价，`finally` 恢复原列表，不运行物品动作或买家委托。标准品种、游客、食品银行的额外特征已覆盖；未知回调的临时报价在实际放入后核对，超预算则恢复原容器/位置/特征列表。
- 不移动装有物品的容器本体，防止连同锁定内容物出售。储物柜也不作为待售商品。容器内的货物可通过共享 `StorageCatalog.GetSources()` 获取，受 `StorageRules.CanAutomate` 保护。
- 使用完整原物品实例，不调用 `GetSplitItem`。本机原生拆堆通过 `DirectoryMaster.Item(identifier)` 重新生成物品，不适合在保持品质、鉴定与其他特征的自动化中使用。
- 每个候选朝向、翻转和坐标通过 `counter.TryInventorySlot` 检查；纯托管有界 beam search 联合比较预算与实际形状占格，以台上总报价最大为目标，再偏向少占空间。候选都保留可跳过分支，每个状态探索最多 8 个放置选择，保留 96 个状态，默认 700 ms 搜索预算。几何预检优先检查高价物品，最多 256 个候选或 1800 ms；到达限制时界面明确显示使用限时搜索方案。属于近似求解，不保证任意不规则背包的全局最优。
- 提交前重新校验锁、归属、买家需求、原父容器、实时预算和目标位置。提交使用 `SlotMarker.TryAcceptOnce`，不删除、不生成、不出售、不完成交易；提交后再次核对原生可售总价。
- 现有柜台物品保持不动。空间不足时用户可手动出售，之后再次点击上货。

## 0.1.1 买家兴趣、多样性和治安部修正

对本机真实程序集再次核对并新增 `BuyerStockPolicy`，统一候选筛选、台上预算计价、放入前与放入后复核，底层仍调用 `StoreClient.IsClientBuyingThisItem`。

- `StoreClient.IsClientRefusingItem` 先检查不许转售、物品 ID 黑名单、类别黑名单、禁止违禁品、已购品种、化学品质等；`IsClientBuyingThisItem` 只有在未被拒绝后才检查购买标签、指定 ID、自定义偏好。因此不感兴趣和排除项不会通过正向兴趣重新加入，也不会进入模组的可售报价。
- `StoreClient.TrackItemVariety` / `IsItemInVarietyList` 以物品 `identifier` 记录当前买家已买品种。既有原生调用已经覆盖跨轮已购品种，求解器与提交前检查覆盖本轮和台上已摆的重复 ID。新增检测手动摆入的同 ID 多件：提示用户先清理，避免假定它们可重复成交或重复占用预算。
- **发现并修复初版缺口：原生“愿意购买”并不等于“治安部放台安全”。** `IsClientRefusingItem` 的 `noAcceptingContraband` 使用 `ContrabandHelper.IsContraband`，后者只查 `CONTRABAND_ITEM_TAG`，不查 `STOLEN_TAG`。治安部对赃物的处罚在 `CompleteSecClient` 安装的 `onItemAddedToWeighted` 等回调内另行触发。离线回归已验证：原生买家谓词可以对有 `STOLEN_TAG` 的物品返回 true；只在放上台后回滚不足以防止检查对话。
- 新增在调用原生移动之前检查治安部身份（`isSecurity` / `FACTION_SECURITY` / `SECURITY`）与三个危险标签（`CONTRABAND_ITEM_TAG` / `CONTRABAND` / `STOLEN_TAG`），包括容器子物品。即使热度低、隐藏或购买覆盖允许也不自动上台。已有危险自有商品在称重区时，先停止并提示手动收回，不自动隐藏物品，不绕过治安检查，也不保证阻止此前已经触发的处罚。
- 非治安部顾客保持原生购买政策，仍可以按其实际规则购买赃物/违禁品，不将安全限制全局化。

`tests/BuyerPolicy.NativeTests.csproj` 在独立离线进程中调用本机真实 `StoreClient` 谓词和生产 `BuyerStockPolicy` 源码，通过 30 项回归：正负兴趣、ID 黑名单、已购品种、台上重复品种、计价集合排除、治安赃物缺口、原生覆盖、身份识别、普通顾客行为及非自有物品。测试物品使用仅数据的原生类型实例，不调用场景构造、UI、物品动作或 Harmony 补丁。这是原生规则逻辑执行测试，不是游戏内操作验收。

## 必需共享接口

`StorageCatalog.GetSources(): IReadOnlyList<GameInventory>`：本地自有储物页与允许取用的嵌套容器，排除后出售台、垃圾、远程仓库及锁定页。

`StorageRules.CanAutomate(GameItem): bool`：检查物品所有权、祖先页/箱锁定、原生固定状态及 `MayRemove()`。

## 运行验收

最终成品已通过游戏原生清单解析和 ModScanner 静态扫描，没有能力请求或拒绝项；见 IMPLEMENTATION.md。尚未在启动的 Unity 游戏中测试 UI 更新、买家回调、槽位转移、回滚和存档操作。源码级测试只能证明纯求解器预算、占格、品种与重复计价边界。

`dotnet run --project tests/StockPlanner.Tests.csproj -c Release` 已通过：精确预算组合优于贪心、空间不足优先最高可放价值、品种限制、已上货品种保留、跨 64 位占格、预算耗尽、台面已满，以及 120 组固定种子的预算/重叠/同实例/品种不变量检查。
