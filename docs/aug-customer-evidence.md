# 改造体客户姓名提示

## 原生依据

目标程序集 SHA-256：`725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。本地 REA 静态身份和定点 ILSpy 输出用于核对真实接口；不随源码或成品发布游戏程序集、反编译代码和私人存档。

`StoreClient.isAug` 是客户的真实改造体身份；`isAugScanned` 记录是否扫描过，不等于真实身份。姓名提示只依据前者，不用精灵、姓名、通缉或派系猜测。尚未扫描或伪装的客户也会提示。

`NegociationUIManager.RefreshClientHeader` 的实际 `NegotiationTabletFrame.clientName` 与 `DialogUIManager.clientNameTMP` 是对应姓名控件。Harmony 后置补丁在原生姓名刷新之后追加“【改造体】”；现有低频刷新覆盖已经打开的界面。仅在文本确实匹配当前客户姓名时处理，不改其他说话者或别的模组自定义文本。

## 状态边界

仅修改自己的显示后缀，不修改 `displayName`、`isAug`、`isAugScanned`、客户蓝图、扫描仪、举报或任务。记录原文本和已应用文本，重复刷新不重复追加；停用只还原仍由本模组拥有的文本。普通客户切换时移除旧后缀。

`PlayerStore.Instance` 可能自动创建实例，因此低频刷新必须先检查 `PlayerStore.HasInstance` 与店铺库存已就绪。这个边界由主菜单启动实测发现并修复。

## 验证

`dotnet run --project tests/CustomerHints.NativeTests.csproj -c Release`：274 项检查通过，覆盖所有原生来访意图、真实身份与扫描状态组合、空客户、不重复后缀及数据不变。

2026-11-13 在独立存档副本中打开原生对话与交易面板：改造体客户两个姓名处均有标记，切换普通客户后无残留。受控客户用于验证界面，不表示完整自然事件已覆盖。详见 `runtime-validation-v0.3.0.md`。
