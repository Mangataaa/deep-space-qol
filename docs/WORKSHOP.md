# GitHub 与 Steam 创意工坊发布

## 当前发布

0.2.3 为卢剧情箱保护、枪械整件收纳和整理算法修复版本。详细变更与离线验证范围见 `release/release-notes-v0.2.3.md`；个人存档和提取布局不上传。以下 0.2.2 记录仅作为历史发布与实机截图的来源。

2026-11-12 的 0.2.3 已通过七套回归、原生扫描及本地安装校验，并更新同一工坊 ID；公开 API 确认条目公开、未封禁，内容为 151,370 字节。安装及工坊包 DLL SHA-256 为 `abaa1e869546af8d361e100415206f86ce1c694eb1983bce46f3b3aba0ee1c3d`。GitHub ZIP SHA-256 为 `9be5d0181310fce5db83ed289f07c760fb06a5397c2fce4ccf572aeb200f380a`。保留六张原截图及实机像素封面；0.2.3 游戏内复测仍待完成。

- GitHub 公开源码：[Mangataaa/deep-space-qol](https://github.com/Mangataaa/deep-space-qol)
- 0.2.3 测试版附件：[v0.2.3 Release](https://github.com/Mangataaa/deep-space-qol/releases/tag/v0.2.3)
- Steam 公开条目：[深空管家 / Deep Space QoL](https://steamcommunity.com/sharedfiles/filedetails/?id=3836735823)，工坊 ID `3836735823`，游戏 App ID `4348910`。

2026-11-09 更新至 0.2.2。Steam 返回上传成功，匿名公开接口确认条目公开、未封禁，内容大小 141,615 字节；展示页使用六张用户实机截图，保持同一工坊 ID。GitHub Release 为 prerelease，ZIP SHA-256 为 `4149ae1adc7f4c85dfd1086bcceb0e70f4b4bc0e31ae14be1d18311d0000ce07`，与远端附件摘要一致。随后补图仅更新展示信息，保留原安装包及校验值。

随后将工坊封面改为用户实机像素场景拼版。更新仅替换封面与展示说明；Steam 公开接口确认条目仍公开、未封禁，工坊内容大小和 0.2.2 DLL 校验值保持不变。

前四张展示 0.2.1 的上货、分类及同一储物页整理前后；后两张展示 0.2.2 固定水源的右键入口与装瓶前后。用户复测截图显示台面两瓶和机器舱内打印机成品区两瓶均由空瓶变为装水状态，提示“已装 4 瓶，共 8000 毫升”。这确认该场景修正通过，不扩展为完整兼容性或存档验收。

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

用于工坊的正方形封面保存在 `release/media/preview.png`，小于 1 MiB。封面使用用户提供的实机截图裁切拼版，保留游戏像素画面；截图中的游戏美术与角色仍归原权利人所有，不属于本项目 MIT 许可。源裁切保存在 `release/media/cover-assets/shop.png`，可运行 `scripts/New-WorkshopCover.ps1` 重建；需要 Python Pillow。`scripts/New-WorkshopGallery.ps1` 接受原四图或新增装瓶前后图对，保留完整游戏画面并在图外加版本说明；输出位于 `release/media/gallery/`。原始未处理截图可放在忽略的 `artifacts/`。

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

默认公开发布；只想先检查可加 `-ValidateOnly`，只想检查登录可加 `-Status`。上传工具记录工坊 ID、账号归属与远端状态到被忽略的 `release/workshop-state.local.json`。后续更新复用 ID，不自动创建新条目；创建结果不明时停止并要求先核对远端。工坊追加图片按远端原文件名去重，保留已有图片；相同文件名不会覆盖或重复上传。

仅补图片和说明时加 `-MetadataOnly`：要求已有条目及相同发布版本、DLL 哈希，不重新上传安装内容，也不改本地游戏安装。

Steam 把各语言版本说明分开保存；工具在内容上传后用独立、仅改文字的更新，把同一份说明同步到默认英语回退，避免匿名 API 或英语界面仍显示旧版本号。这里同步的是中文原文，没有声称提供英文翻译。默认语言同步失败会保留待完成状态，不能报告整项完成。

Steam 若要求接受工坊协议，工具只返回条目地址和待处理状态，由账号本人在 Steam 官方页面确认，随后重跑同一命令继续上传。

## 发布核对

- GitHub 仓库属于实际授权的账号，分支、版本标签和附件哈希一致。
- 工坊发布成功必须有 Steam 的成功回调及可复查条目；仅“已创建 ID”不代表上传成功。
- 本地副本会遮蔽同 ID 工坊版本；转用订阅版前退出游戏并保留备份后移出本地副本。
- 不把离线测试描述为游戏内验收。游戏内截图与复测确认后再更新对应说明。
