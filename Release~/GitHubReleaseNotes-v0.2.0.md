# UnityMiniGameFramework v0.2.0 — 单Git URL预览版

## Package Manager安装

Unity2022.3.62f3中打开 `Window > Package Manager`，选择 `+ > Add package from git URL...`，粘贴：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

**只需一个URL，不需要Python安装器，不需要先安装多个第三方Git包。** `?path`定位仓库中的包，`#v0.2.0`锁定真实发布Tag。升级/回滚时换为同一包的其他真实Tag；不移动已有Tag、不强推。UPM记录实际commit。

0.2.0内置官方未修改的UniTask2.5.11、R3Unity/Core1.3.0、YooAsset2.3.17，保留原源码、DLL、asmdef、meta、LICENSE/NOTICE。顶层依赖只有Unity官方模块、uGUI1.0.0、TMP3.0.7、SBP1.21.25，Unity自动解析，仍需Git、网络及官方registry。

## 使用和迁移

Package Manager选择MiniGame Framework，在Samples中Import Basic Playable，运行导入目录 `Scenes/Boot.unity`。支持开始、目标点击、胜负、暂停、重开和保存，不需要手动绑定组件。

从旧0.1.x迁移前备份工程，移除旧框架、R3Core兄弟包、独立UniTask/R3Unity/YooAsset和重复DLL再安装0.2.x；同名程序集不能共存。不删除用户业务Assets或玩家存档，不重复导入相同GUID样例。0.1.x冻结产物与历史证据保留，不冒充本版Git验收。

YooAsset自身PackageInfo显示外层包版本，上游准确版本及原始文件摘要位于 `Documentation~/third-party-lock.json`，没有修改第三方版本读取函数。

## 验证与附件

实际Git URL `#v0.2.0`在空白Unity工程解析，UPM锁记录 source=git 与标签commit；仅一个框架Git依赖，原第三方包无需单独安装；Package Manager真实Samples Import通过，远端包消费者两项通过（基础API/暂停计时/池/存档及真实样例点击获胜/进度保存）。详情见附件 `git-install-v0.2.0.json`。首次本地预检的探针类型歧义失败日志保留，修复后通过；不隐藏失败，不重跑旧0.1.x任务。

此次默认交付是Git Tag源码包，不另上传旧双包安装套件；GitHub Source code归档是仓库源码，Package Manager只选择URL的包目录。`SHA256SUMS-v0.2.0.txt`是证据附件完整性校验，不是签名。

## 范围

MIT（自有代码Copyright2026 Agi），上游许可独立保留。预览版，不包含账号、广告、购买、运营或厂商SDK，无Luban/MessagePack。只验证Unity2022.3.62f3、旧输入和macOS Editor；新Git包未重新做Player/AOT/设备构建，不把旧Mono证据作为本版构建通过。IL2CPP/WebGL/移动/小游戏SDK及完整负载、内存归因、目标冷缓存和批准预算未放行。
