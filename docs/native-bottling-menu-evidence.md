# 水源右键菜单：一键装瓶

## 0.2.1 修正

菜单的 `TryReady` 现在在确认玩家操作状态后直接调用 `BottlingService.CanUseSource`，与真正执行使用同一水源权限和液体检查。移除了菜单中重复的 `StorageRules.CanAutomate` 检查，避免固定水源因“不能移动”被禁用。`PINNED_TAG` 不被移除，原地分装仍遵守自有、祖先锁、原生液体交互和液体兼容规则。

独立装瓶目录增加交易称重区自有瓶与真实 `bottle_printer` 的成品输出网格。0.2.2 修正单层原生 intake 包装，并定位两种原生机器舱中的打印机；未更改收纳/上货普通目录。菜单提示同步说明范围；F6 备用水源列表也使用装瓶目录。细节见 `bottling-access-evidence.md`。

## 交付范围与集成

实现文件：`src/DeepSpaceQoL/NativeBottlingMenu.cs`。

- `NativeBottlingMenu.Init()`：在 `ModEntry.OnEnable()` 设置 `Enabled = true` 后调用。
- `NativeBottlingMenu.Dispose()`：在 `ModEntry.OnDisable()` 调用；先后设置 `Enabled = false` 均可。关闭已挂接的菜单、还原原生菜单结构并销毁本模块创建的节点。
- `OnGameLoadedLate` 可以调用 `Dispose(); Init();` 清理跨存档菜单对象；加载器重新初始化 `ItemContextHandler` 时补丁也会清理对应旧状态。
- Harmony 使用既有加载器的程序集 `PatchAll`。无需另建 Harmony 实例、反射字段访问器、额外 manifest 能力或 F6 快捷键。
- 唯一动作入口为 `ModEntry.Execute(() => BottlingService.Run(source))`，其中 `source` 从点击瞬间的原生 `currentItem` 字段捕获。液体分装仍由现有 `BottlingService` 完成。
- 未更改 `ManualInputScope`。原生手动“卸下”作用域的补丁只识别 `buttonUnload`，不会把本模块按钮当作卸下。

## 静态证据

目标：本机 `Assembly-CSharp.dll`，SHA-256 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。沿用工作区 REA 证据记录 `game-assembly-evidence.json` 的 `ev_e9976a693be02a1c8c8375dd04c8672c58cc0c127d008d201a3e83f1d3f3abe8`，以及从同一程序集反编译的 `reference/GameCode`。反编译文件只用于本地接口核对，不随模组发行。

| 原生位置 | 已确认行为 | 实现对应 |
| --- | --- | --- |
| `ItemContextHandler.OnEventInit` | 物品菜单使用 `GridPixelElement(1,8)`；按钮是原生 `RichTextElement`；整棵菜单树标记 `ContextMenuNode`。 | 新行也使用 `RichTextElement`，复用原生字体、像素内边距与渲染；新节点全部标记同一菜单标签。 |
| `GridPixelElement` | 宽高维度只读；支持 `AttachPos/Detach` 和原生父级维护。 | 不改维度、不复制八个原按钮；临时用外层 1×2 网格把现有菜单内容作为第一行、装瓶按钮作为第二行。 |
| `GridPixelElement.GetRowPad` | 只检查当前行可见性，即使末行隐藏仍可能给前行增加间距。 | 非水源或菜单关闭时拆掉整个外层网格并还原原内容，避免普通菜单留下空行/额外间距。 |
| `PixelWindow.Attach/Detach` | 原生维护 `childElement`、图存储父级、渲染父级、窗口自动尺寸。 | 只用这些原生方法临时挂接/恢复；保留 `contextButtons` 字段引用和所有原控件身份。 |
| `ItemContextHandler.OnEventPress` | 赋值 `currentItem` 后依次运行 `UpdateConditions`、`UpdateSelectButtonColor`、`EqualizeButtonWidths`、网格和窗口 `Validate`，再显示。 | 在 `UpdateConditions` 后缀加入一行；原生尺寸流程会覆盖扩展后的内容。 |
| `ItemContextHandler.UpdateSelectButtonColor` | 普通/禁用/选中/选中禁用使用四个私有调色板字段。 | Harmony 注入这四个现有字段；新增按钮遵守同样四种颜色。 |
| `ItemContextHandler.EqualizeButtonWidths` | 活跃原按钮宽度统一；自然高度由原生测量。 | 在原生宽度流程后把新增行纳入最大宽度，保留原生自然高度。 |
| `ItemContextHandler.FindRichTextNode` | 射线命中的 `TreeNodeRender.parent.element` 必须是真正的 `RichTextElement`。 | 使用原生构造方法，不克隆 Unity 按钮或拦截任意菜单点击。 |
| `ItemContextHandler.TriggerButton/Stop/OnEventReset` | 原生动作先捕获当前物品、重置菜单，再执行；公开 `Stop()` 调用完整重置。 | 前缀只拦截本按钮；点击重新校验当前物品，调用公开 `Stop()` 后执行。其余按钮返回原流程。 |
| `CustomUITooltipTrigger` | 官方 UI 组件支持进入、退出和禁用时显示/隐藏提示。 | 为新增按钮的射线图元附加该组件；提供操作说明和当前禁用原因。 |

所有私有字段读取都使用既有 Harmony 参数注入（`___currentItem` 等），没有新增 `AccessTools.FieldRef` 或直接反射调用。

## 可操作条件

只在 `BottlingService.IsSource` 识别的单件储水箱、水桶、滤水壶菜单显示。非水源菜单不显示行。

菜单打开时检查：模组启用、已载入店内存档、不在战斗/拖动/多选/固定编辑模式；水源自有且可操作，符合祖先页签/箱锁；位于已审核的装瓶目录。物品本身已固定不影响原地装瓶，固定编辑模式下仍需先结束编辑。液体组分必须有效、含水、未超容量，而且不含药剂、酒精等其他液体。

同水质瓶子、空瓶、温度和容量等目标级条件仍由装瓶服务在执行时逐瓶校验。菜单不会为了绘制每一帧枚举全部瓶子。按钮禁用原因在打开时确定；点击时再次读取原生当前水源并完整重验，避免菜单开启后物品/锁状态变化。没有按打开菜单时建立的物品动作闭包。

每个处理器保留最多一个菜单状态和一个装瓶按钮。每次原生重置都恢复原窗口内容；处理器重新初始化和模组停用时清理。若其他模组在菜单开启后强行替换本模块的父级，清理会隐藏新增按钮并保留仍承载原生控件的节点，避免销毁其他模组/原生内容。此冲突未宣称自动解决。

## 已执行验证与限制

```powershell
dotnet build src/DeepSpaceQoL/DeepSpaceQoL.csproj -c Release --no-restore
```

真实游戏程序集引用下编译成功：0 警告、0 错误。

没有运行或修改当前已启动的游戏，没有向游戏目录写入文件，没有声称完成游戏内交互验收。静态编译无法确认像素布局、射线与原生工具提示在真实 Unity 画面的最终效果。

建议游戏内检查：

1. 右键满水储水箱：出现“一键装瓶”，悬停颜色、宽度、字体、边框与其他原生项一致；点击分装当前水箱，并正常关闭菜单。
2. 连续右键两个不同水源，第二次必须只消耗第二个；每个菜单仅有一个装瓶项。
3. 非水源、关闭重开菜单、排序菜单：原生行数、间距、交互不变；普通按钮和排序菜单仍工作。
4. 空水箱、锁定水源、未拥有水源和药剂混合水源：禁用颜色与原因提示正确，点击不改变液体。固定水源应可装瓶且不移动、不解除固定。
5. 不同品质/污染物瓶、同品质未满瓶、锁定页内空瓶：符合装瓶服务规则。交易台自有瓶和水瓶打印机成品可装，原生交易锁、顾客/已售瓶和机器输入槽不被修改。
6. 反复启用/停用模组、切换存档、菜单开着停用：不重复按钮，不残留可点击入口，不破坏原生菜单内容。

右键执行结果沿用 `ModEntry.Execute/ControlPanel.Status`。0.2.0 的父模块已集成 `NativeFeedback`，面板关闭时也会显示短暂、不阻挡点击的屏幕提示，并保留原日志记录。
