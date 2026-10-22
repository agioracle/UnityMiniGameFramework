# 安装与运行示例

[返回首页](../README.md) · [升级指南](Upgrade.md)

## 安装前

建议先使用 **Unity 2022.3.62f3、macOS、旧输入系统**体验；这是当前 Git 安装和示例验证使用的环境。其他环境见[支持范围](Support.md)。

电脑需要安装 Git，并能访问 GitHub 和 Unity 包服务。不需要克隆仓库、不需要下载压缩包，也不需要 Python。

## 安装框架

1. 打开 **Window → Package Manager**。
2. 点击 **+ → Add package from git URL…**。
3. 粘贴地址，点击 **Add**：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

4. 等待依赖安装和脚本编译完成。列表中出现 **MiniGame Framework 0.2.0** 即可。

框架已包含 UniTask、R3 和 YooAsset。uGUI、TextMesh Pro、Scriptable Build Pipeline（SBP）及所需 Unity 模块会自动解析。

URL 中的 `?path=` 指定包目录，`#v0.2.0` 指定版本标签。必须先写路径，再写标签；其他版本见[升级指南](Upgrade.md)。

## 运行 Basic 示例

1. 在 Package Manager 中选中 **MiniGame Framework**。
2. 展开 **Samples**，点击 **Basic Playable → Import**。
3. 在导入目录中打开 `Scenes/Boot.unity`，进入 Play Mode。

Unity 通常将它导入到 `Assets/Samples/MiniGame Framework/0.2.0/Basic Playable/`；以 Package Manager 显示的路径为准。

点击开始，在有限步数内选中目标即可获胜。示例也提供失败、暂停、重开和结算保存。安装框架本身不会自动打开或替换你的场景。

## 已有项目：先检查重复依赖

内置库不能与同名外部程序集同时存在。重点检查：

- `com.cysharp.unitask`
- `com.cysharp.r3`
- `com.tuyoogame.yooasset`
- `com.minigame.dependencies.r3`
- `Assets/` 中手动导入的相同源码或 DLL

先备份项目，再处理重复项。如果其他插件依赖这些独立包，不要直接删除：需先确认插件兼容性，再决定是否使用当前内置版。

已有 TMP Essential Resources 或旧 Basic 示例时，也要检查相同资源 GUID。不要通过反复导入或覆盖资源解决冲突。

## 常见问题

| 问题 | 检查方式 |
| --- | --- |
| 提示 Git 不可用 | 安装 Git，重启 Unity，确认终端能运行 `git --version` |
| 无法下载仓库 | 检查 GitHub 网络访问、代理和 URL 拼写 |
| 找不到版本 | 使用[已发布的标签](https://github.com/agioracle/UnityMiniGameFramework/releases)，不要填未发布版本 |
| 同名程序集或 DLL 报错 | 检查上面的重复依赖，不修改包内第三方源码掩盖冲突 |
| 样例重复或字体异常 | 检查已有 TMP 资源和旧样例，保留用户修改后再处理重复 GUID |

安装后接入自己的游戏，阅读 [API 使用指南](ApiUsage.md)。
