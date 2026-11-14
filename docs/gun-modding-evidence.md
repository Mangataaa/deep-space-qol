# 委托枪械一键改装：原生证据、边界与验证

核对日期：2026-11-14。目标是已安装的《深空当铺 / Probably Stolen》：

`D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll`

本次重新核对 SHA-256：`725CB999D019DFF1FF31BED353C7F9BB8D81A7605A15DE1F80F5A3A6CF7B1D05`。

静态调用证据复用本项目已缓存的本地 ILSpy 解码 `reference/GameCode`，不把反编译文件或游戏程序集随模组分发。下文将静态核对、实际原生数据方法测试与尚未覆盖的 Unity 运行行为分开说明。

## 已实现行为

- 原生枪械委托面板交付按钮左侧增加“一键改装”，无需通过 F6 进入。
- `GunOrderData.ResolveOrderGun()` 确认当前真实委托使用的枪，不遍历仓库寻找另一把枪，也不会凭一个 `SERVICE_GUN_TAG` 去替换顾客登记的服务枪。
- 服务枪和玩家放在交易区的普通定制枪均可参与。非当前委托、固定、保护或上锁的目标枪拒绝改动。
- 只扫描 `StorageCatalog.GetSources()` 的玩家实际散装配件；配件必须属于玩家、未固定、未受整箱／页锁／原生锁／剧情保护、整件可移除且数量为 1。不拆其他枪、机器或功能槽，不拆分叠放物品，不生成配件。
- 搜索只读取快照，先满足全部必需条件，再优先较高附加奖励、较多附加条件；同等结果优先较低新增配件成本、较少新增件数和较少改动。已满足且无改进的原搭配保留。
- 原配件可安全拆换时，先在真实未锁库存页逐件预留合法返还空位。预检空间不足则整轮不执行，暂不依赖稍后装上枪的新配件将释放的格子。
- 通过新鲜的原生 `TryInventorySlot` 和 `TryAcceptOnce(1)` 转移真实原物件。所有拆装在一次主线程提交内完成；最后由原生统计和真实委托检查复核，玩家自行交付。
- 扫描与搜索在主线程按约 3 ms 分片，无线程或 `Task.Run`。搜索上限为 100 万步或约 1200 ms 求解 CPU 时间；帧间等待不消耗求解时间。有限搜索若有完整方案可采用并提示，若没找到完整方案则不移动配件，不把有限搜索称作已证明无解。
- 换档、换场景、换顾客／委托枪、拖拽／多选／固定模式、其他收纳、Esc、相关来源及锁定变化会停止未提交的工作。提交前再次检查订单、枪、原配件、选中配件、位置、状态和规则。

## 原生依据

| 原生位置 | 已核对事实与实现选择 |
| --- | --- |
| `GunOrderRequirement.IsMet` | 普通门槛是严格 `>`／`<`，不是 `>=`／`<=`。口径要求 ADV 枪且数值相等。策略遵守严格边界。 |
| `GunOrderData.ResolveOrderGun`、`GetPlacedServiceGun` | 服务委托只认该订单私有登记的 `serviceGun`，且必须位于前／后交易区；普通委托认原生选中的台上枪。直接复用，不自行假定枪的归属。 |
| `GunOrderData.CreateServiceGun`、`StoreClientListGun` 服务枪放置路径 | 从原生枪目录创建，设枪况并添加 `SERVICE_GUN_TAG`；放置时设为非玩家所有。该路径不添加 `IMPORTANT_TAG`。不会为服务枪打开任意剧情保护例外。 |
| `GunModHelper.CreateGunModWindow`、`Enable*Slot` | 原生有枪托、导轨、弹匣、下导轨、枪口及三个特殊槽；按审计过的 role 和结构读取真实槽，不把任意子容器当作改装槽。 |
| `GunModHelper.MayModGun` | 玩家自有枪可改；非自有枪仅在原生服务枪情境可改。实际入口另要求真实当前委托。 |
| `GeneralHelper.MayOwnershipAcceptItem`、`IsInsideNonOwnedContainer` | 玩家配件可以装到非自有服务枪；玩家自己装入的配件允许取回，顾客原有非自有配件不允许取走。原件不可移除时冻结保留。 |
| `GunModHelper.HasConflictingMod`、`GetExclusiveGroup` | 检查跨槽互斥组；特殊槽还禁止相同 identifier 的配件重复。搜索中保持真实 token 唯一，正式安装还完整重跑原生槽规则。 |
| `GunModHelper.AllowOnlyWeapon`、配件自身 `mayInventoryAddItemFunc` | 槽位标签相同并不等于兼容；配件自身可能限定接收器。搜索前执行原生物品谓词，正式装入时再次执行完整原生槽谓词。 |
| `GunHelper.UpdateCurrentGunStats` | 六项属性 = 基础值 ×（100 + 枪况修正 + 各件百分比总和）/ 100 + 各件固定加值总和；整数截断、不夹紧。枪况 New／Excellent／Good／Fair／Poor 分别 +30／+15／0／-15／-30。真实提交后由本方法重新计算，不直接写入预测达标值。 |
| 同方法及 `GetModifiedItemSize` | 临时消音器为 1、正规消音器为 2，取最高等级；枪体体积数合并后值为 1 的格子。体积由原生父级形状修饰器预测，再检查交易区实际空位。 |
| `GameItem.ComputeModifiedShapeState`、`GridShapeBuilder.MergeWithLocalShapes` | 按原生枪体原点和方向合并配件形状；重复覆盖不重复计格，支持负坐标枪托。 |
| `TagSystem.Clone`、`TagState.Clone` | 前者只复制字典、共享可变 TagState；因此回滚快照逐项调用后者，防止后续原生回调污染旧状态。 |
| `NegociationUIManager.BuildGunOrder`、`TabletUI.BigButton` | 原生交付按钮在 buttonHost 的 x=153、宽 256；新增同皮肤按钮使用左侧 x=8、宽 132。控件置于面板下而非会被刷新清空的 buttonHost 下，位置跟随该 host。 |

## 恢复策略

提交只搬运真实原件，不生成、销毁、合并、复制物品或发放奖励。记录所有将移动配件的真实对象、原库存、原形状、状态深拷贝及特征列表。

`WholeItemTransferTransaction` 在应用失败时先脱离全部相关物品，再按来源库存先于枪内槽的顺序恢复，从而避免互换槽和互斥配件相互阻塞。恢复后核对配件成员、原库存、几何、状态、枪的原有槽成员和原生枪属性。恢复校验若也失败会明确提示不要保存、立即重新载入，不声称已恢复成功。

正式移动使用完整原生许可；`UncheckedAccept` 仅用于同帧失败后恢复已在提交前确认的原库存和原位置，不用于安装目标方案或绕过当前规则取用配件。

## 离线验证：3180 项检查通过

执行：

```powershell
dotnet run --project tests/GunModding.NativeTests.csproj -c Release
dotnet build src/DeepSpaceQoL/DeepSpaceQoL.csproj -c Release --nologo
```

主项目零警告、零错误；新文件 `git diff --check` 通过。

实际执行已安装游戏程序集的 `GunOrderRequirement`、`GunHelper.UpdateCurrentGunStats`、`GridShapeBuilder`、`GunModHelper`、`GeneralHelper`、`GraphUtils`、`TagSystem` 等数据方法，并直接反射调用生产服务的保护判断及会话解析。夹具仅替代图形 `GameItem.Validate`，不初始化 Unity 场景。

- 六项属性、五档原生枪况及未知枪况、负百分比／固定减值、整数截断，共 240 组原生统计对照。
- 严格大小、口径、临时／正规消音器门槛；负坐标、重叠、旋转和翻转后的枪械体积合并。
- 必需／附加条件、奖励优先、相同结果的成本和改动优先、独立 token、特殊件同 ID、互斥组、原生空间否决、有限搜索标记。
- 160 个随机问题与独立穷举评分比较，Step(1) 与 Step(1000) 切片结果一致。
- 直接调用生产 `CanModifyGun`：非自有原生服务枪和自有普通委托枪允许；非自有普通枪、固定、IMPORTANT、Lou、显式锁等被拒绝。
- 直接调用生产 `IsLooseCandidate`：实际根库存／普通箱内自有散装件允许；原生来源锁、整箱锁、固定、剧情、非自有、机器格、其他枪功能槽及叠放件被拒绝。
- 使用真实八槽及角色名数据图直接执行生产 `TrySession`，确认识别当前登记服务枪；另一把仅有服务标签的枪不会替代它。
- 实际原生槽谓词检查：槽类型、接收器白名单、零件所有权、特殊件互斥及替换目标槽例外；玩家装入服务枪的件可拆、顾客非自有原件不可拆。
- 生产协调器故障注入覆盖操作前、旧件移动后、新件移动后、最终条件否决；验证先脱离全部再恢复和失败状态准确报告。**这验证协调器与适配器合同，不是实际 Unity 转移回调／渲染的实测。**
- 生产状态深拷贝验证，确保恢复快照不共享可变 TagState。

## 明确边界与待实测

- 未启动游戏、未修改玩家存档、未自动交付。按钮显示、面板缩放、实际交易区形状修饰、完整移装及音效／UI 回调仍待用户实机复测。
- 不消耗枪械零件修复枪况。要求只能靠修枪才达成时会提示手动维修；不会暗中消耗维修材料。
- 对第三方未知订单条件、未知槽结构、非空父级状态修饰器或非普通 0/1 配件扩展形状采取保守拒绝，不猜测第三方属性；第三方额外原生槽限制在最终原生搬运阶段仍可否决并触发恢复。
- “最好的同口径枪”订单原生奖励按市场价而非附加条件计算；本功能只满足当前需求和可枚举附加条件，不为抬高市场价盲目堆配件。
- 这是有预算的组合搜索，不保证全局最优；有限搜索找到的每个可提交方案都必须完整满足全部必需条件。
