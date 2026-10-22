# UnityMiniGameFramework v0.1.3 — 预览发行

可独立安装、使用和升级的Unity底层框架。本次发布范围：Unity 2022.3.62f3、旧输入系统、macOS Development Mono；MIT（Copyright 2026 Agi），第三方许可原样保留。

## 下载与安装

- `UnityMiniGameFramework-0.1.3.tar.gz`：推荐发行套件，含两个UPM包、固定外部依赖、安装器、可导入Basic、许可及随包文档。
- `UnityMiniGameFramework-0.1.2.tar.gz`：已验收的升级/回滚基线附件，不是另一个Git Tag或独立推荐Release。
- `release-acceptance.json`、`delivery-supplement.json`：实际验收摘要与已知测试限制。
- `SHA256SUMS.txt`：四个附件的SHA-256，校验完整性，不等于签名认证。

GitHub自动Source code归档是仓库源码，不含被忽略的Releases目录。使用推荐套件解压后关闭目标Unity Editor，再运行：

`python3 <suite>/release-tool.py install <suite> <project> --sample`

打开Unity等待实际解析，运行导入的`Assets/Samples/MiniGame Framework/Basic/Scenes/Boot.unity`。外部UniTask/R3 Unity/YooAsset需Git及网络，不是全离线套件。已有TMP或相同GUID/程序集的工程先检查冲突，不重复导入。升级/回滚不加`--sample`，不覆写用户消费者、样例、设置或存档。

Git安装须先合并仓库dependencies.json，并安装两个同标签包：

- `https://github.com/agioracle/UnityMiniGameFramework.git?path=Packages/com.minigame.dependencies.r3#v0.1.3`
- `https://github.com/agioracle/UnityMiniGameFramework.git?path=Packages/com.minigame.framework#v0.1.3`

远端Git方式Unity安装没有新增验收；公开推送不扩张已有支持范围。

## 本版变化与既有验收

兼容修复：LegacyPointerInput.IsConsumedByUi在访问EventSystem前拒绝非有限位置，公开签名不变。

已有冻结发行物验收：0.1.2初装22项EditMode/13项PlayMode通过；0.1.2→0.1.3升级同两组通过，新Mono Player构建与真实玩法/保存烟测退出0；回滚0.1.2后2项消费者测试通过，71个消费者/样例/设置/存档文件摘要不变。发行工具7项通过，依赖审计补充2项通过。此次发布只核对并上传原产物，不重跑测试或重新组装版本。

## 已知限制

- 这是预览版，不是完整混合变现游戏；不含广告、购买、账号、运营或厂商SDK。
- 不声明IL2CPP、WebGL、移动及微信/抖音/快手支持；完整负载、内存归因、目标冷缓存及批准预算仍暂缓。
- 回滚已恢复，但曾因IDE审批中断备份清理；324个本地备份文件保留，原失败记录不改写，不将其上传为玩家数据。
- 补充主工程测试过滤未生效的101通过/3失败及后续修复详见delivery-supplement，不宣称全项目全量回归通过。
- 冻结套件和Evidence保留发布前的本地验收状态；该历史文案不代表本Release未发布。0.1.0/0.1.1失败或发现不完整的产物不上传为可用版本。
