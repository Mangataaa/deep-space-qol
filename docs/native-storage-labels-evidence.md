# 原生标签打印机与操作反馈（0.2.0）

目标仍为本机 Assembly-CSharp.dll，SHA-256 `725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`，沿用已有 REA 静态证据及本地反编译；不发行游戏源代码或 DLL。

## 已核对的原生接口

- `LabelerBlueUIManager` 是截图中的蓝色标签打印机。`OpenUI(GameItem)` 编辑物品名称；`OpenUI(string, Action<string>, Action, int)` 被页签改名调用。原生输入、清空、提交、关闭和打字状态继续使用原有逻辑。
- `StorageExpansionHelper.OpenRename(GameItem, int)` 明确提供目标容器及页索引，后缀在原生窗口打开后挂接分类控件；没有根据页签文字猜测目标。
- `TypeHelper.MAJOR_TYPES_LIST / ALL_TYPES_LIST / GetTypeDisplayName` 提供实际物品类型列表和本地化文字。主分类优先显示；每个勾选项保存为 `tag:<原生类别>`，不是把显示名称当分类。
- `CustomUIManager.BuildInto` 只在模组自己的子 RectTransform 上创建控件；`UseMachineryStyle` 使用官方机器界面外观与字体。没有清空或重建原生打印机。
- `StorageExpansionHelper.SetTabName` 后缀同步旧版显示名称并刷新分类/锁标记，避免原生改名或清空后丢锁标记、恢复旧名称。
- `CustomUIManager.GetLayerRoot(overlay)` 可容纳只属于本模组的临时提示。`CanvasGroup.blocksRaycasts=false` 使操作提示不阻挡游戏点击，约八秒消失。

## 实现与保存行为

`NativeStorageLabels` 在原名称框下追加可折叠的滚动多选列表，以及手动分类约束、当前页锁、整箱锁；勾选立即通过原有 `StorageRules.Save` 保存到物品状态，随后正常存档即可。物品名称和类型条件彼此独立。储物窗口增加“标签”按钮，普通箱子和背包也可打开同一打印机设置规则。

控件先测量原生输入框与窗口边框，再向下扩展需要的高度，保持原名称框、标题、关闭和清空按钮的尺寸及位置；低分辨率减少滚动高度，并把窗口保持在画布范围。折叠、关闭、换目标、切存档和停用时恢复原生几何参数，销毁的仅是本模组节点。若不能确认边框，保留原生改名并提示失败，不修改未知节点。

旧中文别名由 `CategoryMatcher.ExplicitTokens` 展开为语义等价的原始标签集合。现存精确 ID、自定义标签和通配条件作为可见的“保留原规则”选项继续保留；打开界面本身不写入规则。无勾选类别表示通用收纳；“全部类别”表示显式通配。二者均不意味着排除全部商品。

本机储物目录仅识别已审核原生容器；未知第三方容器不自动纳入。分类菜单可显示原生所有类别，但将类别设为机器、储物等不会使机器/箱体变成可自动搬运的商品。

## 验证边界

- 真正游戏程序集引用下生产编译通过；原生清单解析和 ModScanner 用于检查可加载元数据及能力声明。
- 原生审计额外逐一核对 Harmony 重载签名、命名参数、按索引参数和私有字段注入，不在离线工具中应用补丁或启动 Unity。
- StoragePolicy 的 79 项检查包含旧别名展开、ID/自定义条件保留、逐类别语义等价，以及取消一个勾选后其他分类仍匹配。
- StorageCatalog 的 304 项真实程序集检查包含机器区排除、原生锁、普通水瓶与未安装模组仍可整理收纳。
- 尚未进入游戏验收本版真实像素布局、滚动、物理射线、打印机变高/还原、点击和存档往返。编译和静态分析不是界面运行证据。
