# API 与所有权

## 组合

消费者实例化 FrameworkRuntime，调用 InitializeAsync(FrameworkOptions, CancellationToken)，就绪后每帧传 scaled/unscaled delta 给 Tick，关闭时 Dispose。它只创建计时/暂停技术状态；资源/UI/音频由游戏显式组合，不是服务定位器。

Foundation 无 Unity 依赖：TimerService.Schedule/Cancel、PauseGate.Acquire 返回自己的 IDisposable 令牌、StateMachine<T>、有界 ObjectPool<T>。计时句柄带代次，循环不无限补追，回调有预算。不同暂停持有者只能释放自己的令牌。

## 资源与场景

ResourceService.InitializeAsync 接受明确 YooAsset 初始化参数。LoadAsync<T> 返回 AssetLease<T>；读取 Asset 后仍须保持租约，最后 Dispose。InstantiateAsync 返回 InstanceLease，外部销毁有兜底但不能替代明确释放。PrefabPool 持有 Prefab 租约，PrewarmAsync 有界分帧，Return 不卸载 Prefab，CloseAsync 结束实例后释放资源。

SceneService.LoadAsync / LoadWithProgressAsync 返回 SceneLease。UnloadAsync 完成后才算释放；场景依赖下载和引擎加载的取消采用在途完成/清理语义，不承诺立刻中断全部I/O。进度前半段下载、后半段加载，1仅表示成功；不是预计剩余秒数。游戏自行决定切关前关闭哪个UI和池。

版本检查不切换清单；ActivateVersionAsync 必须无旧租约/加载，候选校验失败回滚并重新验证。双重失败服务不可用，由游戏提供恢复/随包政策。

## 界面、输入与音频

UIService.OpenAsync 返回独立 PanelHandle，Close/Back/CloseAll；调用 Tick 回收缓存。PanelView 描述层级、遮挡、缓存秒数及可选默认选中控件。Focused/FocusChanged 为最高层活动窗口，缓存对象期不等于显示订阅期。覆盖异常记录不遗失句柄，重排有界。UIImageBinding 最后请求优先，隐藏取消旧结果。

LegacyPointerInput.Poll 在旧输入系统读取鼠标/触控，Action 为 Began/Dragged/Tapped/Ended/Cancelled；起始uGUI消费贯穿手势。宿主关闭/后台时 Reset，不自动引入广告策略。输入必须在Unity线程使用。

AudioService 显式预载/解锁，有限SFX声道与优先级；静音、暂停、主动停止各自独立，旧播放句柄不影响新音源。平台解锁/后台差异须另行验收。

## 数据与网络

ConfigService.LoadAsync<T> 加载 TextAsset，JsonCodec 解码受约束 DTO，并调用消费者 validate；租约随后释放。只读模型由消费者投影，框架不定义商品/关卡。SO参数读取值快照，不修改配置资产。

LocalStore.Load/Save(key, schema, value) 使用 A/B 有效快照和 SHA-256；这是损坏检测，不是防作弊或安全加密。缺档/损坏/写失败/新schema返回不同 StoreStatus，未知新格式拒绝覆写。迁移函数和账号隔离由上层提供。

HttpService.SendAsync / SendWithProgressAsync 返回 HttpOutcome、StatusCode、Body。只管技术超时/取消/大小/HTTP状态，不自动重试副作用请求、刷新Token或解释业务响应。取消等待不证明服务端没有执行。

## 显示与本地化

DisplayLifetime.Bind/Clear 及流派发必须Unity主线程；R3源后台值应先显式切回主线程。关闭释放显示期订阅，重开取当前值。LocalizationService 提供键/格式化/切语言，LocalizedTextBinding 维持调用方拥有的字体租约；备用字体完整覆盖文本才选择备用，否则保留TMP缺字处理。Clear 不修改共享字体资产。

Editor工具进行显式绑定生成/Undo、资源/场景审计、API快照与构建输入保护。Runtime不引用Editor。Builds是输出范围，不是源目录。
