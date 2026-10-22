# API 使用指南

[返回首页](../README.md) · [运行示例](Installation.md#运行-basic-示例)

建议先运行 Basic，再查看它的 `Presentation/BasicGameView.cs`：这里演示了初始化、按钮输入、暂停、结算和存档。

## 先理解三个规则

1. **由游戏创建和管理服务。** 框架不会自动查找业务模块，也不会替你管理全部服务。
2. **初始化完成后再使用。** 等待 `InitializeAsync` 完成，再进入玩法；处理取消和失败。
3. **谁持有资源，谁负责释放。** 使用期间保持资源句柄，结束后调用对应的释放方法。

下文的“租约”就是带有释放责任的资源句柄。不要取出对象后立刻释放句柄，再继续使用该对象。

## 程序集与命名空间

有自己的 `.asmdef` 时，按需添加引用：

| 程序集 | 用途 |
| --- | --- |
| `MiniGame.Framework.Foundation` | 不依赖 Unity 的计时、暂停、状态机和普通对象池 |
| `MiniGame.Framework.Runtime` | Unity 中的服务 |
| `MiniGame.Framework.Reactive` | R3 显示期订阅辅助 |
| `UniTask` | 使用异步入口 |

大部分服务在 `MiniGame.Framework` 命名空间；R3 辅助类在 `MiniGame.Framework.Reactive`。其他 UI、TMP 或 R3 程序集按自己的代码需要引用，不必全部添加。

## 启动、更新、关闭

- 创建 `FrameworkRuntime`，等待 `InitializeAsync(options, cancellation)`。
- 每帧调用 `Tick(scaledDelta, unscaledDelta)`，通常传入 Unity 的缩放时间和非缩放时间增量。
- 结束时调用 `Dispose()`。一个实例只能初始化一次，重启需要新实例。

`FrameworkRuntime` 只管理自己的计时器和暂停状态，**不会自动创建、更新或关闭资源、UI、音频等服务**。需要逐帧更新的其他服务由游戏自己驱动。

## 常用服务

### 计时、暂停、状态机、池

| 类型 | 常用入口 | 需要注意 |
| --- | --- | --- |
| `TimerService` | `Schedule`、`Tick`、`Dispose` | 保留计时句柄，结束时取消或关闭服务；回调有预算，循环计时不无限补追 |
| `PauseGate` | `Acquire`、`Dispose` | `Acquire()` 返回暂停令牌；每个来源只释放自己的令牌 |
| `StateMachine<T>` | `Add`、`Change`、`Dispose` | 游戏决定状态和转换规则 |
| `ObjectPool<T>` | `TryRent`、`Return`、`Dispose` | 普通对象池有容量限制；借出的对象应归还 |

游戏时间暂停与非缩放时间分开。使用 `FrameworkRuntime.Tick` 时，它会根据自己的 `Pauses` 决定是否推进游戏计时。

### 资源、Prefab 与场景

| 类型 | 常用入口 | 释放方式 |
| --- | --- | --- |
| `ResourceService` | `InitializeAsync`、`LoadAsync<T>`、`InstantiateAsync` | 使用结束后释放资产或实例句柄，最终等待 `ShutdownAsync()` |
| `AssetLease<T>` | `Asset` | 使用资产期间保留句柄，最后 `Dispose()` |
| `InstanceLease` | 实例句柄 | 最后 `Dispose()`；外部销毁兜底不能替代主动释放 |
| `PrefabPool` | `PrewarmAsync`、`TryRent`、`Return` | 归还实例，最终等待 `CloseAsync()` |
| `SceneService` | `LoadAsync`、`LoadWithProgressAsync` | 返回的 `SceneLease` 需要等待 `UnloadAsync()` |

资源初始化需要明确的包名和 YooAsset `InitializeParameters`，不能只实例化服务就开始加载。场景服务使用同一个资源服务。

建议关闭顺序：停止新请求 → 取消或等待在途操作 → 关闭 UI、音频和池 → 卸载场景并释放剩余租约 → 等待资源服务关闭。资源服务关闭前必须没有在途操作和未释放租约。

场景进度是下载与加载的组合，`1` 表示成功，不表示预计剩余时间。取消可能需要等待底层操作完成并清理，不承诺立即中断全部 I/O。

切换内容版本前同样要清理旧租约和在途加载。候选版本与回退都失败时，应由游戏展示恢复入口，不继续当作资源已就绪。

### UI、输入和音频

| 类型 | 常用入口 | 责任 |
| --- | --- | --- |
| `UIService` | `OpenAsync`、`Close`、`Tick`、`Dispose` | 用 `Close(PanelHandle)` 关闭窗口；句柄没有 `Dispose()`。缓存需要 `Tick()` 清理 |
| `PanelView` | 层级、遮挡、缓存和选中控件配置 | 用来描述窗口行为，业务逻辑仍由游戏处理 |
| `UIImageBinding` | 图片绑定 | 最后一次请求生效；隐藏时取消旧结果 |
| `LegacyPointerInput` | `Poll`、`Feed`、`Reset` | 旧鼠标/触控入口，在 Unity 主线程调用；关闭或后台切换时重置手势 |
| `AudioService` | `PreloadAsync`、`Unlock`、`TryPlaySfx`、`Tick` | 游戏配置音源根对象和容量；用 `AudioService.Stop(handle)` 停止音效，服务最后 `Dispose()` |

UI 被缓存不等于它仍在显示：显示期订阅要随隐藏结束。输入从 UI 开始被消费时，整次手势都不应穿透到玩法。

音频的静音、暂停和主动停止是不同状态。平台音频解锁和前后台行为需单独验证。

### 配置、存档和 HTTP

| 类型 | 常用入口 | 返回与错误处理 |
| --- | --- | --- |
| `ConfigService` | `LoadAsync<T>(key, validate, cancellation)` | 返回配置 DTO；必须提供校验函数，内部会释放 `TextAsset` 租约 |
| `LocalStore` | `Load<T>(key, schema, migrate)`、`Save<T>(key, schema, value)` | 同步返回 `StoreResult`，检查 `Status` 或 `Success` |
| `HttpService` | `SendAsync`、`SendWithProgressAsync` | 返回 `HttpResponse`，检查 `Outcome`、`StatusCode`、`Body` 或 `Success` |

配置资产是默认数据，不要把运行状态写回 ScriptableObject。需要只读模型时由游戏从 DTO 或配置资产创建快照。

存档通过 A/B 文件和摘要处理损坏、写失败及迁移。未知新 schema 不应直接覆写；schema、迁移规则和账号目录由游戏决定。摘要不是加密，也不能防作弊。

HTTP 服务处理超时、取消、大小限制和 HTTP 状态，不自动解释业务响应、刷新 Token 或重试交易。请求内部会释放，不需要资源租约；取消等待不证明服务端没有执行。

### 显示订阅与本地化

- `DisplayLifetime.Bind` / `Clear` 及绑定流的派发在 Unity 主线程进行。后台数据应先切回主线程。
- 隐藏结束订阅，重开读取当前值，不让旧请求更新已关闭视图。
- `LocalizationService` 管理文本键、格式化和语言切换。
- `LocalizedTextBinding` 绑定文本与字体；字体资源的持有者仍负责保持租约。备用字体应覆盖当前文本，框架不会修改共享字体资产。

## 开发时的检查清单

- 参数和容量按项目需求设置，不照搬样例作为生产预算。
- 所有异步入口都处理失败或取消。
- 切场景和关闭 UI 时确认没有旧订阅或资源句柄残留。
- 不直接修改 `Library/PackageCache/` 中的包源码。
- 没有通过 [支持范围](Support.md) 中的验证，不宣称目标平台已支持。

编辑器工具在包的 `Editor/` 中，负责绑定、资源/场景检查、API 快照和构建。Runtime 不引用 Editor；`Builds/` 是输出目录，不是游戏源码。
