# UnityMiniGameFramework

Unity底层框架，**0.2.0预览版采用单个Package Manager Git URL安装**。内置固定版本UniTask、R3 Unity/Core和YooAsset，源码、程序集和许可原样保留。Unity官方依赖自动解析，不需要Python安装器或先安装四个外部Git包。

## 安装

Unity2022.3.62f3中打开`Window > Package Manager`，点击`+ > Add package from git URL...`，输入：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

需要Git、网络和Unity官方registry连接。`?path=/Packages/com.minigame.framework`选择仓库包目录，`#v0.2.0`选择标签对应版本；路径必须写在#之前。标签必须真实存在。不要省略#而让生产工程跟随默认分支，也不要移动已发布标签。

## 选择版本与升级

在Package Manager添加同一个包的另一已发布Tag URL，或改变自己manifest中该包URL的#tag。例如将`#v0.2.0`替换为下一版的已发布Tag；未发布的Tag不能用于安装。UPM锁记录实际commit，不自动跟随移动标签。

升级和回滚都由Package Manager处理，不覆写游戏Assets、已导入样例或玩家数据。游戏schema迁移由消费者负责，代码回滚不保证业务数据可逆。

0.1.x是旧双包和外部Git依赖布局。迁移前备份工程，移除旧框架、com.minigame.dependencies.r3、com.cysharp.unitask、com.cysharp.r3、com.tuyoogame.yooasset包及重复DLL，再安装0.2.x。内置版不能和独立同名程序集共存；不要删除业务Assets或存档。`LegacyPackages~/0.1.3`仅保存原始旧快照，不会被UPM安装。

## 使用与示例

Package Manager选择MiniGame Framework，在Samples中点击Basic Playable的Import。打开导入目录`Scenes/Boot.unity`并运行，支持开始、目标点击、胜负、暂停、重开和保存。不要重复导入同GUID样例；已有TMP Essential Resources或同名程序集时先检查冲突。

包内`Documentation~/ApiUsage.md`说明显式组合、初始化、更新、取消和释放所有权；顶层`Documentation/`说明安装、迁移与支持范围。框架机制测试需宿主显式配置testables。没有业务账号、广告、购买、运营或厂商SDK，无Luban/MessagePack。

## 分发与来源

Git入口为`Packages/com.minigame.framework`。该包顶层dependencies只有Unity官方模块、uGUI1.0.0、TMP3.0.7和SBP1.21.25。`ThirdParty/`内UniTask2.5.11、R3Unity/Core1.3.0、YooAsset2.3.17均来自固定官方来源；源码/DLL/asmdef/meta未修改，MIT/Apache-2.0及Microsoft原NOTICE保留，逐文件摘要在`Documentation~/third-party-lock.json`。自有代码MIT（Copyright 2026 Agi）。

YooAsset的PackageInfo显示外层框架版本，准确上游版本以锁记录为准；没有修改第三方函数掩盖版本差异。程序集名称保持，业务代码不需更名。

唯一底层实现开发源仍为原UnityMiniGameFramework工程。`Tools/build-git-package.py`将该工程源码、锁定三方依赖和文档组装成发行包，主工程不同时内置重复依赖。旧`Tools/release-tool.py`仅用于0.1.x历史套件，不是0.2.x默认安装入口。0.1.x已验收证据与冻结产物不改写，也不冒充新GitURL验收。

## 验证范围

新单包布局已通过UPM本地安装、真实Samples Import与消费者2项测试；远端Git标签安装的结果另行归档，公开Tag不自动代表Unity验收。仅声明Unity2022.3.62f3、旧输入、macOS环境；IL2CPP、WebGL、移动、小游戏SDK及四项暂缓性能门槛不声明通过。

发行入口：[v0.2.0](https://github.com/agioracle/UnityMiniGameFramework/releases/tag/v0.2.0)。源码标签、Release及实际Git安装状态以远端与`Evidence/`记录为准。
