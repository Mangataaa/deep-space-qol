# 一键鉴定填价：原生路径与验证

## 行为

在带有原生价格框的鉴定委托中增加“鉴定”按钮，填入该物品按游戏真实鉴定规则计算的价格。按钮不提交、不关闭鉴定页，不修改物品的真伪、品质、基础售价、客户原先报价、鉴定等级或奖励。

只更新原生可编辑提案 `APPRAISAL_DEFAULT_VALUE`，再刷新 `apprisalServiceInputField`；提交仍由玩家点击原生按钮。原生价格区每帧从这一提案标签刷新，所以不能只改输入框，否则下一帧会恢复旧值。

## 本机静态证据

证据来自已安装游戏 `Probably Stolen_Data/Managed/Assembly-CSharp.dll` 的本地 ILSpy 解码，原游戏程序和反编译文件不随模组发布。程序集身份沿用实施记录中的 SHA-256 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`；这是静态调用核对，不代表游戏内点击已通过。

- `InspectionUIManager.OnInspectItem` 为各物品打开专用检查画面，并在 `APPRAISAL_ITEM` 时显示共享的 `appraisalPanel`。所有现有价格鉴定页面使用同一个 `apprisalServiceInputField`。
- `InspectionUIManager.UpdateAppraisalPanel` 要求当前物品是鉴定品、当前客户存在且意图为 `APPRAISAL_SERVICE`；未编辑时从 `APPRAISAL_DEFAULT_VALUE` 刷新输入框。
- `InspectionUIManager.FinishEditingAppraisalField`、`OnValueChanged`、`ChangeAppraisalServicePrice` 都只修改 `APPRAISAL_DEFAULT_VALUE`，这是原生的用户输入状态，不是物品真实价值。
- `InspectionUIManager.OnAppraisalServiceEstimated` 才调用 `AppraisalServiceHelper.OnEstimate` 并关闭检查页。本功能不调用这两个提交方法。
- `AppraisalServiceHelper.OnEstimate` 和接受结果都读取 `AppraisalHelper.GetCorrectBaseValue`。本功能使用相同方法，不用 `unitValue`、商店报价或复制的价格公式代替。
- `AppraisalItemRegistry` 内置有 `hand`、`ribwich`、`plasma_fuel` 三类。`Find` 支持 Mod API 注册的其他鉴定品，因此按钮无需按这三个 ID 写死。
- 本机其他专用检查画面包括香烟、邮票、注射器和荧光莓；它们本身不带原生价格提交。没有价格框时不额外伪造输入框或提交路径。

## 集成与生命周期

`NativeAppraisal.Refresh()` 加入常规 UI 刷新；`NativeAppraisal.Dispose()` 在停用模组、重新载入时销毁自己的控件。无需增加 Harmony 补丁。`NativeAppraisal.FillPrice()` 是只填价的操作入口。

- 在当前真实鉴定价格区外侧增加一个原生风格按钮，不克隆含原生提交回调的控件，不调整原生价格框、加减或提交按钮。
- 当前经理、输入框或场景变化时释放旧绑定；关闭检查页、切换非鉴定物品或非鉴定委托时隐藏；重复打开不累计按钮和监听器。
- 点击时重新检查当前物品、当前客户和输入状态，不使用创建按钮时缓存的物品。
- 未注册类型、缺少计价器、非正数无效价格保留原输入；异常由现有操作入口记录并提示。
- 按钮名为 `DeepSpaceQoL appraisal fill-only toolbar`，便于游戏内查找和截图检查。主方向在价格区右外侧，窄屏越界时改左外侧。

## 离线验证

执行：

```powershell
dotnet run --project tests/Appraisal.NativeTests.csproj -c Release -- 'D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed'
```

使用真实游戏程序集的数据对象和原生计价方法，58 项检查通过：

- 基础、豪华、战术、破损低价手臂。
- 普通、实验室高品质、改装导致套装无效、严重损坏低价燃料。
- 普通、双肉豪华、打开、伪造日期肉夹包。
- 第三方注册鉴定类型、空物品、缺少鉴定标记、未注册类型、零/负价格。
- 计价过程中不改基础售价、原提案、客户原报价及原物品状态。

此离线测试不初始化 Unity 场景、点击 UI、提交鉴定或发放奖励。2026-11-13 另在独立存档副本进行实际鼠标验证：手臂、肉夹包、燃料分别填入当前原生预期 150、150、420，填价跨帧保持且不自动提交；另点击一次原生提交，正常接受并关闭检查页。范围详见 `runtime-validation-v0.3.0.md`，不代表所有自然委托、缩放或第三方鉴定类型均已实测。
