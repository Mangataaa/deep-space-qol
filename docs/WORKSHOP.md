# GitHub 与 Steam 创意工坊发布

## 内容边界

源码仓库只包含本项目独立源码、测试、脚本、说明与经过检查的展示图。`reference/`、`.tools/`、`artifacts/`、游戏 DLL、反编译代码、存档、日志、凭据和 `*.local.json` 不提交。

GitHub 版本附件使用 `scripts/Package.ps1` 验证的成品目录打 ZIP，保留其六个文件及校验表。测试版在 GitHub 标为 prerelease，工坊说明也明确验证范围。

## 展示图

准备真实 0.2.1 画面，隐藏聊天、通知或其他个人资料：

1. 买家需求、预算及上货箭头同屏，适合封面背景。
2. 标签打印机展开多选分类，勾选项和页／箱锁状态可见。
3. 整理前的储物页。
4. 同一页整理后，物品数量不变。
5. 固定水源的右键“一键装瓶”菜单。
6. 交易台或水瓶打印机成品区的装瓶结果。

用于工坊的封面保存在 `release/media/preview.jpg` 或 `.png`，小于 1 MiB；建议正方形。展示图放在 `release/media/gallery/`。原始未处理截图可放在忽略的 `artifacts/`。

## 独立上传工具

游戏本机原生 `ModWorkshop` 只提供浏览、订阅和查询；上传由单独的 `tools/WorkshopPublisher` 完成，使用本机游戏附带的 Steamworks.NET 与原生 Steam API。上传代码不会进入模组 DLL。工具不分发这些游戏依赖，不读取 Steam 密码或浏览器凭据。

先检查当前 Steam 登录与游戏许可：

```powershell
dotnet run --project tools/WorkshopPublisher -c Release -- --status --game-dir '游戏目录'
```

准备图片后，使用已验证成品上传：

```powershell
.\scripts\Publish-Workshop.ps1 -PackagePath '已验证的 deep_space_qol 成品目录' -PreviewPath '封面图片完整路径'
```

默认公开发布；只想先检查可加 `-ValidateOnly`，只想检查登录可加 `-Status`。上传工具记录工坊 ID、账号归属与远端状态到被忽略的 `release/workshop-state.local.json`。后续更新复用 ID，不自动创建新条目；创建结果不明时停止并要求先核对远端。已有展示图时不覆盖或重复追加展示图。

Steam 若要求接受工坊协议，工具只返回条目地址和待处理状态，由账号本人在 Steam 官方页面确认，随后重跑同一命令继续上传。

## 发布核对

- GitHub 仓库属于实际授权的账号，分支、版本标签和附件哈希一致。
- 工坊发布成功必须有 Steam 的成功回调及可复查条目；仅“已创建 ID”不代表上传成功。
- 本地副本会遮蔽同 ID 工坊版本；转用订阅版前退出游戏并保留备份后移出本地副本。
- 不把离线测试描述为游戏内验收。游戏内截图与复测确认后再更新对应说明。
