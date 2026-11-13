# 搜查风险高亮：实现与证据

## 范围

`NativeSecurityHighlights` 在当前来访者的原生意图为 `StoreClient.ClientIntent.INSPECTION` 且玩家在店内时启用。普通治安部买家、卖家不会仅因其阵营而触发。来访者离开、进入外出/战斗、读档重新初始化或停用模组后，销毁本模组创建的图形并移除提示钩子。

显示方式：违禁属性用红色细边框，赃物用橙色细边框；同时具备时优先红色。左上角增加 `!`，与荧光莓右上角品质角标错开。打开的库存和容器页里的物品都会参与；若容器内部含风险物品，容器本身也显示边框，悬停提示区分本体和内部风险。

图形是挂在物品 `middle` 下的独立节点，自动跟随物品位置、旋转后的包围盒和原生窗口裁剪，不替换原精灵、不调用原生标记功能、不写入任何标签或保存数据。所有 Image、文本和 CanvasGroup 均禁止拦截射线。被原生隐藏的物品图像不会额外显示高亮；本功能不会打开容器、暴露隐藏页或解锁。

## 原生逻辑核对

对象：本机 `Probably Stolen_Data/Managed/Assembly-CSharp.dll`；SHA-256 为 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。复用此前相同程序集的本地 REA 静态身份记录 `game-assembly-evidence.json` / `artifacts/0.2.3-audit/native-managed-evidence.json`，以下为本地 ILSpy 输出的定点静态核对，不是运行场景证据：

- `StoreClientList.CreateInspectionClient`（本地 `reference/GameCode/StoreClientList.cs`）：设定 `clientIntent = INSPECTION`；`CreateTrailerInspectionClient` 复用该函数。
- `StoreClient.ClientIntent`（`StoreClient.cs:16` 起）：BUY、SELL、SELLNBUY 与 INSPECTION 是独立意图。`StoreClientInstance.GetClientBlueprint` 返回当前原生来访者数据。
- `ContrabandHelper.IsFlaggableItem`（`ContrabandHelper.cs:390` 起）：启用违禁或赃物标签才有风险；当违禁等级字符串为 `CONTRABAND_LEVEL_NONE` 且不是赃物时排除。
- `ContrabandHelper.IsContraband`（`ContrabandHelper.cs:882` 起）和 `StolenHelper.IsStolenItem`（`StolenHelper.cs:26` 起）：只检查各自的当前标签。
- `ContrabandHelper.InspectItem`（`ContrabandHelper.cs:510` 起）：还会检查隐藏/锁，且产生原生日志；实际搜查另外对赃物调用概率检测。本模组从不为绘制提示调用这些流程，也不改变搜查结果、热度或随机数。
- `GameItem.GetTagReadonly`（`GameItem.cs:690` 起）内部调用 `TagSystem.GetTag`；后者会插入缺失标签。本功能因此直接读取 `modifiedState.dict.TryGetValue` 来核对等级，语义匹配原生的可标记条件而不插入缺失标签。
- `GameItemElement.middle`、`background`、`modifiedShape` 和 `TreeNodeRender.element` 提供当前物品的可见渲染锚点。扫描只读取激活的 `windowRoot` 子节点，容器风险遍历使用带访问集合的队列，避免有问题的循环图阻塞 UI。
- `ModHook.OnCreateTooltipLate` 是现有原生公开钩子；`Init` 防止重复注册，`Dispose` 成对注销。没有新增 Harmony 补丁。

提示表示“物品具有风险属性”，不等于“该物品必被没收”。原生隐藏、原生锁和原生随机检测照常生效；为了让玩家找到需要注意的物品，藏在容器内的风险也会在对应外层容器悬停提示中列出。

## 验证

命令：`dotnet run --project tests/SecurityHighlights.NativeTests.csproj -c Release`。

已通过 626 项检查，直接使用本机真实 `StoreClient`、`GameItemElement`、`TagSystem`、`GameGridInventory`、`PixelWindow` 数据类型及原生只读谓词，未运行 Unity 场景或搜查动作。检查包括：

- 全部原生来访意图都只有 INSPECTION 触发，空来访者关闭提示；普通治安部买卖意图不误触发。
- 32 组违禁、赃物、隐藏、上锁和锁后物品组合，乘六种等级，与真实 `IsFlaggableItem` 逐项对比。
- 普通商品、缺失等级、残留 NONE 等级，以及赃物同时带 NONE 等级的边界。
- 绘制分类不新增标签、不改变现有值、隐藏状态、锁和赃物热度。
- 两层嵌套容器、同时含两种风险、本体风险与内部风险分开描述。
- 同一图有循环时有限结束，不搬动物品。

2026-11-13 另在独立存档副本的受控原生 INSPECTION 来客下实测：违禁品红框、赃物橙框、普通莓果不误标，外层容器悬停注明内部风险；高亮不阻挡原生右键菜单，切换普通治安部 BUY 来客后图形计数归零。自然搜查结果、所有缩放/旋转/拖拽及反复保存读档尚未全面覆盖。详见 `runtime-validation-v0.3.0.md`。

## 集成接口

- 模组主线程启用和读档重新初始化后调用 `NativeSecurityHighlights.Init()`。
- 与现有原生界面刷新一起约每 0.3 秒调用 `NativeSecurityHighlights.Refresh()`。
- 停用与读档前调用 `NativeSecurityHighlights.Dispose()`；读档后重新 `Init()`。

新增文件：`src/DeepSpaceQoL/NativeSecurityHighlights.cs`、`tests/SecurityHighlights.NativeTests.cs`、`tests/SecurityHighlights.NativeTests.csproj`、本文档。原生测试已加入构建脚本。
