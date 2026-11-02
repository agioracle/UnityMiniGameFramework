using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Platform;
using MiniFramework.Providers.WeChat;
using MiniFramework.Runtime;
using MiniFramework.WeChat.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.S407Memory.Tests
{
    public sealed class MemoryCompositionTests
    {
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        static OperationContext Wait => new OperationContext(default, TimeSpan.FromSeconds(5));
        sealed class Target : IModule, IPrefetchControl, IResourceTrimTarget
        {
            public string Id => "memory-test-target";
            public IReadOnlyList<string> Dependencies => Array.Empty<string>();
            public int Trims, Suppressions, Budget;
            public bool Hold, SawSuppressed, Disposed;
            UniTaskCompletionSource<Result> pending;
            public bool IsPrefetchSuppressed => Suppressions > 0;
            public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation) => UniTask.FromResult(Result.Success());
            public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup) { Disposed = true; return UniTask.FromResult(Result.Success()); }
            public IDisposable SuppressPrefetch() { Suppressions++; return new ReleaseGate(this); }
            sealed class ReleaseGate : IDisposable { Target target; public ReleaseGate(Target target) { this.target=target; } public void Dispose() { if(target==null)return;target.Suppressions--;target=null; } }
            public UniTask<Result> TrimIdleAsync(int budget, CleanupContext cleanup)
            { Trims++; Budget=budget;SawSuppressed=IsPrefetchSuppressed; if(!Hold)return UniTask.FromResult(Result.Success());pending=new UniTaskCompletionSource<Result>();return pending.Task; }
            public void Complete() { Hold=false;pending?.TrySetResult(Result.Success()); }
        }
        sealed class Env
        {
            public readonly MemoryApi Api;
            public readonly PlatformService Platform;
            public readonly PlatformMemoryPressureSource Source;
            public readonly Target Target = new Target();
            public readonly MemoryPressureController Pressure;
            public readonly GameContext Context;
            public Env(bool pressure=true, IWeChatApi legacy=null, MemoryApi shared=null, bool includeSource=true)
            {
                Api=shared??new MemoryApi();Platform=new PlatformService(new WeChatBackend(legacy??Api));Source=new PlatformMemoryPressureSource(Platform);
                Pressure=new MemoryPressureController(Source,Target,Target,2,Cleanup,schedulerModuleId:Target.Id,poolModuleId:Target.Id,sourceModuleId:Source.Id);
                var modules=new List<ModuleRegistration>{Reg(Platform,typeof(IPlatformCapabilities),typeof(IPlatformLifecycle)),Reg(Target)};
                if(includeSource)modules.Add(Reg(Source,typeof(IMemoryPressureSource)));
                if(pressure)modules.Add(Reg(Pressure,typeof(IMemoryPressureHandler)));
                Context=new GameContext(modules,Cleanup);
            }
            static ModuleRegistration Reg(IModule module,params Type[] services)=>new ModuleRegistration(module.Id,module.Dependencies,()=>module,services);
            public async UniTask Start(){Check(await Context.StartAsync(Wait));}
            public async UniTask Finish()
            {
                Target.Complete();Api.FailOff=Api.FailOffAfter=false;Api.Inner.SavedInitialize?.Invoke(0);
                var result=await Context.CloseAsync(Wait);if(!result.IsSuccess)result=await Context.RetryCleanupAsync(Cleanup,Wait);Check(result);
                Assert.AreEqual(0,Context.RetainedModuleCount);Assert.AreEqual(0,Source.SubscriberCount);Assert.AreEqual(0,Target.Suppressions);
            }
        }
        readonly List<Env> envs=new List<Env>();
        Env New(bool pressure=true,IWeChatApi legacy=null,MemoryApi shared=null,bool includeSource=true){var env=new Env(pressure,legacy,shared,includeSource);envs.Add(env);return env;}
        static void Check(Result value)=>Assert.IsTrue(value.IsSuccess,value.IsSuccess?"":value.Error.Code);
        static async UniTask Frames(int count=5){for(int i=0;i<count;i++)await UniTask.Yield();}
        [UnityTearDown] public IEnumerator Teardown()=>UniTask.ToCoroutine(async()=>{foreach(var env in envs)await env.Finish();envs.Clear();});
        [UnityTest] public IEnumerator LegacyAdapterFailsExplicitlyWithoutLeavingBindings()=>UniTask.ToCoroutine(async()=>
        {var legacy=new ScriptedWeChatApi();var env=New(legacy:legacy);var result=await env.Context.StartAsync(Wait);Assert.IsFalse(result.IsSuccess);Assert.AreEqual("platform.memory.unavailable",result.Error.Code);Check(await env.Context.CloseAsync(Wait));Assert.AreEqual(0,env.Target.Trims);Assert.IsEmpty(legacy.Shows);Assert.IsEmpty(legacy.Hides);});
        [UnityTest] public IEnumerator MissingSourceDependencyFailsBeforeNativeSetup()=>UniTask.ToCoroutine(async()=>
        {var env=New(includeSource:false);Assert.IsFalse((await env.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,env.Api.Inner.InitializeCalls);Assert.AreEqual(0,env.Api.OnCalls);Assert.AreEqual(0,env.Target.Trims);});
        [UnityTest] public IEnumerator SourceIsColdAndRequiresSuccessfulInitialization()=>UniTask.ToCoroutine(async()=>
        {var env=New(false);Assert.AreEqual(0,env.Api.OnCalls);Assert.Throws<InvalidOperationException>(()=>env.Source.Subscribe(()=>{}));await env.Start();Assert.IsTrue(env.Platform.Capabilities.Get(PlatformFeature.MemoryWarning).IsUsable);Assert.AreEqual(0,env.Source.SubscriberCount);});
        [UnityTest] public IEnumerator WorkerNotificationRunsOnMainThreadAndVisibilityIsPreserved()=>UniTask.ToCoroutine(async()=>
        {var env=New(false);await env.Start();int calls=0,thread=0;var main=Thread.CurrentThread.ManagedThreadId;using(env.Source.Subscribe(()=>{calls++;thread=Thread.CurrentThread.ManagedThreadId;})){env.Api.Inner.Hide();await Frames();Assert.AreEqual(PlatformVisibility.Background,env.Platform.Visibility);var worker=Task.Run(()=>env.Api.Raise());while(!worker.IsCompleted)await UniTask.Yield();await Frames();Assert.AreEqual(1,calls);Assert.AreEqual(main,thread);Assert.AreEqual(PlatformVisibility.Background,env.Platform.Visibility);}Assert.AreEqual(0,env.Api.Inner.CreateCalls);});
        [UnityTest] public IEnumerator ShowAndHideCannotTriggerResourceTrim()=>UniTask.ToCoroutine(async()=>
        {var env=New();await env.Start();env.Api.Inner.Show();env.Api.Inner.Hide();await Frames();Assert.AreEqual(0,env.Target.Trims);env.Api.Raise();await Frames();Assert.AreEqual(1,env.Target.Trims);Assert.AreEqual(2,env.Target.Budget);Assert.IsTrue(env.Target.SawSuppressed);Assert.AreEqual(0,env.Target.Suppressions);});
        [UnityTest] public IEnumerator BurstCoalescesWhileActualTrimIsInFlight()=>UniTask.ToCoroutine(async()=>
        {var env=New();env.Target.Hold=true;await env.Start();env.Api.Raise();env.Api.Raise();env.Api.Raise();await Frames();Assert.AreEqual(1,env.Target.Trims);Assert.AreEqual(2,env.Pressure.CoalescedCount);Assert.IsTrue(env.Target.IsPrefetchSuppressed);env.Target.Complete();await Frames();Assert.AreEqual(0,env.Target.Suppressions);env.Api.Raise();await Frames();Assert.AreEqual(2,env.Target.Trims);});
        [UnityTest] public IEnumerator ClosingKeepsActualTrimAndDependenciesUntilCompletion()=>UniTask.ToCoroutine(async()=>
        {var env=New();env.Target.Hold=true;await env.Start();env.Api.Raise();await Frames();var closed=await env.Context.CloseAsync(new OperationContext(default,TimeSpan.FromMilliseconds(1)));Assert.AreEqual(ErrorKind.Timeout,closed.Error.Kind);Assert.IsFalse(env.Target.Disposed);Assert.Greater(env.Context.RetainedModuleCount,0);Assert.AreEqual(0,env.Source.SubscriberCount);env.Api.Raise();await Frames();Assert.AreEqual(1,env.Target.Trims);env.Target.Complete();Check(await env.Context.CloseAsync(Wait));Assert.IsTrue(env.Target.Disposed);Assert.AreEqual(0,env.Target.Suppressions);});
        [UnityTest] public IEnumerator ConsumerDisposeAndThrowCannotStopOtherListeners()=>UniTask.ToCoroutine(async()=>
        {var env=New(false);await env.Start();int healthy=0;var broken=env.Source.Subscribe(()=>throw new InvalidOperationException());var good=env.Source.Subscribe(()=>healthy++);env.Api.Raise();await Frames();Assert.AreEqual(1,healthy);Assert.AreEqual(1,env.Source.CallbackErrors);broken.Dispose();broken.Dispose();env.Api.Raise();await Frames();Assert.AreEqual(2,healthy);Assert.AreEqual(1,env.Source.CallbackErrors);good.Dispose();Assert.AreEqual(0,env.Source.SubscriberCount);});
        [UnityTest] public IEnumerator FailedNativeDetachRetainsModuleAndOnlyExplicitRetryReleases()=>UniTask.ToCoroutine(async()=>
        {var env=New();await env.Start();var stale=env.Api.Memory[0];env.Api.FailOff=true;var result=await env.Context.CloseAsync(Wait);Assert.IsFalse(result.IsSuccess);Assert.Greater(env.Context.RetainedModuleCount,0);stale();await Frames();Assert.AreEqual(0,env.Target.Trims);env.Api.FailOff=false;Check(await env.Context.RetryCleanupAsync(Cleanup,Wait));Assert.AreEqual(1,env.Api.OnCalls);Assert.AreEqual(2,env.Api.OffCalls);Assert.IsEmpty(env.Api.Memory);});
        [UnityTest] public IEnumerator OldGenerationCallbackCannotEnterNewSource()=>UniTask.ToCoroutine(async()=>
        {var api=new MemoryApi();var old=New(shared:api);await old.Start();var stale=api.Memory[0];Check(await old.Context.CloseAsync(Wait));var next=New(shared:api);await next.Start();stale();await Frames();Assert.AreEqual(0,next.Target.Trims);api.Raise();await Frames();Assert.AreEqual(1,next.Target.Trims);Assert.AreEqual(1,api.Memory.Count);});
        [UnityTest] public IEnumerator RepeatedContextCyclesDrainSourceAndSdkBindings()=>UniTask.ToCoroutine(async()=>
        {for(int i=0;i<5;i++){var env=New();await env.Start();env.Api.Raise();await Frames();Check(await env.Context.CloseAsync(Wait));Assert.IsEmpty(env.Api.Memory);Assert.IsEmpty(env.Api.Inner.Shows);Assert.IsEmpty(env.Api.Inner.Hides);Assert.AreEqual(0,env.Source.ReadDiagnostics().ActiveScopes);Assert.Throws<InvalidOperationException>(()=>env.Source.Subscribe(()=>{}));}});
    }
}
