# 选择版本、升级与回滚

[返回首页](../README.md) · [安装指南](Installation.md)

## 用标签选择版本

安装地址格式：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

只改变最后的 `#标签`，不要改变仓库地址和包目录。可用版本见 [GitHub Releases](https://github.com/agioracle/UnityMiniGameFramework/releases)。目前已验证的单 URL 安装标签是 **v0.2.0**。

标签会解析为固定提交，并写入项目的 `Packages/packages-lock.json`。省略标签会使用默认分支，不适合需要稳定版本的项目。

## 升级

1. 备份或提交自己的项目改动。
2. 阅读目标版本的更新说明，确认依赖和 API 变化。
3. 在 **Package Manager → + → Add package from git URL…** 中输入同一包的新标签地址。
4. 等待解析、编译，核对 Package Manager 显示的版本。
5. 验证自己的场景、玩法和存档，再发布游戏。

也可以修改项目 `Packages/manifest.json` 中 `com.minigame.framework` 对应 URL 的标签。只改这一项，保留其他依赖；锁文件由 Unity 更新，不要手工填写提交摘要。

**不存在的标签不能安装。** 本文不提供尚未发布的下一版作为可运行地址。

## 回滚

重复安装操作，使用原来的已发布标签地址即可。回滚后同样检查编译、场景和存档。

Package Manager 更新的是包，不会主动覆写游戏的 `Assets/`、已导入样例或玩家存档。但如果新版游戏已经修改了存档格式，回滚框架不会自动恢复旧数据，需由游戏自己的迁移或备份策略处理。

## 样例不会自动升级

导入后的 Basic 是你的项目资产。升级包不会替换它。

不同版本的样例可能保留相同 GUID，不要直接导入第二份。需要更新样例时，先备份用户改动，比较差异后再替换；也可以用新建工程查看新版样例。

## 从 0.1.x 迁移

0.1.x 使用旧双包和外部依赖布局；0.2.x 是内置依赖的单包布局。迁移步骤：

1. 备份工程，检查其他插件是否依赖独立 UniTask、R3 或 YooAsset 包。
2. 移除旧 `com.minigame.framework`、`com.minigame.dependencies.r3`、`com.cysharp.unitask`、`com.cysharp.r3` 和 `com.tuyoogame.yooasset` 包。
3. 处理手动导入的重复源码或 DLL，保留业务代码、场景和存档。
4. 按[安装指南](Installation.md)安装 `v0.2.0`，验证自己的项目。

不要把 `#v0.1.x` 当作单 URL 内置版，也不要在存在依赖冲突时直接清空 `Assets/`。无法移除独立依赖时，应先评估当前发行方式是否适合项目。

## 验证边界

`v0.2.0` 已完成远端 Git 安装和样例测试。旧 0.1.x 的升级、回滚记录是历史证据，不代表 0.2.x 已完成两个不同标签之间的升级演练。每个新版本仍需自己的兼容性验证。
