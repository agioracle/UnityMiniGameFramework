# 支持范围与限制

[返回首页](../README.md)

## 当前版本验证了什么

版本：**v0.2.0 预览版**。

| 项目 | 已验证范围 |
| --- | --- |
| Unity | 2022.3.62f3 |
| 系统 | macOS |
| 输入 | 旧输入系统 |
| 安装 | 空白工程通过 Git URL 和 `#v0.2.0` 安装，锁定对应提交 |
| 示例 | Package Manager 导入 Basic，实际按钮操作与保存测试通过 |
| 消费者测试 | 2 项通过：基础服务/存档、示例玩法/进度保存 |

详细记录：[Git 安装验收](../Evidence/git-install-v0.2.0.json)。本版验证的是 Editor 中的安装和使用，没有重新进行 Player 构建或全项目测试。

## 尚未验证

- 其他 Unity 版本、Windows 或 Linux。
- 本版 Player 构建、IL2CPP、WebGL、Android、iOS及小游戏 SDK。
- 两个不同 0.2.x 标签之间的升级和回滚。
- 完整负载、内存增长归因、目标冷缓存和批准后的性能预算。

“尚未验证”不等于“一定不能运行”，但不能作为生产支持保证。旧版 Mono Player 或其他历史测试结果不自动适用于本版。

## 框架提供与不提供的内容

提供：计时、暂停、状态机、池、资源与场景、UI、音频、配置、存档、HTTP、本地化及编辑器辅助工具。

不提供：游戏业务规则、账号、广告、支付、运营后台或厂商 SDK。当前包也不包含 Luban、MessagePack 或可选 Protobuf 扩展。

## 依赖与许可

| 依赖 | 版本 | 安装方式 |
| --- | --- | --- |
| UniTask | 2.5.11 | 框架内置 |
| R3 Unity / Core | 1.3.0 | 框架内置，Core 所需 DLL 一并包含 |
| YooAsset | 2.3.17 | 框架内置 |
| uGUI | 1.0.0 | Unity 自动解析 |
| TextMesh Pro | 3.0.7 | Unity 自动解析 |
| Scriptable Build Pipeline | 1.21.25 | Unity 自动解析 |

自有代码使用 [MIT](../LICENSE)。第三方代码、DLL和资源保留各自许可，不能都视为 MIT；见[第三方声明](../THIRD_PARTY_NOTICES.md)。

准确上游版本、提交和文件摘要在包内 `Documentation~/third-party-lock.json`。由于库已内置，YooAsset 的 `PackageInfo` 会显示外层框架版本，不应用它判断 YooAsset 版本。

## 使用边界

- 不在 `Library/PackageCache/` 中修改框架或第三方源码。
- 存档校验用于发现损坏，不是加密或防作弊。
- HTTP 取消不代表服务端操作被撤销。
- 升级框架不会替你迁移业务数据，也不保证所有二进制消费者兼容。
