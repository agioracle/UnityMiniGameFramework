# 安装与发行边界

## 内容

发行套件 `Releases/<version>`：`Packages/com.minigame.framework`、`Packages/com.minigame.dependencies.r3`、`dependencies.json`、`release.json`、`release-tool.py`、`Validation~/Consumer`。本地套件用于验证和可重复安装；仓库根的 Packages 为最新组装版本，不在仓库副本修改底层实现。唯一实现源仍为原 UnityMiniGameFramework 开发项目。

## 推荐安装

1. 新建 Unity 2022.3.62f3 工程，使用旧输入系统和 .NET Standard 2.1。
2. 退出该工程 Editor，备份/提交用户工程自身修改。
3. Python 3.10+ 执行 `python3 <suite>/release-tool.py install <suite> <project> --sample`。
4. 打开工程等待 Package Manager 实际解析。外部包需 Git/网络；原缓存不属于发行物。
5. 打开 `Assets/Samples/MiniGame Framework/Basic/Scenes/Boot.unity`。框架不改渲染管线或 Build Settings。

已有 TMP Essential Resources 或同名程序集时先做冲突检查；不要重复导入相同样例。工具拒绝已编辑的受管包和外部包声明冲突；它不会替用户决定依赖升级。原工程其他包、场景和 ProjectSettings 保持。

## Git 安装

将 dependencies.json 的 Git 来源加入宿主 manifest，并安装 R3 核心及框架包：仓库 `https://github.com/agioracle/UnityMiniGameFramework.git?path=Packages/<package>#v0.1.3`，其中 package 为 `com.minigame.dependencies.r3` 或 `com.minigame.framework`。发布入口为 [v0.1.3](https://github.com/agioracle/UnityMiniGameFramework/releases/tag/v0.1.3)。此前本地验收未创建Tag或推送的记录保持原样；公开发布不代表远端Git方式Unity安装已验收。Tag/commit必须真实存在，其他版本不能使用占位地址。

## 验证

`python3 <suite>/release-tool.py verify <suite>` 校验 SHA-256、文件白名单及包身份。摘要是完整性检查，不是签名或可信发布者认证。新版本依赖解析结果以 Unity 生成的 packages-lock.json 为准。
