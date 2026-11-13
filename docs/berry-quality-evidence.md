# 荧光莓品质提示

## 原生依据与范围

本机游戏 `Assembly-CSharp.dll` SHA-256 为 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`，MVID `911508a6-e0eb-4cc0-91e8-e14b16ea28d7`。沿用此前 REA 静态记录的程序集身份；以下具体行为来自本机 ILSpy 反编译与真实数据对象离线运行，不把 REA 对其他类型的静态记录冒充为本功能的运行证据。反编译文件仅在忽略的 `reference/GameCode` 中，不随模组分发。

- `InsBerryUI.OnInit`（`InsBerryUI.cs:25–60`）读取 `BERRY_TAG`、`BERRY_CONDITION_INT`、`BERRY_VISUAL_VARIANT_INT`。条件值 -2 选霉变外观，-1 按外观变体选两个欠熟或一个过熟外观，2 选小果，其余走正常外观。
- `WineHelper.InitBerry`（`WineHelper.cs:627`）的常规生成值为 -2、-1、1、2；`PlantList.RollBloomberryCondition` 的采收结果也是这四档。品质 0 不是当前普通果实生成分支，因此缺失或未知值明确显示问号，不推测为正常。
- `WineHelper.StartFermentation`（`WineHelper.cs:463`）累加每件浆果的品质数；负数先加 1，因此这四档的单果贡献分别为 -1、0、1、2。这只是浆果单项贡献，不冒充水、数量和酵母等因素综合后的成酒品质。
- 原生 `WineHelper.CreateTooltip` 的浆果数值只在开发模式下显示；本功能通过官方 `ModHook.OnCreateTooltipLate` 增加普通模式下可读的品质文案，不启用开发模式。
- `GameItemElement.GetTooltip` 与 `GetTooltipAdvanced` 都先构造原生基础提示；`GetTooltipBasic` 在 `GameItemElement.cs:1139` 调用 `ModInternal.CreateTooltipLate`，后者调用官方提示钩子。普通库存的鼠标悬停沿用原有提示系统。

## 实现

`NativeBerryQuality.Init()` 订阅官方提示钩子；`Refresh()` 接入现有低频主线程 UI 刷新；停用、读档切换时调用 `Dispose()` 移除订阅和自建节点，随后按原生命周期重新初始化。

显示如下：

| 实际品质 | 名称 | 右上角标 | 单果酿造贡献 |
| --- | --- | --- | --- |
| -2 | 霉变 | 霉 | -1 |
| -1，外观变体 0/1 | 欠熟 | 欠 | 0 |
| -1，外观变体 2 | 过熟 | 过 | 0 |
| 1 | 正常 | 良 | +1 |
| 2 | 优质小果 | 优 | +2 |
| 缺失、禁用或其他值 | 缺失/未知档位 | ? | 不推测 |

角标只为原生可见窗口树中的 `GameItemElement` 创建，附在未旋转的 `middle` 节点右上角，适配当前物品尺寸；太小而无法容纳时隐藏角标，仍保留悬停说明。左上角预留给搜查风险提示。文字和底色节点都不参与射线检测，不覆盖原物品图片，不接管拖动、右键、多选或原生固定标记。

荧光莓专用检查页面没有价格输入框。在该页打开时，另显示点击穿透的品质提示条；切换其他物品或关闭检查界面立即在下次低频刷新隐藏。提示条由官方 CustomUI 在叠加层构建，位于顶部操作结果提示下方，不移动原生放大镜、果实或提交按钮。

所有读取直接访问已存在的 `modifiedState.dict`，与原生检查界面读取的当前有效状态一致。没有调用会初始化缺失标签的 `GameItem.GetTagReadonly`，也不修改物品名字、存档标签、酿酒分数或品质。

## 验证

执行：

```powershell
dotnet run --project tests/BerryQuality.NativeTests.csproj -c Release
```

95 项检查通过。测试链接生产 `NativeBerryQuality.cs`，使用本机实际 `Assembly-CSharp.dll` 和真实 `GameItemElement`/`TagSystem` 数据对象，覆盖四档及三个外观变体、酿造贡献、未知/极端值、缺失/禁用标签、基态与当前修改态分离，以及读取前后标签和值不变。

该离线测试不运行 Unity 画面、不应用 Harmony 补丁。2026-11-13 另在独立存档副本中实测五种对应角标、真实鼠标悬停品质，以及原生放大检查页提示条的显示与关闭隐藏；详见 `runtime-validation-v0.3.0.md`。所有缩放、反复保存读档和其他模组组合没有全面覆盖。若其他模组以 `tooltipRichTextFunc` 完全替换原生基础提示，官方钩子添加的文字也可能被它替换；本功能没有覆盖其他模组的回调。
