# UnityMiniGameFramework

一个提供资源、场景、UI、音频、计时、存档和网络等通用服务的 Unity 框架。游戏规则和业务逻辑由你的项目实现。

当前版本：**0.2.0 预览版** · [版本发布](https://github.com/agioracle/UnityMiniGameFramework/releases) · [MIT 许可证](LICENSE)

## 安装

1. 在 Unity 中打开 **Window → Package Manager**。
2. 点击 **+ → Add package from git URL…**。
3. 输入以下地址，点击 **Add**，等待安装完成：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

只需这一个地址。UniTask、R3 和 YooAsset 已包含在框架中，Unity 官方依赖会自动安装。需要 Git 和网络，不需要运行 Python 脚本。

**已有 UniTask、R3 或 YooAsset？** 不要同时安装重复版本，先阅读[安装与冲突处理](Documentation/Installation.md)。

## 运行示例

在 Package Manager 中选中 **MiniGame Framework**，展开 **Samples**，点击 **Basic Playable → Import**。

打开导入目录中的 `Scenes/Boot.unity`，进入 Play Mode。示例支持开始、点击目标、胜负结算、暂停、重开和进度保存。

## 选择版本

URL 末尾的 `#v0.2.0` 是版本标签。换成另一个**已发布的标签**即可选择版本；升级或回滚也使用这个方式。可用标签见[版本发布](https://github.com/agioracle/UnityMiniGameFramework/releases)。

不要省略标签或使用不存在的版本。详细操作见[升级与旧版迁移](Documentation/Upgrade.md)。

## 使用文档

| 想做什么 | 阅读 |
| --- | --- |
| 安装、导入示例、处理冲突 | [安装指南](Documentation/Installation.md) |
| 在游戏中接入框架服务 | [API 使用指南](Documentation/ApiUsage.md) |
| 升级、回滚、从 0.1.x 迁移 | [升级指南](Documentation/Upgrade.md) |
| 了解已验证环境和限制 | [支持范围](Documentation/Support.md) |

## 当前验证范围

`v0.2.0` 已在 Unity **2022.3.62f3 / macOS / 旧输入系统**的空白工程中完成远端 Git 安装、示例导入和两项消费者测试。[验收记录](Evidence/git-install-v0.2.0.json)

这是预览版。本版未验证 Player 构建、IL2CPP、WebGL、移动设备或小游戏 SDK，也不包含账号、广告、支付和运营系统。不要将其他版本的测试结果视为本版保证。

## 维护说明

用户安装入口是 `Packages/com.minigame.framework`。第三方来源和许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，版本变化见 [CHANGELOG.md](CHANGELOG.md)。

底层实现和测试在独立的 Unity 开发工程维护。本仓库保留当前 Git 包、使用文档、许可、验收证据和必要的发行工具；旧快照与旧工具已移到仓库外归档，不参与安装。

维护者使用 `Tools/build-git-package.py --source <开发工程> --repository <本仓库> --version <新版本>` 组装新包。它读取开发工程的源码和已解析依赖，文档统一取自本仓库 `Documentation/`，变更与第三方声明取自根文件。`Release~/` 只保留包 README 模板和原始第三方许可证，不再维护第二份完整指南。

组装结果写入被 Git 忽略的 `Releases/<新版本>/`；已有标签或产物的版本会被拒绝，不覆盖已发布内容。发布前仍需独立验收，组装命令不自动替换根包、创建标签或推送。

清理记录见 [仓库归档清单](Evidence/repository-cleanup-20261022.json)。移出本地副本不删除 Git 历史，现有 `v0.2.0` 及其中的包保持不变。
