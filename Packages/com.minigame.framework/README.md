# MiniGame Framework — Git UPM

从0.2.0起，发行包内置固定版本UniTask、R3 Unity/Core和YooAsset，保留原始源码、DLL、asmdef、meta与许可。只需一个Git URL；Unity官方模块/uGUI/TMP/SBP由Package Manager按顶层dependencies解析。

## 安装

Unity 2022.3.62f3打开`Window > Package Manager`，点击`+ > Add package from git URL...`，输入：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

格式为`仓库.git?path=/包目录#tag`，问号路径必须在#前。Tag必须已推送存在。未写#时跟随默认分支，不推荐用于正式项目。需要Git、网络和Unity官方registry连接，不需要Python安装器，也无需手改manifest添加几个第三方Git包。

## 选择版本、升级与回滚

要安装其他兼容版本，将URL末尾`#v0.2.0`换为真实Tag，例如下一版`#v0.2.1`；两个Tag不存在前不能冒充已发布。UPM锁记录最终commit。升级使用同一个包的新Tag URL；回滚使用旧Tag URL。修改URL不会覆盖游戏Assets、存档或项目设置，已导入样例按用户资产处理，不自动迁移。

0.1.x是旧双包/外部Git依赖发行，不能把#v0.1.x当成单URL自包含版本。迁移前在备份的工程中移除旧com.minigame.framework、com.minigame.dependencies.r3、com.cysharp.unitask、com.cysharp.r3、com.tuyoogame.yooasset包及重复DLL；不要删除业务Assets或玩家数据。同名程序集不能与内置库共存。

## 示例与服务

Package Manager选择MiniGame Framework，Samples中导入Basic Playable。打开导入目录Scenes/Boot.unity，点击开始、目标、暂停、重开，结算保存进度。禁止在同工程重复导入相同GUID。TMP Essential Resources随包带入，已有同GUID资源时先检查冲突。

服务生命周期和所有权见`Documentation~/ApiUsage.md`。不含业务账号/广告/购买/SDK，无Luban或MessagePack。框架测试需显式testables。ThirdParty源码无改动，但YooAsset PackageInfo显示的是外层框架版本；准确上游版本与摘要见`Documentation~/third-party-lock.json`，不得把框架版本当YooAsset版本。

支持范围：Unity2022.3.62f3、旧输入、macOS Development Mono。IL2CPP/WebGL/移动/小游戏及性能预算未验收。公开Tag/Release与Unity安装验收分开记录，以Evidence为准。
