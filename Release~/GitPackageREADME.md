# MiniGame Framework

组装版本：**{{VERSION}} 预览版**。提供资源、场景、UI、音频、计时、存档和网络等通用服务；游戏业务由你的项目实现。

## 安装

打开 Unity 的 **Window → Package Manager → + → Add package from git URL…**，输入：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v{{VERSION}}`

标签必须真实发布。只需一个地址，UniTask、R3 和 YooAsset 已内置，Unity 官方依赖自动解析；需要 Git 和网络，不需要 Python 安装脚本。

已有独立 UniTask、R3、YooAsset 或同名 DLL 时，先备份并处理重复依赖，不能并存。详情见[安装指南](Documentation~/Installation.md)。

## 运行示例

Package Manager 选中 **MiniGame Framework**，点击 **Samples → Basic Playable → Import**。打开导入目录中的 `Scenes/Boot.unity`，进入 Play Mode。

## 接入与升级

- [API 使用指南](Documentation~/ApiUsage.md)：创建服务、逐帧更新、取消与释放责任。
- [升级指南](Documentation~/Upgrade.md)：使用其他已发布标签选择版本、升级或回滚。
- [支持范围](Documentation~/Support.md)：已验证环境与限制。

升级不会主动覆写已导入的样例或玩家存档，业务数据迁移由游戏负责。不要重复导入相同 GUID 样例。

组装版本号不代表该版本已发布或验收。随包文档保留对应测试版本的声明，其他版本、Player、平台及性能需要单独验证；不能将历史结果当作本版保证。

自有代码使用 [MIT](LICENSE)。第三方各自许可和来源见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，上游准确版本与摘要见 `Documentation~/third-party-lock.json`。
