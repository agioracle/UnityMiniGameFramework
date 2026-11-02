# UnityMiniGameFramework

**S5-03 通用构建开发切片通过（2026-10-31）**：开始推进S5通用开发，阶段验收与开发启动分开，不再因冷／热首玩暂缓或真实广告缺权限而继续堆叠S4专项。新增统一BuildProfile的plan／prepare／verify／build、Local和微信C#BuildAdapter、独立SDK物理准备、公开环境资源、回执及产物哈希；原业务与主场景设置不改。63项Python（40新＋23SDK安装回归）通过；两份初始无Library工程实际导出：Local Mono、微信IL2CPP，均Release／High／非Development、0错误，BuildReport警告分别41／25，日志保留；Features29份相同。SDK348份原Runtime输入中345份不变，官方导出选择3项WebGL插件标志，另生成36份目录meta，最终384份；精确差异归档，SDK代码／二进制及归档锁不改。主1149份、前次84份可恢复交付和116行AND表保护；Wefund空闲非Play／Console0错0警告，未跑主测试。无新运行时冒烟／IDE依赖闭包认证、无手机／上传／发布；Local不是第二小游戏平台，生产目的暂拒绝，CDN声明未切换样板Builtin资源后端。S5-03整项仍进行，后续推进第二平台／生产预检／不可变发布、回退和缓存保护；原G4／G5／Alpha未接受及冷／热完整首玩暂缓保持。报告Docs/S5-03BuildProfile.md、ADR-058、工具Tools/Build/README.md，机器状态Evidence/S5-03/build-profile-v1/acceptance.json。下文旧S4继续点按历史阅读。

**S4-07 真实场景异常收尾专项通过（2026-10-31）**：新增4项内置场景组合验证，现有SceneService／UnitySceneProvider／SessionFlow及生产装配不改。独立Unity46/46（4新增＋42原场景回归）0失败／跳过；20项合成接收器另计。官方Release／IL2CPP／High非Development构建0错／3条headless光照警告，专用两场景仅在隔离Profile打包。代理自动开发者工具四项通过，run2b576e9de03a4e248c8c5842642c8074（2933字节，SHAa9512333…）：延迟激活关闭等待，取消观察后实际激活／卸载且保留旧会话，旧卸载拒绝后新会话保持并显式重试，同路径两实例句柄隔离；终态模块／租约／原生句柄／Flow Scope／测试场景0且Bootstrap保留。取消和卸载入口拒绝明确受控注入，原生AsyncOperation未被取消，清理中OnEnable不等于业务Ready。主1136份、SDK384份、YooAsset2070份、前次76份可恢复交付及原116行AND表保护；主Wefund空闲非Play、源码Hash匹配、Console0错0警告，主测试未执行。宿主0错10警告快照不认证零诊断／性能；工具准备两次失败日志及首轮仅4项结果保留，最终完整46项通过。无需手机，不清缓存或上传；AssetBundle场景、原Gates／Alpha保持未认证，冷／热完整首玩继续暂缓。报告Docs/S4-07SceneRecoveryValidation.md、ADR-057、机器状态Evidence/S4-07/scene-recovery-csharp-v1/acceptance.json。下文旧继续点按历史阅读。

**S4-07 AccountScoped／微信文件AOT专项通过（2026-10-31）**：新增4项可选账户组合验证，复用AccountService／OwnershipPolicy／StorageService／WeChatStorageProvider，身份明确Fake／Simulated，真实文件通过官方C#SDK；默认LocalOnly和生产服务不改。独立61/61 Unity回归（4新增＋53Ownership＋4RestartRecovery）0失败／跳过，20项合成接收器另计；官方Release／IL2CPP／High非Development构建0错误／1条headless光照警告。代理自动开发者工具两份完整4用例严格核验通过，最终run54638a7700854e599b9cfab7f1f962fe（4029字节，SHA45e5b669…）：A迟到写入固定原Namespace、B摘要不变，返回A恢复11且代次1→2→3，关闭等待退休A实际写入；取消后迟到身份不发布；默认政策拒绝Simulated且身份／写入0。所有终态模块／文件会话／存储操作／账户操作／活动账户Scope0。暂缓实际写入和身份返回是明确测试注入，不认证物理慢盘／后台事件。两份运行随机Slot不同、摘要不作跨运行配对；不认证正常重启、真实认证服务器／云同步／持久代次源。主1125份既有文件、SDK384份、YooAsset2070份、前轮112份可恢复交付及原116行AND表保护；主Wefund刷新断连后恢复，最终源码Hash匹配、空闲非Play、Console0错0警告，主测试未执行。宿主最终诊断快照0错13警告，未逐条归因，不认证零诊断／性能。无需手机、未清缓存／关闭域名校验／上传；原Gates／Alpha保持、冷／热完整首玩继续暂缓。报告Docs/S4-07AccountStorageValidation.md、接入Docs/OwnershipModeGuide.md、决定ADR-056、机器状态Evidence/S4-07/account-storage-csharp-v1/acceptance.json。后续仍按原依赖图推进可开发项；真实身份后端与真实广告配置权限独立待验，下文旧继续点按历史阅读。

**S4-07 YooAsset故障回退／准备期关闭专项通过（2026-10-31）**：在正式包后端上新增4项真实SDK集成验证，生产Core／Provider／SDK不改。最终v2隔离Unity回归91/91（4新增＋87既有）、0失败／跳过；20项合成接收器另计。官方Release／IL2CPP／High非Development构建0错／1条headless光照警告。代理自动开发者工具两份完整四用例回执严格通过，最终run1d5a24c98b77454799c046ec57be6b01（4664字节，SHA41136fcb…）：真实初始化／清单失败先清理再回退，全部失败旧Context排空后新Context恢复；真实准备后的受控回执延迟期间关闭保持等待、拒绝Ready、消费者0，终态模块／Session／句柄／条目／包及Bundle均0。v1缺失候选意外成功、未实际触发故障的报告拒绝并保留；v2把故意缺失路径移至同域名SDK缓存匹配前缀外，记录候选根并检查C#选项根一致，未清缓存或改SDK，完整路径机制根因未认证。两次夹具失败XML与旧源码、v1构建／原文保持；主1114项、SDK384项、YooAsset2070项、前轮111项可恢复交付和原116行AND表保护。主Wefund刷新短暂断连后恢复，最终源码Hash匹配、空闲非Play、Console快照0错0警告；主测试未执行。宿主报告时10错10警告（包含故意失败请求，未逐条归因），不认证零诊断／性能。无需手机；本轮不是物理断网／正常重启或S5-03生产CDN发行系统，原Gates／Alpha保持，冷／热完整首玩继续暂缓。最新报告Docs/S4-07YooPackageRecoveryValidation.md、ADR-055、接入Docs/YooAssetPackageStartupGuide.md，机器状态Evidence/S4-07/yoo-package-recovery-v1/acceptance.json。下一步按原依赖图推进其余可开发项；下文旧继续点按历史阅读。

**S4-07 YooAsset固定包启动后端专项通过（2026-10-31）**：新增正式YooAssetPackageDefinition／Backend／Driver／Provider四份生产文件，复用PackageStartup及原YooAssetProvider，提供精确候选／固定版本、带类型首玩实际读取、Ready后延迟绑定、已有包所有权保护、关闭停止接单及保留句柄／显式清理重试。主1093份既有文件、SDK Runtime384份、前轮62份可恢复交付和原116行AND任务表保护。补丁后最终87/87 Unity回归通过（21新增＋66既有），0失败／跳过；报告DTO修订前后生产与测试源码完全一致，未重复计数。16项合成接收器另计，六文件HTTPS只读预检200／长度／SHA全匹配。最终v2官方Release/IL2CPP/High非Development构建0错误／1条headless光照探针警告。代理在开发者工具自动取得两份同构建不同runId的完整五用例报告，分项一致；最终run69509ab46e7b4769a892c168e1066200（2450字节，SHA9f965e20…）严格核验：20租约共用一次业务加载，三类拒绝消费者0，外部包保持原Owner，终态模块／Session／句柄／条目／包0与Bundle恢复0基线。最初v1顶层passed但缺cases被拒绝，原文／构建完整保留；v2改报告内DTO数组、类型名与显式保留后明细完整，不认证遗漏的通用根因。Wefund刷新后曾断连，最终恢复、最终源码Hash匹配、刷新后空闲非Play／Console快照0错0警告；主测试未执行，不代替隔离XML。无需手机；CompatibleCache显式不支持，生产CDN更新／发布／回退仍属S5-03，原Gates／Alpha不放行，冷／热完整首玩继续暂缓。当前报告Docs/S4-07YooPackageBackendValidation.md、接入Docs/YooAssetPackageStartupGuide.md、决定ADR-054，机器状态Evidence/S4-07/yoo-package-backend-v1/acceptance.json。下文旧继续点按历史阅读。

**S4-07 已发布真实AssetBundle专项宿主通过（2026-10-31）**：用户提供COS六文件目录后，只读HTTPS预检全部200、长度／SHA与原WebGL产物一致；新的独立v2 Profile显式指定HTTPS根，官方C#导出增加精确缓存路径标识，HTTP仍由YooAsset的UnityWebRequest后端处理，生产源与SDK运行时代码不改。官方Release/IL2CPP/High非Development导出0错0警告；16项新合成接收器测试通过，原145项Unity结果仅经对应源码完全一致核验复用、本轮未重跑。代理自动导入开发者工具、保留严格域名校验及缓存，实际弹窗run f780fa7e2dab49f28c1d25e27cbc6328（2886字节，SHA f33808e1…）四用例通过：真实首玩加载后Ready、20租约共享一次业务加载；缺失首玩／配置不匹配／文本不匹配拒绝消费者；终态模块／会话／句柄／条目／包0，Unity观察Bundle恢复0基线。初次Unity加载后模拟器无响应提示已封存；关闭提示并Escape后观察SDK上下文重新初始化并成功，未在提示后再次Compile，单次成功不认证异常根因或启动稳定性。报告时诊断快照0错14警告，含SDK／图形提示，不认证零诊断或性能。主1093项、前轮138项可恢复交付、原116行AND表保护；无需手机，本轮不认证生产远端内容系统／发行流程／物理内存下降／Alpha，冷／热完整首玩继续暂缓。最新见Docs/S4-07PublishedBundleValidation.md、ADR-053与Evidence/S4-07/real-bundle-route-csharp-v2/acceptance.json。下文旧待目录／待验状态按历史阅读。

**S4-07 真实AssetBundle宿主已接续，资源根地址待提供（2026-10-31）**：用户确认解锁后，代理实际读取开发者工具、正常关闭旧项目并自动导入RealBundle-v1，原锁屏已解除。冻结v1实际回执run926287ba99b24440a266b1905729622b（1211字节／SHA b3f68ce1…）明确failed：SDK nativeReady=true，但相对StreamingAssets/S407RealBundle/BuiltinCatalog.bytes落到默认game.weixin.qq.com，被request合法域名检查拦截；UWR500对应此SDK拒绝，不能据此称CDN服务器500。消费者／租约0，模块／会话／句柄／条目／包／Bundle归零，严格接收器拒绝计成功。原文、实际AX／截图和失败接收回执已封存；未清缓存、关闭校验或改SDK／生产代码，现有static域名登记仍有效，所缺为本包六文件的真实HTTPS位置。已提供六文件zip并请求已发布目录根地址，非请求放行默认域名或手机采集。145项Unity、12项合成接收器及Release/IL2CPP/High导出通过保持，本轮未重复测试；Wefund只读状态恢复为空闲，不补计主测试。此前102项交付先核验并保存可变原文before-unlock-resume，原116行AND/Gates/Alpha与冷／热完整首玩暂缓保持。最新以Evidence/S4-07/real-bundle-csharp-v1/acceptance.json、验证报告宿主接续段为准。

**S4-07 真实AssetBundle通用验证／官方构建通过，宿主待验（2026-10-31）**：新增真实StandaloneOSX与WebGL资源包各6文件；4项新用例＋141项关联PlayMode共145/145通过、0失败／跳过，12项合成接收器另计。实际YooAsset加载后才Ready；20租约共用一次业务加载，首玩缺失／配置不兼容／文本不匹配均消费者0，关闭后模块／条目／句柄／包与已加载Bundle数恢复基线。官方Release/IL2CPP/High非Development导出0错／1条headless警告，WASM/Brotli及严格域名Host准备通过。CUA明确Mac锁屏，已请求解锁，未导入／运行新宿主或生成手机码；静态还确认StreamingAssets仅在webgl导出目录、minigame无同名资源且CDN为空，实际资源路由未认证，不将构建当宿主可读。主1082项、WX Runtime384项、YooAsset源2070项、前轮86项和原116行AND表保护通过；自有YooAsset仅2项Editor .meta由Unity补齐导入字段，差异归档。主Wefund刷新后传输中断，未计主测试；生产服务／主场景不改，新后端仅验证夹具。无需真机，冷／热完整首玩继续暂缓，真实广告与原Gates/Alpha保持。最新见Docs/S4-07RealBundleValidation.md、ADR-052与Evidence/S4-07/real-bundle-csharp-v1/acceptance.json。

**S4-07 CDN／WX文件缓存／内容启动专项通过（2026-10-31）**：独立微信C# SDK Profile新增4项协议测试＋111项关联回归共115/115通过，12项合成接收器另计；官方Release／IL2CPP／High非Development导出通过（0错误／1条headless光照探针警告）。代理在开发者工具自动完成真实CDN下载、WX文件长度／SHA独立校验后消费者才启动，并正常关闭项目重开；新runId／launchId严格配对，2,505,648字节原缓存恢复通过。注入网络不可用后先清理远端会话再选兼容缓存；缺失／独立坏副本／不兼容合成catalog均拒绝Ready、消费者0，原缓存保持，坏副本与.part残留0，全部终态计数0。首轮SDK因request合法域名拦截而失败，原文完整保留；项目设置实际列出已配置CDN，点击域名刷新并官方Compile同冻结包后成功，不认证工具缓存机制根因；未清缓存或关闭校验、未改SDK／载荷。主1071项、SDK384项、前轮85项交付和原116行AND表保护通过。新后端仅验证夹具，不是生产CDN／AssetBundle Provider；未加载模型、不认证物理断网或手机。此专项无需真机配合，冷／热完整首玩继续暂缓，真实广告及原Gates/Alpha保持。最新见Docs/S4-07ContentCacheValidation.md、ADR-051和Evidence/S4-07/content-cache-csharp-v1/acceptance.json。

**S4-07 跨运行时奖励恢复专项通过（2026-10-31）**：独立微信C# SDK Profile新增4项协议测试及85项关联回归共89/89通过，12项合成接收器测试另计；官方Release／IL2CPP／High非Development导出通过（0错误／1条headless光照探针警告）。代理在微信开发者工具自动完成三次运行、两次正常关闭项目重开，无用户操作或手机扫码。三个不同runId／launchId与同一chain的前次摘要／序列4→8→9、Owner代次1→2→3严格配对：真实Pending写入后故障注入丢失回执，0金币且不启动广告；新运行时无完成证明保持Pending且不发奖，显式Abandon后10次Mock完成只授予1个Granted／5金币；第三轮恢复5金币、原Granted代次2、不重展／重复发奖，终态模块／会话等均0。主1055项、SDK384项、前轮350项交付和原116行AND表保护通过，生产服务／场景／SDK不改。新夹具、报告及ADR-050见Docs/S4-07RestartRecoveryValidation.md，机器结果Evidence/S4-07/restart-recovery-csharp-v1/acceptance.json。本轮仅Devtools正常关闭重开及受控故障组合，不认证手机、强杀／断电、真实广告或零诊断／性能；此专项无需追加手机验证，后续有设备独立必要性再安排。主Wefund刷新后传输中断，无主测试终态不计通过；冷／热完整首玩继续暂缓，原Gates/Alpha保持。

**S4-07 iPhone生命周期组合短验通过（2026-10-31）**：原iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5用户回传run53b96e8c442f41079489a383f931d3a2，2279字节／SHA00405ff9…；严格接收器核验9阶段、两轮官方Hidden/Shown、后台门控零副作用、无完成事实不发奖、关闭等待、10次Mock完成仅Claim1／5金币、WX文件同进程重读及全部终态计数0通过。与已验宿主独立run的阶段／事件序号和语义计数一致，运行Generation各自独立。手机为用户操作回传，非代理独立观察，未另附两轮手势文字；原生报告完整记录两轮，不因此要求重采。原文／来源／接收回执在phone/run1，宿主对照host-phone-comparison.json。本专项验证完成，无需重扫或关闭重开；89项Unity回归、9项合成接收器及官方SDK构建保持，336项前轮交付可从当前或before-phone-receipt恢复。生产源码／SDK／主1037项与原116行AND表保持；真实广告、后台持续执行、正常小游戏重启／强杀／断电与原Gates/Alpha未认证，冷／热完整首玩继续暂缓。最新见Docs/S4-07HostLifecycleValidation.md和本轮acceptance.json。

**S4-07 真实宿主生命周期组合通过（2026-10-31）**：用户回传devtools run326dac185919400fb8f5564ae1e85e6b，2280字节／SHA6dc3e9a3…；严格接收器核验9阶段、两轮Hidden/Shown及C#代际序号、后台零写入／拒绝广告、无事实不发奖、关闭等待、10次Mock完成只提交1个Claim／5金币、WX文件同进程重读、监听解绑及终态归零全部通过。代理从实际报告弹窗AX读回原文并逐字节匹配、截图已保存；两轮操作由用户完成，非代理独立操作，尚无另附两轮手势文字，不追加确认阻断已有报告接收。SDK诊断计数1错／69警告保留，未逐条归因，不认证零诊断或性能。89项回归及9项合成接收器和SDK构建保持；316项前轮交付已核验并存可变原文before-host-receipt，主1037项／SDK384项／原116行表保持。手机待验，新预览已生成（14440KB、10月31日00:36前有效），已请求原iPhone13两轮桌面往返及完整报告；旧无响应与审批容量故障保留，当前单次成功不证明通用根因修复。真实广告／后台持续执行／正常小游戏重启／Alpha均未认证，冷／热完整首玩继续暂缓。见Docs/S4-07HostLifecycleValidation.md和本轮acceptance.json。

**S4-07 首轮步骤已观察（2026-10-31）**：用户手动编译后回复步骤已出现；CUA实际读取正确HostLifecycle-v1窗口、当前SDK运行页与第1轮Lifecycle Step弹窗。本次冻结构建已进入夹具，旧启动无响应不再阻止本次进入，但根因与重复启动稳定性未认证。首次OK索引因AX树变化失效，刷新后点击尚未执行即再次被自动审批服务Selected model is at capacity阻断；已请求用户在开发者工具手动完成两轮OK／Actions Home约5秒／Return并回传完整报告。代理实际切换0轮，尚无终态报告／新手机码，当前仍host_blocked_approval_review_unavailable，不将首轮步骤当九阶段通过。304项前轮交付已核验并存可变原文before-host-step-resume；生产源、SDK与原任务表保持，冷／热完整首玩继续暂缓。

**S4-07 手动重开接续（2026-10-31）**：用户确认完全退出重开。CUA首次显示旧RealPool项目，已通过项目列表按完整名称选择HostLifecycle-v1，实际AX／截图确认本轮目录、普通编译入口及about:blank模拟器，界面已完成加载。随后Compile操作尚未执行即被自动审批服务容量错误阻断，重试只读窗口同样未执行，原因Selected model is at capacity，并非安全拒绝；未绕过审批，已请求用户在本项目手动编译一次并反馈Step／具体错误。当前host_blocked_approval_review_unavailable，仍无本轮步骤／报告／生命周期轮次／新手机码／手机回执。89项Unity回归、9项合成接收器、官方Release/IL2CPP/High导出及源码保护通过保持；前轮293项交付先核验并将可变原文存before-manual-restart。主工程与SDK未改、缓存和校验保持、冷／热完整首玩继续暂缓，原Gates/Alpha不放行。详见Docs/S4-07HostLifecycleValidation.md及本轮acceptance.json。

**S4-07 宿主解锁接续（2026-10-30）**：用户已确认解锁，CUA实际读取开发者工具并将严格域名HostBabel目录导入独立项目MiniFramework-S407-HostLifecycle-v1；原Mac锁屏阻塞已解除。关联89项回归、9项合成接收器及官方SDK构建证据保持。宿主首次启动明确提示Simulator does not response for a long time并进入chrome-error页；普通编译、项目重载／重开后仍未取得步骤，正常退出及Cmd-Q未能确认完成，最终CUA读取timeoutReached。已请求用户手动完全退出重开；当前host_blocked_devtools_unresponsive，根因未确认，尚无9阶段报告／新手机码／手机回执，不计实际前后台组合通过；urlCheck／ES6／增强编译保持。旧273项交付核验并将可变原文存于before-host-resume；主1037项、SDK384项、前轮190项及原116行AND表保持。冷／热完整首玩继续暂缓、真实广告权限阻塞、原Gates/Alpha不放行。最新以Docs/Evidence/S4-07/host-lifecycle-csharp-v1/acceptance.json为准。

**S4-07 真实前后台组合接续（2026-10-30）**：新增独立验证夹具，官方C#生命周期来源与Mock广告能力显式分离，组合描述符保持Mock；两轮Hidden/Shown分别核对后台零写入／零广告、返回前台无事实不发奖，再验证在途关闭屏障、10次Mock完成只提交1个Claim／5金币、WX文件同进程重读和监听排空。独立微信Profile关联PlayMode89/89通过（4新增、85既有），9项合成接收器另计；官方Release／IL2CPP／High非Development导出、WASM/Brotli和严格域名Host清单通过（0错／1条headless探针警告）；Mac锁屏阻塞宿主操作，已请求解锁，宿主／手机尚未执行，未生成新码。首轮准备时序、程序集引用、槽位／预算及夹具门控断言失败均保留，生产实现未改。主Wefund初始读取空闲，后续读取／refresh未返回，不计主测试通过。主1037项既有文件、SDK Runtime384项、前轮190项交付及原52项AND表116行保持。报告 Docs/S4-07HostLifecycleValidation.md，决定ADR-049，最新状态 Docs/Evidence/S4-07/host-lifecycle-csharp-v1/acceptance.json；真实广告权限阻塞、冷／热完整首玩继续暂缓，原Gates/Alpha保持。

**KVCache 微信SDK／AOT接续（2026-10-30）**：独立官方C# SDK Profile中32项EditMode＋1项PlayMode通过，7项合成报告接收器测试另计；Release／IL2CPP／High非Development官方导出、WASM/Brotli一致性校验通过，构建0错/22条警告（既有CS4014重复记录及headless空场景探针警告保留）。主工程1037项既有文件、SDK Runtime384项及前轮102项交付保护通过，原52项AND表116行不变。新包仅通过Unity PlayerPrefs访问两个专用KV测试命名空间，验证11阶段及前次marker/实际值配对，Flush仍不代表持久确认；开发者工具两轮11阶段及正常关闭项目重开配对通过，Storage面板观察8个实际Key／524B；宿主启动等待错误和后续SDK上报提示保留，不认证零诊断或性能。iPhone13正常关闭重进两轮11阶段已严格配对（6d0af515…→fa62de6a…），前次marker与实际类型／删除／命名空间值恢复通过，7→3个合并写入Key符合预期；用户确认正常关闭重进，非代理独立观察。第二份1328字节／SHA82eb4e36…已归档phone/run2；本项短验完成，无需重扫，强杀／断电／容量耗尽及其他平台仍未认证。主Wefund无返回不计主测试。当前报告 Docs/KVCacheWeChatValidation.md，最新状态 Docs/Evidence/PlayerPrefsKv/wechat-sdk-v1/acceptance.json。冷／热完整首玩继续暂缓，原Gates/Alpha保持。

**KVCache 命名调整（2026-10-30）**：按用户要求，将本轮轻量 KV 模块目录、程序集及命名空间统一为 KVCache，测试与工具路径同步；Public 类型名、PlayerPrefs Key 前缀、值格式与行为不变，Unity .meta/GUID保留。重命名后独立Unity工程32项EditMode＋1项PlayMode通过，9个源码/asmdef仅命名替换、14份.meta字节不变；主Wefund刷新被接受但重载后传输失败，无主测试终态不计通过。验收见 Docs/Evidence/PlayerPrefsKv/rename-kvcache-v1/acceptance.json。此前 Preferences 名称和 v1 测试证据按历史阅读，平台验证边界与冷／热首玩暂缓保持。

**轻量 KV 缓存新增（2026-10-30，按用户新授权）**：新增 IKvCache、PlayerPrefsKvCache 和 UnityPlayerPrefsCache，生产底层统一调用 Unity PlayerPrefs；C# 读缓存、同 Key 写入合并、命名空间和类型隔离、显式 Flush、失败保留重试。该接口不是文件 StorageService 后端，不承诺多 Key 原子性或平台持久提交确认，不提供全局 DeleteAll、云同步或旧 Key 自动迁移。最终源码在独立 Unity 2022.3.62f3 工程32项EditMode＋1项PlayMode通过，包含真实PlayerPrefs读写；主Wefund刷新后连接反复中断，无主测试终态不计通过。验收见 Docs/Evidence/PlayerPrefsKv/v1/acceptance.json；使用方式见 Docs/PlayerPrefsKvGuide.md，决定见 ADR-048。此前“PlayerPrefs未实现”按历史快照阅读；既有文件存档和52项AND图/Gates保持，冷／热完整首玩继续暂缓。

**S4-07 真实对象池 iPhone 受控短验通过（2026-10-30）**：用户回传 iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5，run `6914b478ca684df8975ad4ac2099910e`；2744字节/SHA3362e30c…，严格接收器核验三用例和全部终态计数通过。18个真实实例销毁、每帧最多2个，活动租用保护、预取恢复、关闭屏障、迟到围栏通过；与宿主独立run的用例/计数完全一致。On/Off各3、受控信号6/迟到3，SDK真实告警0；仅用户操作回传，非代理独立观察。原文/回执/来源/宿主对照见 Evidence/S4-07/real-pool-csharp-v1/phone/run1/，本轮无需重复扫码或关闭重开。303项Unity、6项接收器、SDK构建及宿主证据保持；设备回执前144项交付已保护，986既有工程文件/384 SDK Runtime不变。仅接受本轮受控验证，不认证OS真实告警、资源包卸载、进程内存下降或Alpha；原52项AND/Gates保持、PlayerPrefs未实现、冷/热完整首玩继续暂缓。见 [验证报告](../Docs/S4-07RealPoolValidation.md)。以下此前手机待验文字按历史阅读。

**S4-07 真实对象池宿主短验通过（2026-10-30）**：用户解锁后，本轮独立微信C#构建在开发者工具普通编译运行，run `28c5c8e5deff4bdb986958b9b169d073`，2745字节/SHA6c7aff22…；严格接收器核验三用例通过、18个真实实例销毁、每帧最多2个、活动保护/预取恢复/关闭屏障及终态全部归零。On/Off各3、受控信号6/迟到3，真实SDK告警0。诊断快照2错/53警告保留，其中已展开错误为SDK启动上报提示，未逐条归因全部记录，不认证零诊断或性能。303项Unity与6项接收器测试保持，986既有工程文件/384 SDK Runtime不变，原52项AND/Gates不放行；解锁前122项交付已保护。本包14,431KB新预览码已显示（10月30日19:34前有效），已请求原iPhone13短验，手机仍待回执；冷/热完整首玩继续暂缓、PlayerPrefs未实现。详见 [验证报告](../Docs/S4-07RealPoolValidation.md) 与 Evidence/S4-07/real-pool-csharp-v1/host/run1/。

**S4-07 真实对象池组合验证（2026-10-30）**：新增独立夹具，将既有平台内存源、MemoryPressureController、RuntimeHostModule、ResourceWorkScheduler、AssetsService、GameObjectPool 装入同一 GameContext。176 EditMode＋124 既有 PlayMode＋3 新增组合用例共303项通过，6项报告接收器反例另计；18个真实实例实际 OnDestroy、每帧最多2个，活动租用保护、预取恢复、关闭等待及各项账本归零通过。首轮夹具未激活实例造成 OnDestroy 计数0的失败 XML 保留，生产API与实现未改。Resources 源预制体仍由Unity管理，不认证资源包卸载、进程内存下降或真实OS告警。独立微信C# SDK Release/IL2CPP/High非Development构建与WASM/Brotli核验通过（0错/1条headless光照警告），严格域名Host及104个官方纯Babel helper已准备；Mac在导入前锁屏，已请求解锁，本包宿主/手机尚未执行、未生成新码。具体状态以 [验证报告](../Docs/S4-07RealPoolValidation.md) 与 Evidence/S4-07/real-pool-csharp-v1/acceptance.json 为准；主Wefund刷新后连接中断，无主测试终态不计通过。旧397项交付可从当前原文或before快照恢复；52项AND/G0-P1/G3-AOT/G4/Alpha保持，PlayerPrefs未实现、冷/热完整首玩继续暂缓。

**S4-07 内存告警 v4 手机受控短验通过（2026-10-30）**：原iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5回传run `06a4647b098545b782814f16768d4bc3`，989字节/SHA `8a3e9b625dce1988995da581c01fd8a2609579ba024ad2fa480287fd23d14eb5`，严格接收器核对六阶段通过、On/Off各1、受控信号3/迟到1、裁剪1/合并2及终态5项0。原文/回执/用户来源见 `Evidence/S4-07/memory-csharp-v1/babel-startup/phone-v4/run1/`；打包修订materialized-helpers-v4沿用冻结WASM buildId，修订归属依据用户对v4扫码请求的回传，非代理独立观察。宿主独立run同样通过；本次运行未复现原Babel启动失败，无需重复扫码。仅接受该环境本次启动及受控内存链路；SDK真实告警回调0，真实OS低内存派发/真实资源释放量、旧机兼容和重复启动未认证，具体编译器漏包机制仍未确认。此前379项交付可从当前文件或before-device-receipt恢复，原v1/v2/v3失败保持。原52项AND/G0-P1/G3-AOT/G4/Alpha不变；PlayerPrefs未实现，冷/热完整首玩继续暂缓。见[验证报告](../Docs/S4-07MemoryPressureValidation.md)。以下待手机回执内容按历史阅读。

**S4-07 手机启动依赖修复候选（2026-10-30）**：手机黑屏错误为 `@babel/runtime/helpers/objectSpread2` 模块未定义，原失败与126项交付已保存。开发者工具独立复现并定位到编译后weapp-adapter.js:4。改outputPath(v2)仍缺依赖、关闭ES5转译(v3)出现import/export语法错误，两者均拒绝且不发给设备。当前v4保留原ES6/增强转译及严格域名校验，显式包含当前官方工具Babel 7.21.0纯helper与许可证，并跳过helper二次转译；同一冻结WASM/C#/SDK JS不变，67个非配置导出文件哈希与104个helper依赖闭包核验通过。本地Node语义检查不计宿主/手机。v4宿主六阶段受控报告已严格核验通过（run bcbabd2e…，990字节/SHA434595e8…），终态5项0，SDK诊断0错/28警告，非零诊断认证；新预览14415KB/335编译文件、18:16前有效，已请求原iPhone13回传。手机仍待验，旧启动失败保留，未认证通用打包根因、修复成功或旧机兼容；原52项AND/Gates和暂缓项保持。见[本轮报告](../Docs/S4-07MemoryPressureValidation.md)及 `Evidence/S4-07/memory-csharp-v1/babel-startup/`。

**S4-07 微信 C# 内存告警接续（2026-10-30）**：新增可选 MemoryWarning 能力、官方 WX.OnMemoryWarning/OffMemoryWarning 绑定及 PlatformMemoryPressureSource，显式接入已有 MemoryPressureController 的合并、预取抑制和分帧闲置资源裁剪；共享平台顺序/代际与主线程邮箱，不改变前后台可见性。独立 SDK Profile 最终 176 EditMode＋124 PlayMode 共300项通过（新增27），报告接收器4项另计；官方SDK Release/IL2CPP/High非Development导出与WASM/Brotli校验通过（0错/26项已分类警告），宿主六阶段受控短验通过（run321f5d1b…，SDK诊断3错/46警告单独保留），手机待验；详见[本轮报告](../Docs/S4-07MemoryPressureValidation.md)和 Evidence/S4-07/memory-csharp-v1/acceptance.json。主Wefund显式刷新最终源码后9 Edit＋11 Play补测通过（RunFinished XML），Play作业状态在域重载后unknown_after_reload，未当作作业成功；11条CS4014来自未改动基线文件，旧程序集失败及传输/超时记录保留；首轮构建配置支撑文件缺失及测试关闭时序失败均留档。短验只用受控信号/裁剪目标，真实系统低内存回调和真实资源释放量未认证。原52项AND依赖/G0-P1/G3-AOT/G4/Alpha保持，PlayerPrefs未实现、冷/热完整首玩继续暂缓；旧构建与手机证据不覆盖。

**S3-08 C# SDK 六项 AOT 手机短验已通过（2026-10-30）**：收到同构建 s308-csharp-aot-v1-20261030 的 iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5 原文，runId=ab89c5e52a364779837f96b7dbe71b01；严格接收器核对六项通过、35 coins/5 transitions/关闭六项计数0。865字节/SHA ab62e3ae…已归档phone/run1，用户操作回传、非代理独立观察；与宿主独立runId同构建结果一致，无需重复扫码或关闭重开。19项Unity、23+4项Python及SDK Release/IL2CPP/High非Development构建证据保持；主1245项、同步545项、SDK Runtime384项与前次交付保护不变。宿主SDK启动自定义上报/GL/监控提示保留，不认证零诊断或性能。最新[报告](../Docs/S3-08CSharpAotValidation.md)和Evidence/S3-08/csharp-aot-v1/acceptance.json；收到回执前54项交付及待验文档另存before-device-receipt。原S3-08/G0-P1/G3-AOT/G4/Alpha未整体接受、原52项AND不变；真实广告仍缺ID/权限，冷/热完整首玩暂缓、PlayerPrefs未实现。下文旧待回传状态按历史阅读。

**资源预算修复接续（2026-10-30）**：已确认并修正固定微信C# SDK单文件StatSync返回stats=null导致下载Seal失败的问题；改用递归父目录结果精确匹配文件。修复前回归复现，修复后423项相关测试通过；SDK v2构建与严格域名宿主短验已通过，当前修复版用户回传iOS短验也已通过（128KiB存档同进程重读、2.5MB单次下载/磁盘SHA及清理归零）；性能/跨重启待验，详情以[资源预算报告](../Docs/ResourceBudgetsReport.md)及Evidence/S4-Limits/acceptance.json为准。旧411项/v1产物保留，不提前实现PlayerPrefs或恢复冷/热完整首玩。

**最新进展：资源预算与增量文件下载（2026-10-30）**。按用户授权调整S4-07/S4-09：HTTP保留原默认响应1MiB/完整缓冲实现范围16MiB，增加活动响应容量预留预算；微信事务存档由统一WeChatStorageOptions配置，默认64KiB保持兼容、显式可提高至全容器缓冲实现范围16MiB，端口/Provider/服务限额匹配。新增CaptureDownload及C# UnityWebRequest增量写文件、长度/SHA验证、暂存发布和清理；提供本地与官方WX C# SDK文件端口，无直接平台HTTP或自有jslib。已完成隔离回归，最终计数及SDK/宿主状态以Docs/ResourceBudgetsReport.md和Evidence/S4-Limits/acceptance.json为准。旧格式/双槽/修复及旧SDK/手机证据保持；平台配额不等于单次预算，不声称SDK内部恒定内存或大存档真机性能。主Wefund刷新/最终只读快照有超时，具体状态见main-final.json，不以超时计通过。52项AND/G3-AOT/G4/Alpha不放行；PlayerPrefs未提前实现、冷/热完整首玩继续暂缓、预算未冻结。下文旧限额最大值按历史实现范围阅读。

**最新进展：S4-07 UnityWebRequest v2 iPhone回执通过（2026-10-30）**。原iPhone 13回传runId=971dadedb91b4650ba274271a96d592e，生产HttpClientService请求及raw Script/Buffer三路径均200、2,505,648字节、SHA-256匹配ab6679…；生产仅1次请求，raw nativeResult均Success、error为空，实际终结/Dispose及关闭后全部保留计数归零。完整JSON/严格接收回执/用户来源已归档至Evidence/S4-07/remote-https/ios-500/diagnostic-v2/phone/run1/。这证明当前冻结v2构建在该次真机运行成功；未复现v1手机500，不能认定根因已找到或生产修复已完成。宿主两轮中一次Script摘要异常、一次三路径通过仍保留；调查整体blocked，仅删除“v2手机未回传”阻塞。HTTP保持C# UnityWebRequest，不换平台HTTP Provider；不增加重试、清缓存或要求本轮重复扫码。主生产1229项/v1冻结89产物和52项AND依赖保持，G3-AOT/G4/Alpha不放行；PlayerPrefs不提前实现、冷/热完整首玩暂缓、预算未冻结。下文旧待回传状态按历史阅读。

**最新进展：S4-07 UnityWebRequest诊断v2（2026-10-30）**。用户明确HTTP统一走C# UnityWebRequest，由小游戏SDK转换；当前生产和诊断均遵循，无直接平台HTTP Provider或自有jslib。v1手机500/0字节/3次框架尝试的失败原文已严格接收，关闭4项0；同机WiFi无VPN/Safari可下载，同期curl200。SDK HandleError存在合成500机制，但原手机根因未确认。新独立v2官方SDK Release/IL2CPP/High构建通过（0错/1条headless光照警告），6项接收器合成测试通过。v2严格域名校验的实际宿主2轮均完成诊断并清理：首轮生产+raw Script为200/同长度但SHA2553aa…不匹配，Buffer摘要正确；同构建正常Compile复验三条路径摘要全正确。故已观察到偶发完整性异常，不能把Script断言为确定根因、不能计下载稳定通过；两个原文都保留。最新Docs/S4-07HttpsIosDiagnostic.md及Evidence/S4-07/remote-https/ios-500/。主1229项生产文件/v1冻结89产物不改，当前准备v2手机短诊断，未接受生产修复、平台Gate或Alpha。52项AND/G3-AOT/G4不变；PlayerPrefs未实现、冷/热完整首玩继续暂缓、预算未冻结。下文旧状态按历史阅读。

**最新进展：S4-07 iPhone HTTPS失败已接收（2026-10-30）**。v1真机runId=cfcd92ed15d64f6aa8541929ba5a4330，iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5，statusCode500、0字节、3次请求、network.http.500、passed=false；关闭4项计数0，原文及严格接收回执在remote-https/corrected-v1/phone/run1，空载荷SHA不是模型摘要。用户补充同一WiFi、无VPN、Safari能下载；同期桌面curl仍200且原摘要一致。发现SDK转换产物HandleError会合成500并携带native error/code，v1未保留Unity原始result/error；不能据此断言CDN真实500。生产UnityHttpTransport在非2xx情形可能优先把该状态当HTTP响应，此处尚不修复或放宽重试。已在新独立v2 Profile准备纯C#诊断：保留生产GET，加raw DownloadHandlerScript与DownloadHandlerBuffer对照，记录native result/error；仅诊断，非Provider替换、不改SDK。最新调查Evidence/S4-07/remote-https/ios-500/；v1 SDK/宿主200仍属历史通过，手机明确失败而非待首次验证。主工程1229项、冻结包、52项AND/G3-AOT/G4/Alpha不变；PlayerPrefs未实现、冷/热完整首玩继续暂缓、预算未冻结。下文旧待回传状态按历史阅读。

**最新进展：S4-07 更正HTTPS地址与C# SDK短验（2026-10-30）**。用户更正地址为 `https://static.mini.iaa.itddo2page.com/mzcb/ai-bot-models/model_tier1.sentis`，并确认当前AppID合法域名已配置。curl、独立Unity C# GET和开启URL校验的开发者工具宿主均成功下载2,505,648字节，SHA-256均为ab6679f8b5c1472f32da792adea872ad7673bc685851ae98adb77890c69140f4。Unity短验1/1及6项接收器合成测试通过。新单SDK Profile官方转换及Release/IL2CPP/High非Development构建通过，0错/1条headless光照警告；89项产物和WASM/Brotli一致性已锁定。实际宿主runId=300ac54cc82f4b2d8f2c9a34fd2a2501，200、1次请求、摘要匹配、关闭后4项计数归零；宿主有SDK/GL/monitor警告，不宣称宿主零警告。主Wefund仍空闲/非Play、Console快照0错0警告，主工程1229项文件及冻结包/旧手机原文不改。新Host公共/私有配置urlCheck=true；旧urlCheck=false保持历史，不以新开关推断后台注册页面已独立检查。当前待真机短验，旧404与失败XML保留；不加载Sentis、不认证内容Ready/全HTTP矩阵，探针16MiB限额不改生产默认1MiB。详见Docs/S4-07HttpsSdkValidation.md及Evidence/S4-07/remote-https/corrected-v1/acceptance.json。原52项AND任务/G3-AOT/G4/Alpha保持，PlayerPrefs未实现、冷/热完整首玩继续暂缓、预算未冻结。下文旧继续点按历史阅读。

**最新进展：S4-07 用户 HTTPS/CDN 地址短验（2026-10-30）**。用户现已提供 `https://static.mini.iaa.itddo2page.com/ai-bot-models/model_tier1.sentis`；原“暂无端点”按历史阅读。实际curl GET（TLS验证开启）与干净独立Unity C# HttpClientService/UnityHttpTransport均返回404，错误体NoSuchKey。对象成功下载用例0/1通过、1失败，exit2及XML保持；框架分类network.http.404、仅1次请求、关闭后pending/exchanges/running/modules全部0。主工程/生产代码不改，无SDK构建或宿主/手机执行；微信合法域名权限尚未确认。当前blocked原因是资源不存在及域名权限待确认，等待修正对象路径/发布后复验，不将404计作模型下载或内容就绪通过。详见Docs/S4-07RemoteHttpsValidation.md及Evidence/S4-07/remote-https/。此前本地67/67及冻结SDK/手机证据保持，原52项AND任务/G3-AOT/G4/Alpha不变，PlayerPrefs未实现、冷/热完整首玩继续暂缓、预算未冻结。下文旧继续点为历史。

**当前继续点：S4-07 真实本地HTTP／内容启动组合验证（2026-10-30）**。复用现有C# HttpClientService/UnityHttpTransport/PackageStartup，新增13项组合用例，连同既有18项真实HTTP和36项Package展开用例，在无SDK、无主编译Library的干净源码工程67/67通过、0失败/0跳过。服务端独立计数核验503有限重试、403不重试、307不跟随、响应限额、截断拒绝；实际载荷SHA-256、缓存完整性、暂存先清理后回退、版本冻结、取消/关闭与实际终结后清理均通过。13份逐例记录及服务端终态均无残留。两轮66/67夹具失败及旧计数65保留：关闭可能Abort后快速排空，不能强制Timeout；最终以透明真实传输观察器验证正确契约，生产代码不改。1219项原工程文件与冻结SDK/手机证据保持，原52任务AND表不变。测试清单/载荷/缓存是夹具，未新增生产CDN Provider或AssetBundle能力；本轮无SDK/真机/HTTPS认证。用户明确暂无专用HTTPS/CDN端点，真实宿主联网验证blocked，不改域名/证书设置、不重复索取。详见S4-07ContentHttpIntegration.md、ADR-043、Evidence/S4-07/content-http/acceptance.json。下一步待端点具备后验证SDK转换后的C#网络路径和实际内容链；其他真实平台/广告权限及G3-AOT/G4仍独立待验。冷／热完整首玩继续暂缓，PlayerPrefs未实现，预算未冻结。下文按历史阅读。

**当前继续点：S4-07 生命周期／奖励／文件组合验证（2026-10-30）**。新增独立测试程序集，通过生产 PlatformService/AdsService/ReviveService/WeChatStorageProvider 组合，验证前后台门控、内存通知非完成事实、实际提交屏障、后台关闭等待、释放失败显式重试及旧Generation隔离。生命周期/广告为明确Mock注入，文件为真实System.IO临时双槽、声明Volatile。独立无SDK、无主Library的干净源码工程10/10 PlayMode通过；主Wefund两轮派发unknown_after_reload且无终态XML，不计主Wefund测试通过，记录保留。主Wefund最终恢复、空闲/非Play、Console快照0错0警告，LocalBootstrap无未保存修改，既有missing_light未自动修复。885项原工程文件与52项AND任务表不变；生产源码/已有SDK包/手机原文均保持。此前S4-07 iPhone受控10阶段通过保持；本轮不新增SDK、真机或OS内存压力认证。详见S4-07LifecycleIntegration.md、ADR-042、Evidence/S4-07/lifecycle-main/acceptance.json。下一步核对既有C# UnityWebRequest在SDK转换后的网络路径、确定专用HTTPS/CDN测试端点及内容启动矩阵；没有通用WX.Request声明不代表平台不能联网，不以DownloadFile直接替代HTTP契约。原平台/真实广告配置权限与G3-AOT/G4仍待验，冷／热完整首玩继续暂缓、PlayerPrefs未实现、预算未冻结。下文按历史阅读。

**当前继续点：S4-07 受控奖励/文件集成准备（2026-10-29）**。新独立单SDK Profile组合既有ReviveService/AdsService与真实微信C#文件端口，广告始终为Mock、写入/清理故障明确注入，不认证真实展示或Alpha。新增11项Unity（3 Edit/8 Play）及7项接收器合成测试通过；官方SDK转换/IL2CPP/Release/High/非Development构建通过（0错/1条headless探针警告），实际宿主10阶段及关闭0/0通过。ReviveRepository仅新增可选StorageLimits参数，微信装配显式64KiB，原调用默认不变，格式与容量校验不改。主Wefund原有95项回归（setup2/Edit30/Play62/cleanup1）最终通过，原场景/Build Settings/测试标记已恢复，最终Console快照0错0警告；前两轮因漏setup导致61/62的失败XML保持。2026-10-30已接收iPhone 13本轮报告（runId=f75f50112aad4c5e886efda30a8f07d6），严格接收器核验10阶段通过、终态0模块/0会话；真机范围仅为真实WX C#文件配合Mock广告及注入故障的同进程重建。此前误回传的S4-12原文及拒绝记录保留，旧S4-12通过保持，无需重采。保护主工程1209项基线（仅允许上述1份源码两行扩展）与旧74项不可变交付快照。最新报告S4-07ExecutionReport.md、指南RewardFileIntegrationGuide.md、ADR-041和Evidence/S4-07/acceptance.json。S4-07原AND依赖/G3-AOT/G4不放行，真实广告缺ID/权限、冷/热完整首玩暂缓、PlayerPrefs未实现、预算未冻结。下文旧继续点按历史阅读。

**当前继续点：S4-12 序列化 AOT 验证准备（2026-10-29）**。独立新单 SDK Profile 完成真实 JsonUtility DTO 与生产 ReviveCodec 的裁剪/序列化验证：21 EditMode + 9 PlayMode、7项接收器合成测试通过；官方 SDK 转换及 IL2CPP/Release/High/非Development 构建通过，0错/1条headless探针警告，WASM/Brotli一致。新开发者工具宿主实际13阶段全部通过、终态0模块/0会话，只证明同进程重建和受控样档迁移/失败保留。新 iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5报告已严格校验并归档，runId=91aa9cfa090d49708d842306da24036e，独立13阶段及终态0/0通过；手机界面由用户操作回传，未被独立观察，只认证同进程重建。无需重复扫码或关闭重开；旧S4-10手机配对证据保持。下一步按原依赖图推进可开发项，真实广告/平台故障权限及原Gates仍独立待验。主工程1209项基线及前一轮196项不可变快照保护，旧Profile和原文不覆写。修复测试DTO写入前校验，保留null数组测试失败及准备工具构建失败证据。报告 Docs/S4-12ExecutionReport.md，指南 Docs/SerializationAotGuide.md，机器验收 Docs/Evidence/S4-12/acceptance.json、ADR-040。原前置 S4-02 AND S4-09 AND G3-AOT 不变，S4-12整体blocked；Gates/52项依赖未放行、PlayerPrefs未实现、冷/热完整首玩暂缓、预算未冻结、真实广告缺配置权限。下文旧继续点按历史阅读。

**C# SDK API 约束接续（2026-10-29）**：当前自有微信平台操作统一通过官方 C# SDK，SDK 内部实现不改；宿主门控、文件端口及激励视频绑定位于独立单 SDK Profile，当前适配目录无自写 jslib。361 项 SDK Profile 回归与最终主 Wefund 188 项存储回归已通过；最终 C# 文件端口已完成正式构建、开发者工具与 iPhone13 两份报告配对短验，跨运行时 marker/StorageService 恢复及关闭归零通过（不外推强杀、断电或清缓存）。构建、宿主和设备的实际接受范围以 Docs/S4-10CSharpSdkValidation.md、Docs/Evidence/S4-10/native-rewarded/acceptance.json、ADR-039 为准。旧 S4-09 v2 iPhone 回执只属于旧端口，不能继承为新 C# 端口设备通过。缺真实广告位/展示权限，S4-08/09/10 整体 blocked，原52任务 AND 图及 G0-P1/G3-AOT/G4 未接受保持。PlayerPrefs Provider 未实现；冷/热完整首玩继续暂缓，预算未冻结。下文旧实现与旧继续点按历史阅读。

**S4-10通用桥接续（2026-10-29）**：按原规划推进SDK无关激励视频桥与AdsService集成验证，PlayerPrefs Provider未实现；用户暂无广告位ID，真实WX广告绑定/SDK展示构建及宿主/手机展示尚未完成，S4-10整体blocked。冷/热完整首玩继续暂缓、预算未冻结，S4-08/09整体blocked、原52任务AND依赖与G0-P1/G3-AOT/G4未接受状态保持；S4-09 iPhone13正常关闭重开有限通过及旧原文保持。最新结果与下一步以 Docs/S4-10ExecutionReport.md、Docs/Evidence/S4-10/acceptance.json 和 ADR-038 为准；下文此前继续点按历史阅读。

**S4-09 iOS 分类修复（2026-10-29）**：同一iPhone13只读回执确认 statSync 的 Error.message 含双重 fail，errno=1300002仅作观测元数据；v1精确桥将不存在文件误分为Io(-7)。生产桥仅对四个已知同步文件API开头归一化一个重复fail，保留权限/配额优先和未知失败关闭；67项JS桥回归及17项v2接收器合成测试通过。新独立v2 Profile的204项Unity测试通过（176Edit+28Play），正式SDK转换及IL2CPP/High/非Development构建通过（0错误/1条Emscripten优化限制警告），WASM/转换载荷一致；开发者工具两个独立运行时各13阶段通过，marker匹配及跨运行时StorageService seq3恢复通过；同一iPhone13的v2首轮13阶段回执通过（跨运行时恢复因无旧标记明确N/A），缺档首读修复已实机复验，现已取得独立第二份iPhone13回执：runId/launchId均变化，前次marker严格匹配，跨运行时StorageService seq3/[42,9]恢复及13阶段通过，关闭modules/sessions=0；正常关闭重开短验证通过，先前重复回传仍保留拒绝记录。v2另增失败阶段、清理实测计数和跨运行时MINISAVE恢复检查；首轮无旧档明确N/A，不等于持久性通过。旧v1失败、只读诊断及构建保持，主Wefund188为历史结果、本轮未重跑。冷／热完整首玩继续暂缓、合格样本0、预算未冻结；S4-09整体blocked，原52任务AND依赖不变，G0-P1/G3-AOT/G4未接受。最新证据 Docs/Evidence/S4-09/ios-fix/acceptance.json；下文旧状态按历史阅读。


**A/B/C稳态原文与条件已齐（2026-10-29）**：新C完整附件206240个UTF-16单元，计算FNV0fa3a9ec／SHA256af535251b7bcf3bd0777482d8da6d2746a3916f047992b1a2333845d6722cd11，captureId=s408abc-1a1856d31bf-f0816d；三窗53970帧／900.032秒、P95/P99均17ms、最大118/18/36ms，Ready11模块/4资源，终态关闭0/0/0及解绑通过。操作者确认C仍为Wi-Fi、未充电、低电量模式开启、无明显发热、无来电/切后台/锁屏、导出按钮自动出现；独立摘要无法取得，无需因此重采。选择A、B-repeat2、C形成描述性基线，B/C缺独立摘要交叉核对，非完全受控或CPU/GC归因；C首窗118ms长帧保留且原因未知。见abc-device/ABC-raw-candidate-comparison.json、C-condition-supplement.json与pending/C。前两次异常B整份保留，不拼窗；实际顺序跨批次，温度/冷却时长未知。本轮52项既有Host回归通过，新增0、不同用例覆盖仍231，Unity未重跑，冻结SDK/采集器/三包不变。冷/热完整首玩仍0，下一步采集外部启动录像及缓存证据；预算未冻结，G0-P1/G3-AOT/G4未接受，52依赖不变。最新abc-device/acceptance.json；下文旧状态为历史。

**A/B/C 真机稳态采集继续（2026-10-28）**：用户决定继续S4-08，不采用暂缓采集/提前推进S4-09方案。已确认同一iPhone13可用、先A；此前约21万字符baseline由恢复页复制完整JSON后保存，本轮复用该出口，冻结v2采集器/SDK/IL2CPP产物不改。新增稳态原文接收与A/B/C只读恢复后备，200项Host通过（178+22两批，164既有+36新增），本轮Unity未重跑。A的capture手机预览已生成，实际进入/3×300秒原文尚待回传；网络/供电/省电/温度未提供则保持未知。Mac开发者工具出现分包下载超时OBS-S408-ABC-DL-01，已保留失败，本地重启后20.017秒/1019帧及关闭排空通过；SDK空壳也观察到图形缓冲警告，根因未明，手机是否受影响待证；原图形观察保持。最新 `Docs/Evidence/S4-08/abc-device/acceptance.json`，当前操作S4-08ABCDeviceCollection.md。预算未冻结、冷/热完整首玩合格样本0、G0-P1/G3-AOT/G4未接受、52依赖不变。下文旧接续按历史状态阅读。

**C v2 完整原文恢复通过（2026-10-28）**：原手机通过只读恢复页与系统输入框手动复制回传完整C v2短回执；2676个UTF-16字符/FNV939d8ee7与先前摘要严格一致，构建身份及iPhone13环境匹配。原文确认Ready11模块/4资源，关闭后模块/资源/场景均0，观察者解绑；微秒换算8121.652ms与墙钟8122ms相符。原上传字节与仅移除一次已验证传输换行的规范原文分别归档，未先重序列化。最新证据 `Docs/Evidence/S4-08/abc-recovery/acceptance.json`、received/phone-raw.json、phone-recovery-receipt.json；下文文件待恢复/保留旧会话为历史状态。164项Host为前轮已通过，本次仅原文接收及一致性复核，Unity未重跑，abc-host三组v2构建保持。已证明手动完整原文出口，不认证自动剪贴板；A/B原文、新A/B/C稳态对照与冷/热完整首玩仍待验。预算未冻结、首玩合格样本0、G0-P1/G3-AOT/G4未接受，52依赖不变。

**C v2 会话关闭后的恢复接续（2026-10-28）**：用户确认手机会话已关闭，完整JSON尚未回传；此前三组摘要保留，不能推断v2剪贴板成功或失败。已新增严格匹配captureId/身份/环境/2676字符/FNV939d8ee7的只读ABC恢复页与原文接收器，Host164项（新增28、复跑此前136）通过；主Wefund空闲/非Play/Console0，Unity构建与测试本轮未重跑，前轮192次为历史证据。实际开发者工具显示恢复页，手机文件是否存在及完整回传仍待验证，不重采、不清缓存。最新证据 `Docs/Evidence/S4-08/abc-recovery/acceptance.json`，报告S4-08ABCRecovery.md；abc-host/v2三组构建保持。预算未冻结、首玩合格样本0、G0-P1/G3-AOT/G4未接受，52依赖不变。下文“保留当前C会话/等待旧导出菜单”按历史快照阅读。

**宿主冒烟与时钟资格接续（2026-10-28）**：v1手机性能时钟单位缺陷已修复，原摘要与产物保留；v2三组正式SDK/IL2CPP High构建、192次Unity执行（176项不同用例，新增0）、136项Host（新增26）及6项实际模拟器场景通过，主完整1292未重跑。A/B/C在同一iPhone13回传短冒烟摘要，构建身份及微秒单位交叉检查匹配；摘要不能认证缺失的原文、残留计数或受控性能。C完整原文出口待回传确认，先保留手机会话，不开始长采集。最新证据 `Docs/Evidence/S4-08/abc-host/acceptance.json`，契约ADR-034、报告S4-08ABCHostValidation.md；当前模板collection-session-template-v2.json。OBS-S408-ABC-GL-01图形警告根因及保留/新发区分仍开放。预算未冻结，G0-P1/G3-AOT/G4未接受、52依赖不变、S4-09/10不放行。下文旧计数及v1待办为历史快照。

**A/B/C与首玩工具接续（2026-10-28）**：新独立A SDK空壳、B最小Core、C完整样板使用相同锁定SDK与公共探针；正式SDK/IL2CPP High对照构建及采集工具证据见 `Docs/Evidence/S4-08/abc-firstplay/acceptance.json`，契约ADR-033，报告S4-08ABCFirstPlayValidation.md，操作S4-08ABCFirstPlayGuide.md。Unity192次执行/176项不同用例、Host110项以最终机器验收为准，主完整1292未重跑。旧iPhone13基线保持，新C必须重采；实际新A/B/C配对数据与首玩冷/热各10次尚未采集。内部JS计时不代表完整首玩，预算保持未冻结；G0-P1/G3-AOT/G4未接受，52任务依赖不变、S4-09/10不放行。下文此前“A/B未准备”等为历史快照。

**实机原始基线接续（2026-10-28）**：iPhone13/iOS18.7.8/微信8.0.79/基础库3.17.5，完整C样板3×300秒共53974帧、900.020秒已严格复算；P95均17ms、P99为18/18/17ms、最大93/31/25ms。摘要/FNV/构建身份及上传原文一致，正常初始化/登录凭证释放、50关卡往返/100面板切换和关闭排空通过，手动恢复完成。新增8项接收测试与55项既有共63Host通过，本次未重跑Unity；先前168项保留。最新 `device-baseline/acceptance.json` 与 S4-08iPhone13Baseline.md；预算未冻结、A/B/首玩/CPU/GC/RSS、其他设备与权限/生命周期故障矩阵仍待验，G0-P1/G3-AOT/G4未接受、52依赖不变。下文“手机未回传/恢复待验”是历史快照，不再作为当前阻塞。

**最新接续：S4-08真实微信宿主故障矩阵（2026-10-28）**。在实际开发者工具/SDK/IL2CPP High中，14/14项受控注入场景通过，含取消/超时、重复及迟到回调、complete-only、关闭在途、释放/部分解绑失败显式重试、旧Generation隔离及三轮官方生命周期验证。9次实际wx.login、29条C#阶段证据；injected=true/deviceExecuted=false，不认证上游自然故障或真机。新增冷Native API装配工厂沿用宿主门控，Editor拒绝Native；仅自有绑定与测试两份文件修改。最终168/168 Unity（新增1，样板19+平台/桥/SDK149）、20/20新增Host；主完整1292及旧浏览器AOT未重跑。新项目实际基础库3.17.4，上轮3.15.2结果分别保存。真实设备、生产权限范围与预算仍阻塞，S4-08整体blocked，G0-P1/G3-AOT/G4未通过，52依赖不变、S4-09/10不放行；宿主内确定性矩阵已完成，不再笼统记为未执行。见[矩阵报告](../Docs/S4-08HostFaultMatrixValidation.md)、[host-fault-matrix验收](../Docs/Evidence/S4-08/host-fault-matrix/acceptance.json)、[ADR-031](../Docs/ADR/ADR-031-WeChat-Host-Fault-Matrix.md)。下文此前计数和待办均为历史快照。

**最新接续：完整 LocalCore-v1 微信开发者工具验证（2026-10-28）**。用户 AppID 已配置；SDK 转换后的小游戏已实际运行，Native 初始化、一次登录凭证获取与释放、官方 Hidden→Shown、平台与样板关闭排空四类模拟器验收通过，关闭后复测无新框架事件。修复自有浏览器探针宿主保护，以及 WX 原生成功 200→通用 0 的绑定映射；新增 3 项 Unity。本轮最终独立 Profile 167/167 Unity（样板19+平台/桥/SDK148）、18/18 Host；主完整1292与旧浏览器六类AOT未重跑。1505保护文件仅自有WX绑定/测试两项授权修改，其余1503不变；主已导入资产/设置/包锁、SDK来源和旧证据保持。真实设备、生产权限范围、宿主故障矩阵与预算仍待验，S4-08整体blocked，G0-P1/G3-AOT/G4未通过，52依赖不变、S4-09/10不放行；不再以缺AppID/SDK或未运行小游戏作为当前阻塞。见[本轮报告](../Docs/S4-08DevtoolsValidation.md)、[devtools-host验收](../Docs/Evidence/S4-08/devtools-host/acceptance.json)、[ADR-030](../Docs/ADR/ADR-030-WeChat-Devtools-Native-Host-Validation.md)。下文此前进度和计数为历史快照。

Latest — **S4-08 complete LocalCore-v1 SDK export, IL2CPP/High and actual browser AOT passed (2026-10-28)**. [Report](../Docs/S4-08LocalCoreAotValidation.md), [acceptance](../Docs/Evidence/S4-08/localcore-aot/acceptance.json), [ADR-029](../Docs/ADR/ADR-029-LocalCore-WeChat-Browser-AOT.md). Three original scenes and LocalOnly/Mock composition retained; official SDK export SUCCEED, WebGL2/Linear/non-Development High build 0 errors / 25 classified warnings. Six AOT cases execute in isolated Chrome against the exact linked WASM: coins35, transitions5, all ownership drained. This run passes 164 Unity (19 template + 145 SDK/platform) and 12 new Host checks; historical main1292 not rerun. Only two validation DTO fields use precise Preserve after High removed them; no stripping downgrade. Main and previous profiles/evidence unchanged. Converted minigame, real WeChat API/device and budgets remain unverified; S4-08 BLOCKED, G0-P1/G3-AOT/G4 unaccepted, 52 dependencies unchanged, no S4-09/10 release. Earlier snapshots follow.

Historical — **S4-08 offline SDK export and IL2CPP/High adapter-link build passed (2026-10-28)**. [Build report](../Docs/S4-08SDKBuildValidation.md), [latest acceptance](../Docs/Evidence/S4-08/sdk-build/acceptance.json). Independent WeChatAdapterLinkProbe-v1: official `WXConvertCore.DoExport(true)` SUCCEED; non-Development WebGL IL2CPP/High Succeeded, 0 errors / 8 classified warnings, 88 artifact files. Actual WASM validates and SDK Brotli payload matches it byte-for-byte. This run repeats 145 Unity cases and adds 9 Host policy checks; previous main 1292 and Host 119 are historical, not rerun. Primary source/settings and wechat-final4 evidence unchanged; exporter automatically disables one optional GLX importer in the new disposable build profile. SDK/toolchain runtime observations remain open. Blank AppID; no host/device run; not LocalCore-v1 AOT acceptance. S4-08 stays BLOCKED, G0-P1/G3-AOT/G4 unaccepted, no S4-09/10 release; 52 dependencies unchanged. Earlier progress follows as history.

Historical — **S4-08 implementation and local validation delivered; platform acceptance BLOCKED**. [Report](../Docs/S4-08ExecutionReport.md), [ADR-028](../Docs/ADR/ADR-028-WeChat-SDK-Isolation-Native-Adapter.md), [guide](../Docs/WeChatAdapterGuide.md), [acceptance](../Docs/Evidence/S4-08/sdk-adapter/acceptance.json). Main Wefund **1292/1292** (57 new + 1235 historical), final isolated **57/57**, clean selected WX profile **145/145** + real WebGL C# compile, Host **119/119**. Added the SDK-free ownership bridge and actual WX binding, host gate, callback-clone cleanup lease and cold-release guard. User WX202610200817 archive and actual TTSDK6.9.5 installation are locked; selected profiles compile, mixed actual SDKs reject with CS0433. Main only adds the owned pure provider package; vendor DLL/importer bytes and original settings/UI remain unchanged. Native binding is materialized from PlatformAdapters/WeChat by `Tools/S4/prepare_wechat_sdk.py`; the lock selects wechat-final4. Main runner/verifier: `run_wechat_adapter_tests.py` / `verify_wechat_adapter.py`. G0-P1, real host/device, SDK export/IL2CPP, G3-AOT/G4 remain unaccepted; 52-task dependencies unchanged.

Historical progress — **S4-08 generic preparation complete; native adaptation BLOCKED**. [Report](../Docs/S4-08ExecutionReport.md), [ADR-027](../Docs/ADR/ADR-027-First-Platform-Preparation-Native-Gate.md), [guide](../Docs/FirstPlatformPreparationGuide.md), [record](../Docs/Evidence/S4-08/acceptance.json). Current Wefund common platform **80/80**, independently recompiled SDK-free profile repeats the same **80/80**, Host **91/91** (53 new + 38 historical). No new Unity tests or full historical 1235 rerun. Added fail-closed preparation profile, 12 native contract families, integrity preflight and reproducible source-only isolation. Run `python3 Tools/S4/platform_preflight.py` (blocked exit 2), `run_first_platform_tests.py`, and `verify_first_platform.py` under Tools/S4. External package changes and initial SDK CS0433 are preserved; this task changed neither SDK nor package selection. Native backend/permission/device and G0-P1 remain blocked; no S4-09/10 release, no G3-AOT/G4 acceptance. Original 52 dependencies remain. Historical results follow.

Historical acceptance — **S4-06 ReviveLocal-v1**: consumer-owned persistent Claim/reward/progress transaction, local generation reservation before publication (restart durability follows the selected backend), accepted-work Writer ownership through Stop, and an independent responsive static sample. **1235/1235 Unity** (95 new, 1140 historical; 910 PlayMode/325 EditMode, 44 suites) + **38/38 host**, final isolated **95/95**. [Report](../Docs/S4-06ExecutionReport.md), [ADR-026](../Docs/ADR/ADR-026-Revive-Persistent-Claim-Sample.md), [guide](../Docs/ReviveSampleGuide.md), [acceptance](../Docs/Evidence/S4-06/acceptance.json). Run `python3 Tools/S4/run_revive_tests.py` with the controlled HTTP fixture, then `--new-only` and `python3 Tools/S4/verify_revive.py`. Default Mock/Volatile; Editor disk is explicit. Original LocalCore assets/settings/locks/profiles and 52-task dependencies preserved. Source-only profile closures are not new clean Player builds. Next real-platform S4-08 remains gated by G0-P1; G3-AOT/G4 unaccepted. Historical progress follows.

Historical acceptance — S4-11: **explicit LocalOnly policy and Account exclusion; optional AccountSession/FakeIdentity and original-owner retirement**. [Report](../Docs/S4-11ExecutionReport.md), [ADR-025](../Docs/ADR/ADR-025-Ownership-Modes-Account-Retirement.md), [guide](../Docs/OwnershipModeGuide.md). 1140/1140 Unity passed (83 new, 1057 historical; 848 PlayMode/292 EditMode) + 38/38 host; final isolated 83/83. Run `python3 Tools/S4/run_ownership_tests.py` (historical controlled HTTP fixture required), `--new-only` (83), then `python3 Tools/S4/verify_ownership.py`; read Evidence/S4-11/acceptance.json for actual acceptance.

LocalOnly publishes an explicit stable installation owner/namespace without Account or login. Optional account switching gates the old session before cancellation and retains its actual work at its original namespace; Ads owner authority blocks stale requests before Show and preserves already visible presentation facts. FakeIdentity is simulation, not server authentication. Generation seed persistence requires the application. Next: S4-06, general dependencies satisfied; original dependencies unchanged. G3-AOT/G4/platform/server certification remain unaccepted. LocalCore-v1 mounting is deferred to S4-06. Historical snapshots follow.

Historical acceptance — S4-05: **Ads state machine, serial native drain, fixed Claim owner and explicit Mock**. [Report](../Docs/S4-05ExecutionReport.md), [ADR-024](../Docs/ADR/ADR-024-Ads-Serial-Presentation-Claim-Owner.md), [guide](../Docs/AdsClaimGuide.md). 1057/1057 Unity (90 new, 967 historical; 795 PlayMode/262 EditMode) + 38/38 host tests passed; see Evidence/S4-05/acceptance.json for the evidence gate. Run `python3 Tools/S4/run_ads_tests.py` (controlled HTTP fixture required), `--new-only` (90), then `python3 Tools/S4/verify_ads.py`.

Cancel/timeout ends observation; an already visible ad retains its native slot, callbacks and frozen Owner until actual termination, fact handoff and cleanup. Caller-owned bounded journal records presentation facts; it does not grant rewards or persist Claims. AccountScoped is rejected pending S4-11. Next eligible general task: **S4-11 before S4-06**, preserving the existing dependency graph. Native SDK/device, G3-AOT/G4 remain unaccepted. LocalCore-v1 does not automatically mount Ads/Revive. Historical progress follows.

Historical acceptance: **S4-04 platform domains and explicit Mock, 967/967 Unity + 38/38 host tests**. [Report](../Docs/S4-04ExecutionReport.md), [ADR-023](../Docs/ADR/ADR-023-Platform-Capabilities-Login-Lifecycle.md), [guide](../Docs/PlatformMockGuide.md). Separate capability/lifecycle/login contracts; credential results are authorization codes, not business accounts. Bounded generation/sequence/attempt mailbox, independent of paused gameplay queues; cold single-flight login, actual drain, explicit cleanup retry, and credential revocation on close. No real SDK installed or certified.

Run `python3 Tools/S4/run_platform_tests.py` (967 cases; historical HTTP fixture required), `--new-only` (80), then `python3 Tools/S4/verify_platform.py`. Next: S4-05, not started. LocalCore-v1 does not automatically install a platform or log in. Actual SDK/account integration remains S4-08/11; G3-AOT/G4 unaccepted. Existing native/SDK-plugin observations remain tracked. Historical progress follows.

Historical acceptance: **S4-03 lightweight HTTP, 887/887 Unity + 38/38 host tests**. [Report](../Docs/S4-03ExecutionReport.md), [ADR-022](../Docs/ADR/ADR-022-Http-Client-Lifecycle-Retry.md), [guide](../Docs/HttpClientGuide.md). Pure Context-owned client and isolated UnityWebRequest transport: cold single-use operations, overall real-time deadlines, bounded responses, GET/HEAD at most three attempts, write verbs once, redirects disabled, Abort followed by actual drain and release. Cleanup retry never resends HTTP.

Start the controlled loopback fixture as described in the guide, then run `python3 Tools/S4/run_http_tests.py` (887 cases; `--new-only` 73), capture server proof and run `python3 Tools/S4/verify_http.py`. Next: S4-04, not started. LocalCore-v1 does not automatically send network requests. Platform/TLS/CORS/device/AOT remain separate; G3-AOT/G4 unaccepted. Existing native/SDK-plugin observations remain tracked. Historical progress follows.

Historical acceptance: **S4-02 Editor/Fake storage and recovery, 814/814 Unity + 32/32 host tests**. [Report](../Docs/S4-02ExecutionReport.md), [ADR-021](../Docs/ADR/ADR-021-Storage-Backends-Recovery.md), [guide](../Docs/StorageRecoveryGuide.md). Explicit copy migrations commit once before publishing immutable DTO snapshots. Editor physical I/O stays Editor-only; Fake is Volatile. `Restored` is the initial snapshot, not a writable live model. Omit `StorageRecovery` to retain S4-01 strict mode.

Run `python3 Tools/S4/run_recovery_tests.py` (814 tests), `--new-only` (74), then `python3 Tools/S4/verify_recovery.py`. Next: S4-03, not started. LocalCore-v1 does not automatically install storage. Platform power-loss recovery, account scoping and published AOT remain S4-09/11/12; G3-AOT/G4 remain unaccepted. Existing native JobTempAlloc and Stark plugin observations are tracked separately. Historical progress follows.

Historical acceptance: **S4-01 portable storage foundation, 740/740 Unity tests + 32/32 host tests**. [Report](../Docs/S4-01ExecutionReport.md), [ADR-020](../Docs/ADR/ADR-020-Storage-Format-Provider.md), [format/provider guide](../Docs/StorageFormatV1.md). `MiniFramework.Storage` provides explicit IDs/schema, bounded cold snapshots, format-v1 checksums, conditional ordered commits and Context drain. `StorageProvider` is a cold session factory bridge; production physical backends, migration and game-state restoration are S4-02. The local sample does not yet mount storage. Next: S4-02 (not started). S3-08 remains platform-blocked; G3-AOT/G4 are unaccepted.

Run `python3 Tools/S4/run_storage_tests.py` from repository root for all 740 tests, `--new-only` for 83 new storage tests, then `python3 Tools/S4/verify_storage.py` for the evidence gate. Historical progress follows.

Historical continuation: **S3-08 generic AOT preflight prepared, real first-platform execution blocked**. [Report](../Docs/S3-08ExecutionReport.md), [ADR-019](../Docs/ADR/ADR-019-AOT-Release-Evidence-Gate.md). S3-07 was completed before S3-01. No new AOT Player/device execution is claimed; G3-AOT remains unaccepted. Next eligible generic task: S4-01. Existing S3-06 local runtime acceptance follows below.

S3-06 complete: **657/657 Unity tests + 9/9 pipeline tests**, three clean profiles and native macOS Mono Player smoke passed. **G3-Core accepted locally for LocalCore-v1 only; G3-AOT not accepted.** Local template: [quick start](../Docs/LocalTemplateQuickStart.md), [execution report](../Docs/S3-06ExecutionReport.md), [ADR-018](../Docs/ADR/ADR-018-Local-Template-G3-Core.md). Open **Tools > MiniFramework > Local Sample > Open Bootstrap**, then Play. The local profile uses real generated config, native scenes and Unity audio; external platform capabilities are labeled **Mock**. G3-AOT remains unaccepted. The earlier LocalGame example below is the preserved S1 compile stub.

Current task context and handoff: [Unity project context](../Docs/AI/UnityProjectContext.md) and [execution ledger](../Docs/AI/ExecutionContext.md). Read these and the latest acceptance before continuing implementation.

Open this directory in Unity **2022.3.62f3**. Existing URP 2D settings and SampleScene are preserved. No reference-project source has been copied. Official MIT Luban runtime sources are separately pinned and attributed in Assets/Game/Config/LubanRuntime/LICENSE.

S1-02 adds `MiniFramework.Runtime.GameContext`: explicit module registration, graph validation, startup, rollback, and single-flight shutdown. `Game.Boot.LocalGame` composes a no-resource Home module, injects `IGameServices` into the instance `Game.Access.GameServices` facade, and supplies `Game.Features.HomePanel`. No static service locator or platform credentials are needed. The generic UI stub uses Contracts.S1.IPanelContract/IUiContractStub to compile `OpenAsync<HomePanel>` across the real assembly boundaries; it creates no GameObject and is replaced by UIKit in S3.

```csharp
var game = new Game.Boot.LocalGame("Local home");
var started = await game.StartAsync(MiniFramework.Contracts.OperationContext.Unlimited());
if (started.IsSuccess)
{
    var panel = await Game.Features.HomePanel.OpenAsync(game.Access, MiniFramework.Contracts.OperationContext.Unlimited());
    if (panel.IsSuccess)
        await panel.Value.RefreshAsync(MiniFramework.Contracts.OperationContext.Unlimited());
}
await game.CloseAsync(MiniFramework.Contracts.OperationContext.Unlimited());
```

Call lifecycle APIs on Unity's main thread. Never synchronously block on a UniTask. A timed-out close waiter does not release in-flight resources; keep the Context until drain finishes. Default GameContext now creates a real Scope for each module. Use `IScopedModuleContext.Scope` to create children, track subscriptions, or start owned tasks. The custom registry factory overload remains available; `NoResourcesCleanupRegistry` is only an explicit no-resource compatibility/test option. A failed-cleanup Context must not be reused.

Kernel contains pure lifecycle/arbitration primitives. Contracts owns Result and interfaces. Runtime depends on Kernel/Contracts/UniTask. Game.Access references only Contracts and UniTask; Features references Access, Contracts and UniTask; Boot owns concrete composition. Host/EditorFoundation/Local provider remain assembly boundaries, not complete runtime services.

Run all six MiniFramework test assemblies in Unity Test Runner, or from the repository root with the existing Wefund Editor MCP server enabled:

```sh
python3 Tools/S1/run_scope_tests.py
```

The script reads the local MCP token without printing it and saves actual Test Runner XML under `Docs/Evidence/S1-03/`. See [ADR-001](../Docs/ADR/ADR-001-Lifecycle-Async-Contracts.md), [S1-03 report](../Docs/S1-03ExecutionReport.md), and [S0 audit/blockers](../Docs/S0ExecutionReport.md). S1-03 does not imply G1, platform build, or device acceptance.

`CleanupRegistry` and each ticket separate the caller's wait from actual release. A timeout leaves a running cleanup owned. `RetryFailedAsync`/ticket `RetryAsync` explicitly retry terminated failures only; successful cleanup is not replayed. Context `RetryCleanupAsync(cleanupPolicy, wait)` retries registry failures and then disposes retained dependency modules. It does not replay failed module Stop/Dispose phases. Keep the Context until drain succeeds; never restart a closed or failed instance. A task factory is invoked only after its observer is registered; task failure is recorded, not retried as cleanup.

## S1-04 基础设施

类型事件 `EventChannel<T>`、写入端 `StateValue<T>` 与独立只读视图、`StateMachine<TKey>`、托管 `ObjectPool<T>` 均位于 MiniFramework.Runtime；没有静态全局实例。模块从 `IScopedModuleContext.Scope` 登记所有权。

```csharp
var scope = ((IScopedModuleContext)context).Scope;
var changes = new EventChannel<int>();
var subscribed = changes.Subscribe(scope, "score-listener", score => { /* refresh */ });
if (!subscribed.IsSuccess) return Result.Failure(subscribed.Error); // 在返回 Result 的初始化辅助方法中
```

FSM 与池自身用 `scope.TrackSubscription`；池先登记自身，再 `Rent(scope,id)`，确保逆序归还后再关闭池。检查所有 Result，订阅不会自动回放当前状态。池仅用于无外部资源的托管对象；场景对象和资产池属于 S2。完整可编译模块示例见 `Assets/Tests/S1Primitives/PlayMode/PrimitiveTests.cs` 的 PrimitiveModule。

契约见 [ADR-002](../Docs/ADR/ADR-002-Scoped-Primitives.md)，验证见 [S1-04 执行报告](../Docs/S1-04ExecutionReport.md)。`python3 Tools/S1/run_primitives_tests.py` 从仓库根目录运行全部 7 个程序集，98 项测试；`python3 Tools/S1/verify_primitives.py` 校验最终证据。G1 未通过，S1-05/06 待实施。

## S1-05 Unity Host

每个 Context 绑定独立 RuntimeHost；在 Boot 中将 RuntimeHostModule 注册为 IRuntimeScheduler，消费者声明 `runtime-host` 依赖，通过 Services 取得接口。用 Scope.TrackSubscription 管理定时器、Tick 和操作观察句柄，检查每次登记 Result。可编译示例见 `Assets/Tests/S1Host/PlayMode/HostTests.cs`。没有隐式全局 Host，也不自动修改场景。

scaled/unscaled 游戏时钟在显式暂停时冻结，实时操作期限继续流逝；恢复时取消/超时优先于 SDK 队列。Post 可从工作线程调用，其余操作在主线程。接受的载荷必须由 dispatch/discard 处理；关闭时 discard 失败保留到显式重试。按需 Tick 只调用订阅者，仍有轻量 Unity Update 驱动。

[ADR-003](../Docs/ADR/ADR-003-RuntimeHost-Clocks.md) 定义完整契约，[S1-05 报告](../Docs/S1-05ExecutionReport.md) 记录验证边界。仓库根运行 `python3 Tools/S1/run_host_tests.py` 执行 8 个程序集、132 项测试；`python3 Tools/S1/verify_host.py` 核验证据。G1 未通过，下一步 S1-06。

## S1-06 诊断与 G1

G1 已通过本地验收，142/142 项测试和实际关闭 Domain Reload 后的 10 次 Play/Stop 均通过。DiagnosticSession 默认关闭；按实例登记 Context、事件、状态、Host 和池后，显式 Enabled=true 并 Capture。Context 已包含根 Scope 树，勿重复登记子 Scope；CaptureModules 分配详情数组，不建议每帧查询。Dispose 可释放诊断引用。

完整契约及成本见 [ADR-004](../Docs/ADR/ADR-004-Diagnostics-G1.md)，证据见 [S1-06 报告](../Docs/S1-06ExecutionReport.md)。仓库根执行 `python3 Tools/S1/run_diagnostics_tests.py`、`python3 Tools/S1/run_domain_reload_acceptance.py`、`python3 Tools/S1/verify_diagnostics.py`；架构门禁为 `python3 Tools/S1/check_architecture.py`。Domain Reload 脚本临时改设置并在 finally 恢复，若外部强制结束需先恢复 Library/G1PlayAcceptance/original.json 记录的原值。下一项 S2-01，平台门槛仍独立阻塞。

## S2-01 资源协议与 FakeProvider

Contracts 中定义 ResourceKey、业务租约/服务接口与 Provider 实际完成接口；独立本地包 com.team.mini.provider.fake 可通过 AssetAt/SceneAt 的 Succeed/Fail 及 ReleaseAttempt/Activation/Unload 控制完成。Fake 只能用于协议验证，不提供 Unity/YooAsset 加载，不实现服务层共享或自动取消。

可编译协议示例见 `Assets/Tests/S2Resources/ResourceContractTests.cs`。接口/所有权详见 [ADR-005](../Docs/ADR/ADR-005-Resource-Ownership.md)，证据见 [S2-01 报告](../Docs/S2-01ExecutionReport.md)。仓库根执行 `python3 Tools/S2/run_resource_contract_tests.py` 运行 172 项测试；`python3 Tools/S2/verify_resource_contracts.py` 核验本阶段证据。此处为 S2-01 阶段记录；S2-02 交付见下节，G2 未通过。

## S2-02 共享 AssetsService

在 Boot 创建 `AssetsService(provider, package, contentVersion, cleanupPolicy)`，通过 ModuleRegistration 注册为 IAssetsService。消费者声明该模块 Id 依赖，取得服务后用 `Load<T>(key, scope)` 登记冷请求，再且仅一次调用 WaitAsync。请求接受、等待和归还均检查 Result；不直接使用 Provider 句柄，也不修改引用计数。

Lease.ReleaseAsync 可提前结束等待而实际收尾继续；成功后的重复归还返回 InvalidState，失败必须显式 RetryReleaseAsync 或走 Scope/Context Registry 重试。服务本身 CloseAsync 返回实际收尾任务，需要有界观察时使用 Scope/GameContext 关闭接口。可编译示例与 20 请求共享测试见 `Assets/Tests/S2Assets/AssetsServiceTests.cs`。

[ADR-006](../Docs/ADR/ADR-006-Shared-AssetsService.md) 定义全部行为，[执行报告](../Docs/S2-02ExecutionReport.md) 记录验证边界。仓库根运行 `python3 Tools/S2/run_assets_tests.py` 执行 199 项测试；`python3 Tools/S2/verify_assets.py` 核验证据。下一项 S2-03；G2 未通过。

## S2-03 真实 SDK 与本地后端

`com.team.mini.provider.yooasset` 提供指定 ResourcePackage + 冻结版本的 YooAssetProvider（SDK 3.0.6）。Boot 初始化包/清单后，将 Provider 注册为模块，并向 AssetsService 传入 `providerModuleId: provider.Id`，两者 ModuleRegistration 都使用自己的 Dependencies。Provider 不自行选择 CDN 或切换包版本。

`com.team.mini.provider.local/Editor` 提供 EditorLocalAssetProvider，构造传入精确地址→Assets/ 路径字典；只在 Editor 编译，无 YooAsset 依赖。归还不销毁编辑资产。两后端当前场景能力返回 NotSupported，S3-04 再实现。

完整代码示例及错误映射见 [ADR-007](../Docs/ADR/ADR-007-YooAsset-Local-Providers.md)，[执行报告](../Docs/S2-03ExecutionReport.md) 说明 SDK EditorSimulate 与设备/CDN 验证边界。运行 `python3 Tools/S2/run_provider_tests.py` 和 `python3 Tools/S2/verify_providers.py` 复测。下一项 S2-04，G2 未通过。

## S2-04：资源调度

实现与边界见 [ADR-008](../Docs/ADR/ADR-008-Resource-Scheduling.md)，本地验收见 [报告](../Docs/S2-04ExecutionReport.md)。S2-04 完成不代表 G2 或设备性能通过。

装配时创建调度器并传入 AssetsService（下列变量为装配方已有实例/配置）：

```csharp
var scheduler = new ResourceWorkScheduler(
    new ResourceScheduleBudget(2, 2, 4, TimeSpan.FromMilliseconds(2)),
    hostModuleId: hostModule.Id);
var assets = new AssetsService(provider, package, version, cleanup,
    providerModuleId: provider.Id,
    scheduler: scheduler, schedulerModuleId: scheduler.Id);
```

在 Context 中注册 Host 的 IRuntimeScheduler、Provider 的 IAssetProvider、scheduler 的 IResourceWorkScheduler 和 assets 的 IAssetsService；依赖先初始化 Host/Provider，再 scheduler，最后 assets。完整可编译装配与关闭样例见 `Assets/Tests/S2Scheduler/ResourceSchedulerTests.cs` 和 `Assets/Tests/S2Providers/YooAssetIntegrationTests.cs`。Host 自动每帧驱动，勿再手动 Tick。未注入 scheduler 的旧调用继续工作，但无调度限额。Load 的优先级重载允许 Critical/Normal/Prefetch；第三方 IAssetsService 实现需补齐新增重载。

ScheduleFrame 用于独立同步单元预算；已开始单元不可抢占，创建出的对象仍须由生产者注册 Owner。实例池/预热尚待 S2-05。配置数值仅为示例，不代表真机性能推荐。

当前全量复验使用 `python3 Tools/S2/run_scheduler_tests.py`（258 项）与 `python3 Tools/S2/verify_scheduler.py`。上方各阶段计数与“下一项”为历史验收记录；当前下一项 S2-05。旧阶段 runner 的预期计数对应当时源码快照。

## S2-05：GameObject 实例池

最新验收为 296 项，复验入口 `python3 Tools/S2/run_pool_tests.py`、`python3 Tools/S2/verify_pool.py`。上方阶段计数/下一项均为历史记录；当前下一项 S2-06，G2 未通过。契约见 [ADR-009](../Docs/ADR/ADR-009-GameObject-Pool.md)，证据见 [执行报告](../Docs/S2-05ExecutionReport.md)。

GameObjectPool 在独立 MiniFramework.Pool 程序集中，以已有 assets/scheduler、PoolCapacity、独立 CleanupContext 构造；可传 hostModuleId 自动检测外部 Destroy。将 IModule 及 `IPoolService<GameObject>` 注册进 Context，并使用 Pool.Dependencies 声明 Assets/Scheduler/Host 依赖。无 Host 装配须每帧调用 PollDestroyed；不要重复驱动 Scheduler.Tick。

```csharp
// pool、owner、prefabKey、operation、cleanup 由装配方/调用方提供。
var accepted = pool.Spawn(prefabKey, owner);
if (!accepted.IsSuccess) return;
var spawned = await accepted.Value.WaitAsync(operation);
if (!spawned.IsSuccess) return;
var rent = spawned.Value;
// 动态资源使用 rent.RentScope 作为 Owner，完成组件初始化、parent 绑定后激活。
rent.Value.SetActive(true);
var returned = await rent.ReturnAsync(cleanup);
// 检查 returned：等待超时不代表收尾结束；ReturnFailed 使用 RetryReturnAsync。
```

完整可编译装配见 `Assets/Tests/S2Pool/GameObjectPoolTests.cs`。每次租用得到新 RentScope；勿保留旧租约访问复用后的实例。对象不会自动重置脚本字段，业务应在激活前完成重置。Bucket 持有 Prefab，归还动态资源后才允许缓存复用；空桶自动驱逐，含闲置实例的桶可调用 EvictAsync。PrewarmAsync 受实例/闲置/桶容量与帧预算约束。示例不代表默认入口已装配资源包或完成平台性能验证。

## S2-06：资源绑定、诊断与压力

当前复验入口为 `python3 Tools/S2/run_maintenance_tests.py`（337 项）和 `python3 Tools/S2/verify_maintenance.py`。上方各阶段命令/计数为历史快照；下一项 S2-07，G2 未通过。详见 [ADR-010](../Docs/ADR/ADR-010-Resource-Bindings-Pressure.md) 和 [报告](../Docs/S2-06ExecutionReport.md)。

为现有 Image 创建一个绑定对象，并在该 Owner 会话内重复使用，避免每次换图都创建互不相干的写入者：

```csharp
var created = SpriteBindings.ForImage("home/icon", image, assets, viewScope, cleanup);
if (!created.IsSuccess) return;
var icon = created.Value;
var result = await icon.SetAsync(spriteKey, operation);
// 检查 result；失败/取消保留旧图。ViewScope 关闭会先清空显示，再归还租约。
```

池化子节点用 `SpriteBindings.ForChildImage("enemy/icon", image, rent, assets, cleanup)`，它验证 Transform 归属并使用 rent.RentScope；每次租用创建新绑定。也可为 SpriteRenderer 使用 ForRenderer。适配程序集 MiniFramework.Bindings 使用现有 UnityEngine.UI，没有新增包；业务自定义目标可实现纯 Contracts 的 IResourceTarget<T>。示例变量由装配方提供，完整可编译用例见 `Assets/Tests/S2Maintenance/ResourceMaintenanceTests.cs`。

压力装配：创建 ManualMemoryPressureSource、MemoryPressureController(source, scheduler, pool, destroysPerFrame, cleanup)，按控制器 Dependencies 注册到 Context，暴露 IMemoryPressureHandler。测试入口 source.Raise() 或 NotifyAsync(operation)；平台通知尚未连接。回收只针对 pass 开始时的闲置快照，重复通知在该 pass 内合并；不会停止正在显示或实际加载的资源。失败的桶需显式 Evict/Context 重试。

诊断调用 assets.CaptureResources() / pool.CapturePoolResources()；返回逻辑持有快照，按需分配。Editor 的 Tools → MiniFramework → Resource Diagnostics 视图可从 Editor 装配调用 window.Attach(assetDiagnostics, poolDiagnostics)，再手动刷新；不自动扫描场景或定位全局 Context。不要把 EditorFoundation 引入运行时程序集。只读视图不修改资产，亦不估算显存大小。

## S2-07：包级启动与冻结版本

S2-07 已完成，G2 本地通用验收通过（373/373，失败/跳过均为 0）。最新复验入口：`python3 Tools/S2/run_package_tests.py`（373 项）及 `python3 Tools/S2/verify_package.py`。新增 PackageStartup/IPackageReadiness，接入 AssetsService 门禁、失败清理、兼容回退及显式重试；生产装配必须注入包依赖。配置与装配样例见 [ADR-011](../Docs/ADR/ADR-011-Package-Startup.md)，最终验收见 [报告](../Docs/S2-07ExecutionReport.md)。上方计数/下一项为历史快照。G2 本地验收不外推平台；真实包文件系统/CDN 后端尚待 S4，未启动 S3。

## S3-07：UI 集成边界与 Standard Profile

S3-07 已完成：主工程 401/401，隔离工程 28/28 项通过。最新测试入口 `python3 Tools/S3/run_ui_boundary_tests.py`（401 项），校验入口 `python3 Tools/S3/verify_ui_boundary.py`。模板固定项在 Build/S3/profile.json，Editor 菜单为 Tools/MiniFramework/Validate UI Template。三个程序集独立于可选 UI SDK/动画库，Pool 继续承担资源所有权；默认 Unity Animator 只提供参数指令和 Scope 停止接缝，完整面板服务留 S3-01。

见 [ADR-012](../Docs/ADR/ADR-012-UI-Integration-Boundaries.md) 和 [报告](../Docs/S3-07ExecutionReport.md)。G2 保持本地通过，G3-Core 未通过，G3-AOT 未通过；字体发布证据仍缺失。上文各阶段计数/下一项为历史记录，当前下一项 S3-01。

## S3-01：UI 会话与缓存

当前实现见 [ADR-013](../Docs/ADR/ADR-013-UI-Sessions.md) 与 [执行报告](../Docs/S3-01ExecutionReport.md)。生产入口为 `GameServices.OpenAsync<HomeView>(owner, operation)`，返回持有 ViewScope 的 IUiSession；关闭使用 CloseAsync，失败后显式 RetryCloseAsync。Prefab 与层级根需自行配置，不修改现有 SampleScene。

回归入口 `python3 Tools/S3/run_ui_tests.py`，验收入口 `python3 Tools/S3/verify_ui.py`。旧无 Owner 重载仅保留历史编译探针。G2 保持本地通过；G3-Core 未通过，G3-AOT 未通过。此前计数/下一项为历史记录；下一项 S3-02。

S3-01 最终本地回归 **454/454** 通过（UI 新增 53，历史 401），S3-02 尚未启动。上文 S3-07 干净编译结果保持历史边界。

## S3-02：字段生成与 Prefab 赋值

创建 Create → MiniFramework → UI Binding Manifest，配置普通 Prefab、partial 面板脚本和 Component Target，再预览、生成、等待编译、赋值保存。订阅通过 UiSubscription.Bind 登记到 ViewScope。步骤和限制见 [ADR-014](../Docs/ADR/ADR-014-UI-Generated-Bindings.md)，结果见 [执行报告](../Docs/S3-02ExecutionReport.md)。测试入口 Tools/S3/run_ui_binding_tests.py，验收入口 Tools/S3/verify_ui_bindings.py。当前 488/488 项本地回归通过，下一项 S3-03；G3-Core 未通过，G3-AOT 未通过。

## S3-03 Config

S3-03 completes pinned Luban generation and Context-owned configuration: 522/522 Unity tests plus 9/9 host pipeline tests passed locally. See [execution report](../Docs/S3-03ExecutionReport.md), [ADR-015](../Docs/ADR/ADR-015-Configuration-Pipeline.md), and [tool commands](../Tools/S3/README.md). Game.Config owns generated Tables and the decoder. Register ConfigModule<Tables> as IConfigService<Tables> alongside PackageStartup and AssetsService; Game.Access.ReadConfig<Tables>() returns only the complete compatible snapshot. The executable composition is in Assets/Tests/S3Config/ConfigModuleTests.cs. Runtime fixtures run in the Editor; Player/AOT remains unvalidated. Earlier task counts/next steps are historical. G3-Core 未通过，G3-AOT 未通过。Next: S3-04 (not started).

## S3-04 Scenes and SessionFlow

S3-04 completes SceneService, a Unity Built-in scene provider and optional-scene SessionFlow: 567/567 Unity tests and 9/9 host pipeline tests passed locally. Register SceneService as ISceneService, SessionFlow as ISessionFlow, and declare the package/scene dependencies in the module registrations. Game.Access.EnterSessionAsync("round", null, operation) needs no scene module. With scenes, use a frozen ResourceKey.Scene(provider, package, version, address); register session tasks, subscriptions, resources and View scopes under the returned Session.Scope. See the executable composition in Assets/Tests/S3Scenes/SceneFlowTests.cs.

Single replaces only service-owned scenes after activating the candidate; it preserves external Bootstrap scenes. Deferred native loads must activate before cleanup and may run Awake/OnEnable. Built-in Player scenes require explicit paths and build inclusion; Editor tests use isolated scene assets without editing Build Settings. YooAsset Bundle/CDN scene adaptation remains unsupported. [ADR-016](../Docs/ADR/ADR-016-Scene-Session-Flow.md) and [report](../Docs/S3-04ExecutionReport.md) define cancellation, recovery and evidence. Earlier counts/next tasks are historical. G3-Core 未通过，G3-AOT 未通过。Next S3-05 is not started.

## S3-05 Input, Device and Unity Audio

S3-05 passed local validation: 638/638 Unity tests (71 new and 567 regression), plus 9/9 host pipeline tests. See [execution context](../Docs/AI/ExecutionContext.md), [ADR-017](../Docs/ADR/ADR-017-Input-Device-Unity-Audio.md), and [report](../Docs/S3-05ExecutionReport.md). RuntimeHostModule implements both IRuntimeScheduler and IApplicationLifecycle. Register InputDeviceService as IInputDeviceService and AudioService as IAudioService with their declared host/assets dependencies. Game.Access.InputDevice() resolves scoped subscriptions; PlayAudioAsync loads an AudioClip ResourceKey through AssetsService. Use Session.Scope for round sounds and ViewScope for UI input. Current input mode is Both; ProjectSettings are preserved.

UnityAudioBackend clears/stops a source before returning the clip lease and confirms root destruction at shutdown. Application pause and manual pause are independent. Sources are pooled; short-clip preloading should retain the same Assets lease in App Scope. Device/actual audibility/HostAudio/Player/AOT and performance measurements remain unvalidated. Earlier counts and next tasks are historical. G3-Core 未通过，G3-AOT 未通过。Next: S3-06, not started.

S4-11 observation OBS-S411-01: external ByteGame warning-source timestamps refreshed during regression; no pre-task SDK hashes, so provenance/content changes remain unconfirmed. Sources are preserved. The freshness gate covers framework-owned sources; post-refresh isolated contracts passed. The retained Console snapshot is not continuous across reloads. Native SDK/server/G3-AOT/G4 certification remains pending.
