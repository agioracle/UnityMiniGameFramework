using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Config;
using Game.Config.Generated;
using Game.Features;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Host;
using MiniFramework.Pool;
using MiniFramework.Config;
using MiniFramework.Scenes;
using MiniFramework.UI;
using MiniFramework.Audio;
using MiniFramework.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Boot
{
    // Consumer composition. No framework service knows about this sample or generated tables.
    public sealed class LocalSampleGame:ILocalSampleCommands,IReviveSampleCommands
    {
        public static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
        public static OperationContext Wait=>OperationContext.Unlimited();
        public readonly GameContext Context;public readonly GameServices Access;
        public readonly Scope App=new Scope("local-sample",ScopeKind.App);
        public readonly SampleResourceProvider Provider;
        public readonly SamplePackageBackend PackageBackend;
        public readonly UnitySceneProvider SceneProvider;public readonly SessionFlow Flow;public readonly UiService UI;
        public readonly GameObjectPool Pool;public readonly InputDeviceService Input;public readonly AudioService Audio;
        public readonly UnityAudioBackend AudioBackend=new UnityAudioBackend(8);
        public readonly ConfigModule<Tables> Config;public readonly RuntimeHostModule Host;
        public IUiSession<SampleHomePanel> Home {get;private set;}public IUiSession<SampleLevelPanel> Level {get;private set;}
        int legacyCoins;public int Coins=>ReviveStore==null?legacyCoins:(int)ReviveStore.Snapshot.Coins;public int GameplayTicks {get;private set;}public int TransitionCount {get;private set;}
        public bool Busy {get;private set;}public bool Muted {get;private set;}public Result LastResult {get;private set;}
        public UniTask<Result> Pending {get;private set;}=UniTask.FromResult(Result.Success());
        public UniTask<Result> SoundCompletion {get;private set;}=UniTask.FromResult(Result.Success());
        public Action<string> StatusChanged;public IScope CurrentScope=>Flow.Current?.Scope;
        public readonly IRevivePersistenceView ReviveStore;public readonly IReviveService Revive;
        public IUiSession<SampleRevivePanel> ReviveView {get;private set;}readonly ILocalSampleExtension reviveOptions;
        bool closing,runConfirmed;int activeLevel;
        bool MatchingRun=>Revive==null||runConfirmed&&Revive.Snapshot.Level==activeLevel;
        public LocalSampleGame(RuntimeHost host,Transform uiRoot,Func<string,AsyncOperation> load=null,Func<Scene,AsyncOperation> unload=null,ILocalSampleExtension revive=null)
        {
            reviveOptions=revive;Provider=new SampleResourceProvider(revive!=null);PackageBackend=new SamplePackageBackend(revive!=null);
            Host=new RuntimeHostModule(host);
            var package=new PackageStartup(new PackageProfile(SampleContent.Package,"local-1",GeneratedConfigInfo.Schema,SampleContent.FirstPlay,new[]{new PackageCandidate(PackageSource.BuiltIn,SampleContent.Version,"Resources/MiniFrameworkSample")}),PackageBackend);
            var scheduler=new ResourceWorkScheduler(new ResourceScheduleBudget(1,2,4,TimeSpan.FromMilliseconds(2)),hostModuleId:Host.Id);
            var assets=new AssetsService(Provider,SampleContent.Package,SampleContent.Version,Cleanup,scheduler:scheduler,schedulerModuleId:scheduler.Id,packageReadiness:package,packageModuleId:package.Id);
            Pool=new GameObjectPool(assets,scheduler,new PoolCapacity(4,2,4),Cleanup,hostModuleId:Host.Id);
            var panels=new List<PanelRegistration>{PanelRegistration.For<SampleHomePanel>(Asset<GameObject>("ui/home"),cache:PanelCachePolicy.RetainInstance),PanelRegistration.For<SampleLevelPanel>(Asset<GameObject>("ui/level"),cache:PanelCachePolicy.RetainInstance)};if(revive!=null)panels.Add(PanelRegistration.For<SampleRevivePanel>(Asset<GameObject>("ui/revive"),cache:PanelCachePolicy.RetainInstance));
            UI=new UiService(new PooledPanelSource(Pool),panels,new[]{new UiLayerHost(0,uiRoot)},Cleanup,maxCached:revive==null?2:3);
            Config=new ConfigModule<Tables>(assets,package,GameConfigDecoder.Definition(SampleContent.Provider),GameConfigDecoder.Decode,Cleanup);
            SceneProvider=new UnitySceneProvider(SampleContent.Provider,SampleContent.Package,SampleContent.Version,SampleContent.Scenes,load,unload);
            var scenes=new SceneService(SceneProvider,SampleContent.Provider,SampleContent.Package,SampleContent.Version,Cleanup,package,package.Id);
            Flow=new SessionFlow(Cleanup,scenes);
            Input=new InputDeviceService(new UnityPointerSource(),Host);
            Audio=new AudioService(assets,AudioBackend,Host,Cleanup,8);
            var modules=new List<ModuleRegistration>();
            Add(modules,Host,typeof(IRuntimeScheduler),typeof(IApplicationLifecycle));Add(modules,package,typeof(IPackageReadiness),typeof(IConfigPackageReadiness));
            Add(modules,scheduler,typeof(IResourceWorkScheduler));Add(modules,assets,typeof(IAssetsService));Add(modules,Pool,typeof(IPoolService<GameObject>));
            Add(modules,UI,typeof(IUiService));Add(modules,Config,typeof(IConfigService<Tables>));Add(modules,scenes,typeof(ISceneService));Add(modules,Flow,typeof(ISessionFlow));
            Add(modules,Input,typeof(IInputDeviceService));Add(modules,Audio,typeof(IAudioService));
            if(revive!=null)
            {
                ReviveStore=revive.Store;Revive=revive.Revive;
                foreach(var entry in revive.Modules)Add(modules,entry.Module,new List<Type>(entry.Services).ToArray());
            }
            Context=new GameContext(modules,Cleanup);Access=new GameServices(Context);
        }
        static void Add(List<ModuleRegistration> list,IModule module,params Type[] services)=>list.Add(new ModuleRegistration(module.Id,module.Dependencies,()=>module,services));
        public static ResourceKey Asset<T>(string address) where T:class=>new ResourceKey(SampleContent.Provider,SampleContent.Package,SampleContent.Version,address,typeof(T));
        public async UniTask<Result> StartAsync()
        {
            var started=await Context.StartAsync(Wait);if(!started.IsSuccess){LastResult=started;Report(started);return started;}
            return await Transition(ReviveStore?.Snapshot.Level??0,true);
        }
        public Result RequestLevel(int id)=>Request(()=>Transition(id));public Result RequestHome()=>Request(()=>Transition(0));
        public Result RequestRetry()=>Request(Recover);
        Result Request(Func<UniTask<Result>> operation)
        {
            if(closing||Busy||Context.State!=GameContextState.Ready)return Fail("sample.command.busy");
            Busy=true;var completion=new UniTaskCompletionSource<Result>();Pending=completion.Task;
            var registered=App.RunTask("command/"+Guid.NewGuid().ToString("N"),async token=>
            {
                Result result;try{result=await operation();}catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.Unexpected,"sample.command.exception",e.Message));}
                LastResult=result;Busy=false;Report(result);completion.TrySetResult(result);return result;
            });
            if(!registered.IsSuccess){Busy=false;var failed=Result.Failure(registered.Error);completion.TrySetResult(failed);return failed;}return Result.Success();
        }
        async UniTask<Result> Transition(int id,bool restore=false)
        {
            if(id!=0&&Access.ReadConfig<Tables>().Value.TbLevel.GetOrDefault(id)==null)return Fail("sample.level.unknown");
            if(Revive!=null&&id>0&&!restore&&(Revive.Busy||Revive.RecoveryRequired))return Fail("sample.run.recovery_required");
            ResourceKey? key=id==0?(ResourceKey?)null:ResourceKey.Scene(SampleContent.Provider,SampleContent.Package,SampleContent.Version,Access.ReadConfig<Tables>().Value.TbLevel.Get(id).SceneAddress);
            var previous=Flow.Current;var result=await Access.EnterSessionAsync(id==0?"home":"level/"+id,key,Wait);
            // A precommit load failure leaves the old screen. A postcommit cleanup failure has a new Current.
            if(Flow.Current!=previous)
            {
                activeLevel=id;TransitionCount++;runConfirmed=Revive==null||restore||id==0;Result begin=Result.Success();
                // Scene selection must succeed before advancing persisted gameplay. A rejected run gates the new screen.
                if(Revive!=null&&id>0&&!restore){begin=await Revive.BeginRunAsync(id,Wait);runConfirmed=begin.IsSuccess;}
                await RenderCurrent();if(!begin.IsSuccess)return begin;
            }
            return result.IsSuccess?Result.Success():Result.Failure(result.Error);
        }
        async UniTask<Result> Recover()
        {var result=await Flow.RetryRecoveryAsync(Cleanup);if(result.IsSuccess)await RenderCurrent();return result;}
        async UniTask RenderCurrent()
        {
            // Recovery can change presentation inside the SAME committed Session.
            // Drain the recovery Home before restoring a Level; never leave an unreferenced UI session.
            if(Home!=null){var closed=await Home.CloseAsync(Wait);if(!closed.IsSuccess)throw new InvalidOperationException(closed.Error.ToString());}
            if(Level!=null){var closed=await Level.CloseAsync(Wait);if(!closed.IsSuccess)throw new InvalidOperationException(closed.Error.ToString());}
            if(ReviveView!=null){var closed=await ReviveView.CloseAsync(Wait);if(!closed.IsSuccess)throw new InvalidOperationException(closed.Error.Code);}ReviveView=null;
            Home=null;Level=null;var owner=CurrentScope;if(owner==null)return;
            if(activeLevel==0||Flow.State==SessionFlowState.Recovering)
            {
                var panel=await Access.OpenAsync<SampleHomePanel>(owner,Wait);if(!panel.IsSuccess)throw new InvalidOperationException(panel.Error.ToString());
                Home=panel.Value;Home.Panel.Configure(this,Status(),Flow.State==SessionFlowState.Recovering);
            }
            else
            {
                var panel=await Access.OpenAsync<SampleLevelPanel>(owner,Wait);if(!panel.IsSuccess)throw new InvalidOperationException(panel.Error.ToString());Level=panel.Value;Level.Panel.Configure(this,Status());
                var actor=UnityEngine.Object.FindObjectOfType<SampleLevelActor>();if(actor==null)throw new InvalidOperationException("sample.level.actor_missing");
                owner.TrackSubscription("level.tick",Host.SubscribeTick((_,__)=>{if(owner.State==ScopeState.Open&&(Revive==null||MatchingRun&&!Busy&&Flow.State==SessionFlowState.Ready&&Revive.Snapshot.Phase==ReviveRunPhase.Playing)){GameplayTicks++;actor.Tick();}}));
                var input=Input.Subscribe(owner,p=>{if(p.Phase==PointerPhase.Ended)Collect();});if(!input.IsSuccess)throw new InvalidOperationException(input.Error.Code);
            }
            if(Revive!=null){var overlay=await Access.OpenAsync<SampleRevivePanel>(owner,Wait);if(!overlay.IsSuccess)throw new InvalidOperationException(overlay.Error.Code);ReviveView=overlay.Value;ReviveView.Panel.Configure(this);}
            Report(Result.Success());
        }
        string Status()=>"LOCAL SAMPLE [Platform: Mock] | "+(Flow.State==SessionFlowState.Recovering?"Cleanup failed - Retry":activeLevel==0?"Home":"Level "+activeLevel)+" | Coins "+Coins+(Muted?" | Muted":"");
        void Report(Result result){var text=Status()+(result.IsSuccess?"":" | "+result.Error.Code);StatusChanged?.Invoke(text);if(Home?.Panel)Home.Panel.Status.text=text;if(Level?.Panel)Level.Panel.Status.text=text;if(ReviveView?.Panel)ReviveView.Panel.Refresh();}
        public Result Collect()
        {
            if(closing||Busy||activeLevel==0||Flow.State!=SessionFlowState.Ready||!MatchingRun)return Fail("sample.collect.state");
            if(Revive!=null)return Request(async()=>{var committed=await Revive.CollectAsync(Access.ReadConfig<Tables>().Value.TbLevel.Get(activeLevel).RewardId_Ref.Coins,Wait);return committed;});
            legacyCoins+=Access.ReadConfig<Tables>().Value.TbLevel.Get(activeLevel).RewardId_Ref.Coins;
            var completion=new UniTaskCompletionSource<Result>();SoundCompletion=completion.Task;
            var owner=CurrentScope;var ticket=owner.RunTask("click/"+Guid.NewGuid().ToString("N"),async token=>{var played=await Access.PlayAudioAsync(Asset<AudioClip>("audio/click"),new AudioPlayOptions(AudioChannel.Ui),owner,OperationContext.Unlimited(token));var outcome=played.IsSuccess?Result.Success():Result.Failure(played.Error);completion.TrySetResult(outcome);return outcome;});
            var result=ticket.IsSuccess?Result.Success():Result.Failure(ticket.Error);if(!ticket.IsSuccess)completion.TrySetResult(result);Report(result);return result;
        }
        public Result ToggleMute(){Muted=!Muted;var result=Audio.SetMuted(Muted);Report(result);return result;}
        public async UniTask<Result> CloseAsync()
        {
            closing=true;var app=await App.CloseAsync(Cleanup);while(app.PendingCount>0){await UniTask.Yield();app=App.Cleanup.Snapshot;}
            // Completed command failures are diagnostic, not retained resources. Explicit retry handles actual failed cleanup.
            if(!app.IsFullyDrained)app=await App.RetryFailedAsync(Cleanup);
            var result=await Context.CloseAsync(Wait);if(!result.IsSuccess&&reviveOptions==null)result=await Context.RetryCleanupAsync(Cleanup,Wait);Home=null;Level=null;ReviveView=null;return result;
        }
        public string ReviveStatus=>Revive==null?"Revive disabled":"MOCK / "+ReviveStore.Persistence+" | "+Revive.Snapshot.Phase+" | Revive used: "+Revive.Snapshot.ReviveUsed+" | Coins "+Coins+(ReviveStore.RecoveryRequired?" | Unknown write: close/reopen":Revive.Snapshot.Pending!=null?" | Claim pending (not granted)":" | Confirmed snapshot")+(!MatchingRun?" | Level setup failed: reopen level":"");
        public Result RequestFailure()=>Revive==null||activeLevel==0||!MatchingRun?Fail("sample.revive.disabled"):Request(()=>Revive.FailRunAsync(Wait));
        public Result RequestWatch(CancellationToken view)
        {
            if(Revive==null||activeLevel==0||!MatchingRun)return Fail("sample.revive.disabled");
            return Request(async()=>{using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(view,App.LifetimeCancellation)){var c=Revive.CaptureRevive();if(!c.IsSuccess)return Result.Failure(c.Error);var watched=await c.Value.WaitAsync(OperationContext.Unlimited(cancel.Token));return watched.IsSuccess?Result.Success():Result.Failure(watched.Error);}});
        }
        public Result RequestRecoverClaim()=>Revive==null?Fail("sample.revive.disabled"):Request(async()=>{var r=await Revive.RecoverPendingAsync(Wait);return r.IsSuccess?Result.Success():Result.Failure(r.Error);});
        public Result RequestAbandonClaim()=>Revive==null?Fail("sample.revive.disabled"):Request(()=>Revive.AbandonPendingAsync(Wait));
        public Result RequestReopen(){if(reviveOptions?.Reopen==null||Busy||Revive.Busy)return Fail("sample.reopen.busy");reviveOptions.Reopen();return Result.Success();}
        public void RefreshReviveStatus(){if(ReviveView?.Panel)ReviveView.Panel.Refresh();Report(LastResult);}
        static Result Fail(string code)=>Result.Failure(new FrameworkError(ErrorKind.InvalidState,code));
    }
}
