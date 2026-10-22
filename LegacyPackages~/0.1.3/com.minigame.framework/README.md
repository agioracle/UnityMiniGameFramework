# MiniGame Framework

独立底层框架，MIT（Copyright 2026 Agi）。Unity 2022.3.62f3 / macOS Development Mono 为本轮验证范围；其他 Unity 版本、IL2CPP、WebGL、移动及小游戏 SDK 不声明支持。没有业务账号、广告、购买、运营模型，没有 Luban 或 MessagePack。

## 安装

获取完整版本套件，包含两个包、固定外部依赖、安装工具及许可。先退出目标工程 Unity Editor，再运行 Python 3.10+：

`python3 <suite>/release-tool.py install <suite> <project>`

可加 `--sample`，一次性导入 Basic 到 `Assets/Samples/MiniGame Framework/Basic`。之后用 Unity 打开目标工程，等待依赖解析。UniTask、R3 Unity、YooAsset 通过固定 Git 提交下载，需要 Git 与网络；这不是全离线套件。工具只管理两个明确包，保留其他 manifest 字段和依赖，冲突/手工修改包会拒绝安装。不会手写 packages-lock.json、复制缓存或改项目设置。

通过 Package Manager 手工安装或 Git URL 时也必须先添加 `dependencies.json` 中的三个 Git 依赖及 R3 核心包。本包版本依赖不能让 UPM 自动发现未注册的 Git 包；不能仅添加本包 URL 就省略这些依赖。

## 使用

安装框架不自动创建场景。可选 Basic 样例在 Package Manager 的 Samples 导入，或使用上述 `--sample`，二选一，不能在一个工程重复导入相同 GUID。打开导入目录的 `Scenes/Boot.unity`，运行并点击开始；8步内命中5个目标获胜，可暂停/重开，结算保存次数。发行套件自带所需 TMP Essential Resources（原许可保留），已有同 GUID TMP 资源的工程应先检查冲突，不覆盖原资源。

程序集显式引用 `MiniGame.Framework.Foundation`、`MiniGame.Framework.Runtime`，需要 R3 生命周期辅助时引用 `MiniGame.Framework.Reactive`。消费者决定游戏初始化、更新与释放顺序，框架不自动定位业务模块。

详见随包 `Documentation~/ApiUsage.md`、`Installation.md`、`Upgrade.md`、`Support.md`、`THIRD_PARTY_NOTICES.md`、`CHANGELOG.md`。

## 测试

在宿主 manifest 加 `"testables": ["com.minigame.framework"]`，使用 Test Runner。默认不启用性能采集。框架测试所需字体夹具已随包交付，不要求宿主 Basic 服务示例。Basic 导入后附纯规则及真实按钮测试。`Validation~/Consumer` 是套件外的发行验证消费者，不自动导入产品。

## 升级

关闭 Editor，向相同工程再次安装另一套件版本，不加 `--sample`。只替换工具管理且未被修改的两个包；原样例、消费者、ProjectSettings 和玩家存档不替换。回滚使用原版本套件同一命令。新版本若已经改变用户 schema，回滚框架不意味着数据也可回滚；迁移由消费者定义。

这是范围受限的预览发行物，不保证所有平台、二进制兼容或性能预算。没有自动 Git 提交、Tag、推送或线上发布。
