using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Kernel;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class GameContextTests
{
    private interface IService { }
    private sealed class Module : IModule, IService
    {
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public readonly List<string> Trace;
        public Func<IModuleContext, UniTask<Result>> Initialize;
        public Func<UniTask<Result>> Stop, Dispose;
        public IModuleContext Context;
        public int StopCount, DisposeCount;
        public bool CleanupCanceled;
        public Module(string id, List<string> trace, params string[] dependencies) { Id = id; Trace = trace; Dependencies = dependencies; }
        public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation)
        { Context = context; Trace.Add("init:" + Id); return Initialize != null ? Initialize(context) : UniTask.FromResult(Result.Success()); }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        { StopCount++; CleanupCanceled |= cleanup.ShutdownCancellation.IsCancellationRequested; Trace.Add("stop:" + Id); return Stop != null ? Stop() : UniTask.FromResult(Result.Success()); }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)
        { DisposeCount++; CleanupCanceled |= cleanup.ShutdownCancellation.IsCancellationRequested; Trace.Add("dispose:" + Id); return Dispose != null ? Dispose() : UniTask.FromResult(Result.Success()); }
    }
    private sealed class Registry : ICleanupRegistry
    {
        public CleanupRegistryState State { get; private set; }
        public bool Pending;
        public int CloseCount;
        public Result<ICleanupTicket> Register(IAsyncCleanup cleanup) => Result<ICleanupTicket>.Failure(new FrameworkError(ErrorKind.NotSupported,"test.no.registration"));
        public UniTask<CleanupReport> CloseAsync(CleanupContext cleanup)
        { CloseCount++; State = Pending ? CleanupRegistryState.Closing : CleanupRegistryState.Closed; return UniTask.FromResult(new CleanupReport(Pending ? 1 : 0, 0, !Pending)); }
    }
    private static ModuleRegistration R(Module module, params Type[] services) => new ModuleRegistration(module.Id,module.Dependencies,() => module,services);
    private static GameContext C(params ModuleRegistration[] registrations) => new GameContext(registrations,_ => new NoResourcesCleanupRegistry(),new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan));
    private static Result Failure() => Result.Failure(new FrameworkError(ErrorKind.Provider,"test.init.failure"));
    [UnityTest] public IEnumerator InvalidGraphsNeverInvokeFactories() => UniTask.ToCoroutine(async () =>
    {
        var calls = 0;
        Func<IModule> factory = () => { calls++; return null; };
        var graphs = new[] {
            new[] { new ModuleRegistration("a",new[]{"missing"},factory) },
            new[] { new ModuleRegistration("a",new[]{"a"},factory) },
            new[] { new ModuleRegistration("a",new[]{"b"},factory),new ModuleRegistration("b",new[]{"a"},factory) },
            new[] { new ModuleRegistration("a",Array.Empty<string>(),factory),new ModuleRegistration("a",Array.Empty<string>(),factory) },
            new[] { new ModuleRegistration("a",Array.Empty<string>(),factory,typeof(IService)),new ModuleRegistration("b",Array.Empty<string>(),factory,typeof(IService)) }
        };
        foreach (var graph in graphs) { var c=C(graph); Assert.AreEqual(ErrorKind.Dependency,(await c.StartAsync(OperationContext.Unlimited())).Error.Kind); await c.CloseAsync(OperationContext.Unlimited()); }
        Assert.AreEqual(0,calls);
    });
    [UnityTest] public IEnumerator StableTopologyAndReverseShutdown() => UniTask.ToCoroutine(async () =>
    {
        var trace=new List<string>(); var a=new Module("a",trace);var b=new Module("b",trace,"a");var c=new Module("c",trace,"a");
        var context=C(R(c),R(b),R(a)); Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
        await context.CloseAsync(OperationContext.Unlimited());
        CollectionAssert.AreEqual(new[]{"init:a","init:c","init:b","stop:b","stop:c","stop:a","dispose:b","dispose:c","dispose:a"},trace);
        Assert.AreEqual(GameContextState.Closed,context.State); Assert.AreEqual(0,context.RetainedModuleCount);
    });
    [UnityTest] public IEnumerator ResolverOnlyExposesDeclaredReadyDependencies() => UniTask.ToCoroutine(async () =>
    {
        var trace=new List<string>();var a=new Module("a",trace);var b=new Module("b",trace,"a");var c=new Module("c",trace);
        a.Initialize=ctx=>{Assert.IsFalse(ctx.Services.TryGet<IService>(out _));return UniTask.FromResult(Result.Success());};
        b.Initialize=ctx=>{Assert.IsTrue(ctx.Services.TryGet<IService>(out var value));Assert.AreSame(a,value);return UniTask.FromResult(Result.Success());};
        c.Initialize=ctx=>{Assert.IsFalse(ctx.Services.TryGet<IService>(out _));return UniTask.FromResult(Result.Success());};
        var context=C(R(b),R(a,typeof(IService)),R(c));Assert.IsFalse(context.TryGet<IService>(out _));
        Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);Assert.IsTrue(context.TryGet<IService>(out _));
        await context.CloseAsync(OperationContext.Unlimited());Assert.IsFalse(context.TryGet<IService>(out _));Assert.IsFalse(b.Context.Services.TryGet<IService>(out _));
    });
    [UnityTest] public IEnumerator FailedPartialInitializationRollsBackEveryCreatedObject() => UniTask.ToCoroutine(async () =>
    {
        var trace=new List<string>();var a=new Module("a",trace);var b=new Module("b",trace,"a");var c=new Module("c",trace,"b");
        b.Initialize=ctx=>UniTask.FromResult(Failure());b.Stop=()=>throw new InvalidOperationException("stop failed");
        var context=C(R(a),R(b),R(c));var result=await context.StartAsync(OperationContext.Unlimited());
        Assert.AreEqual("test.init.failure",result.Error.Code);Assert.AreEqual(ErrorKind.CleanupFailed,(await context.CloseAsync(OperationContext.Unlimited())).Error.Kind);
        Assert.AreEqual(1,a.DisposeCount);Assert.AreEqual(1,b.DisposeCount);Assert.AreEqual(1,c.DisposeCount);Assert.IsFalse(trace.Contains("init:c"));
        Assert.IsFalse(b.CleanupCanceled);Assert.Greater(context.CleanupDiagnostics.Count,0);Assert.AreEqual(ModuleState.CleanupFailed,context.GetModuleState("b"));
    });
    [UnityTest] public IEnumerator FactoryFailureRollsBackPreviouslyCreatedObjects() => UniTask.ToCoroutine(async () =>
    {
        var a=new Module("a",new List<string>());var context=C(R(a),new ModuleRegistration("b",new[]{"a"},()=>throw new InvalidOperationException("factory")));
        Assert.AreEqual(ErrorKind.Unexpected,(await context.StartAsync(OperationContext.Unlimited())).Error.Kind);
        await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,a.DisposeCount);Assert.IsFalse(a.Trace.Contains("init:a"));
    });
    [UnityTest] public IEnumerator MetadataFailureStillDisposesCreatedModule() => UniTask.ToCoroutine(async () =>
    {
        var a=new Module("actual",new List<string>());var context=C(new ModuleRegistration("wrong",Array.Empty<string>(),()=>a));
        Assert.IsFalse((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,a.DisposeCount);
    });
    [UnityTest] public IEnumerator PreCanceledAndZeroDeadlineCreateNothing() => UniTask.ToCoroutine(async () =>
    {
        var count=0;var cancel=new CancellationTokenSource();cancel.Cancel();
        foreach(var token in new[]{cancel.Token,CancellationToken.None})
        {var context=C(new ModuleRegistration("a",Array.Empty<string>(),()=>{count++;return null;}));var result=await context.StartAsync(new OperationContext(token,TimeSpan.Zero));
        Assert.AreEqual(token.IsCancellationRequested?ErrorKind.Canceled:ErrorKind.Timeout,result.Error.Kind);await context.CloseAsync(OperationContext.Unlimited());}
        Assert.AreEqual(0,count);cancel.Dispose();
    });
    [UnityTest] public IEnumerator CanceledInitializationIsRetainedUntilLateSuccess() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var cancel=new CancellationTokenSource();var a=new Module("a",new List<string>());
        a.Initialize=ctx=>source.Task;var context=C(R(a,typeof(IService)));var start=context.StartAsync(OperationContext.Unlimited(cancel.Token));cancel.Cancel();
        Assert.AreEqual(ErrorKind.Canceled,(await start).Error.Kind);Assert.AreEqual(0,a.DisposeCount);Assert.AreEqual(GameContextState.Closing,context.State);
        Assert.AreEqual(ErrorKind.Timeout,(await context.CloseAsync(default)).Error.Kind);Assert.AreEqual(0,a.StopCount);
        source.TrySetResult(Result.Success());Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        Assert.IsFalse(context.TryGet<IService>(out _));Assert.AreEqual(1,a.DisposeCount);Assert.IsFalse(a.CleanupCanceled);cancel.Dispose();
    });
    [UnityTest] public IEnumerator RealTimeoutIgnoresTimeScaleAndObservesLateFailure() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var a=new Module("a",new List<string>());a.Initialize=ctx=>source.Task;var context=C(R(a));
        var previous=Time.timeScale;
        try {Time.timeScale=0;var result=await context.StartAsync(new OperationContext(CancellationToken.None,TimeSpan.FromMilliseconds(30)));
        Assert.AreEqual(ErrorKind.Timeout,result.Error.Kind);Assert.AreEqual(0,a.DisposeCount);source.TrySetResult(Failure());await context.CloseAsync(OperationContext.Unlimited());
        Assert.AreEqual("test.init.failure",context.LateCompletionDiagnostics[0].Code);}
        finally {Time.timeScale=previous;source.TrySetResult(Result.Success());}
    });
    [UnityTest] public IEnumerator ConcurrentCloseWaitersJoinOneDispose() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var a=new Module("a",new List<string>());a.Dispose=()=>source.Task;var context=C(R(a));await context.StartAsync(OperationContext.Unlimited());
        var first=context.CloseAsync(OperationContext.Unlimited());var second=context.CloseAsync(OperationContext.Unlimited());
        Assert.AreEqual(1,a.DisposeCount);Assert.AreEqual(ErrorKind.Timeout,(await context.CloseAsync(default)).Error.Kind);
        source.TrySetResult(Result.Success());Assert.IsTrue((await first).IsSuccess);Assert.IsTrue((await second).IsSuccess);
        Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(1,a.StopCount);Assert.AreEqual(1,a.DisposeCount);
    });
    [UnityTest] public IEnumerator CloseDuringStartRejectsLateReadyAndRestart() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var a=new Module("a",new List<string>());a.Initialize=ctx=>source.Task;var context=C(R(a));var start=context.StartAsync(OperationContext.Unlimited());
        var close=context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(ErrorKind.Canceled,(await start).Error.Kind);
        source.TrySetResult(Result.Success());await close;Assert.AreEqual(ErrorKind.InvalidState,(await context.StartAsync(OperationContext.Unlimited())).Error.Kind);Assert.AreEqual(1,a.DisposeCount);
    });
    [UnityTest] public IEnumerator DisposeFailureRetainsOwnersAndDoesNotSkipDependencies() => UniTask.ToCoroutine(async () =>
    {
        var trace=new List<string>();var a=new Module("a",trace);var b=new Module("b",trace,"a");b.Dispose=()=>throw new InvalidOperationException("release");
        var context=C(R(a),R(b));await context.StartAsync(OperationContext.Unlimited());Assert.IsFalse((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        Assert.AreEqual(2,context.RetainedModuleCount);Assert.AreEqual(1,a.DisposeCount);await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,b.DisposeCount);
    });
    [UnityTest] public IEnumerator UndrainedRegistryNeverClaimsClosed() => UniTask.ToCoroutine(async () =>
    {
        var registry=new Registry{Pending=true};var a=new Module("a",new List<string>());var context=new GameContext(new[]{R(a)},_=>registry,new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan));
        await context.StartAsync(OperationContext.Unlimited());Assert.AreEqual(ErrorKind.Timeout,(await context.CloseAsync(default)).Error.Kind);
        Assert.AreEqual(GameContextState.Closing,context.State);Assert.AreEqual(1,context.RetainedModuleCount);
        registry.Pending=false;Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
    });
    [UnityTest] public IEnumerator CloseBeforeStartIsIdempotent() => UniTask.ToCoroutine(async () =>
    {
        var count=0;var context=C(new ModuleRegistration("a",Array.Empty<string>(),()=>{count++;return null;}));
        await context.CloseAsync(OperationContext.Unlimited());await context.CloseAsync(OperationContext.Unlimited());
        Assert.AreEqual(0,count);Assert.AreEqual(ErrorKind.InvalidState,(await context.StartAsync(OperationContext.Unlimited())).Error.Kind);
    });
    [UnityTest] public IEnumerator InitializeExceptionAndDefaultResultCannotBecomeReady() => UniTask.ToCoroutine(async () =>
    {
        foreach(var throws in new[]{false,true})
        {
            var module=new Module("a",new List<string>());
            module.Initialize=ctx=>throws ? throw new InvalidOperationException("init") : UniTask.FromResult(default(Result));
            var context=C(R(module));var result=await context.StartAsync(OperationContext.Unlimited());
            Assert.AreEqual(throws ? ErrorKind.Unexpected : ErrorKind.InvalidResult,result.Error.Kind);
            await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,module.DisposeCount);
        }
    });
    [UnityTest] public IEnumerator CancelingCloseWaiterDoesNotCancelSharedDrain() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var a=new Module("a",new List<string>());a.Dispose=()=>source.Task;
        var context=C(R(a));await context.StartAsync(OperationContext.Unlimited());
        var first=context.CloseAsync(OperationContext.Unlimited());var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.AreEqual(ErrorKind.Canceled,(await context.CloseAsync(OperationContext.Unlimited(cancel.Token))).Error.Kind);
        Assert.IsFalse(a.CleanupCanceled);Assert.AreEqual(1,a.DisposeCount);
        var racingCancel = new CancellationTokenSource();
        var racingWaiter = context.CloseAsync(OperationContext.Unlimited(racingCancel.Token));
        // Both signals arrive before this waiter resumes on the next PlayerLoop batch.
        racingCancel.Cancel(); source.TrySetResult(Result.Success());
        Assert.AreEqual(ErrorKind.Canceled, (await racingWaiter).Error.Kind);
        Assert.IsTrue((await first).IsSuccess);cancel.Dispose();racingCancel.Dispose();
    });
    [UnityTest] public IEnumerator PendingRegistryHoldsDependencyUntilDrained() => UniTask.ToCoroutine(async () =>
    {
        var trace=new List<string>();var a=new Module("a",trace);var b=new Module("b",trace,"a");var registry=new Registry{Pending=true};
        var context=new GameContext(new[]{R(a),R(b)},id=>id=="b" ? (ICleanupRegistry)registry : new NoResourcesCleanupRegistry(),new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan));
        await context.StartAsync(OperationContext.Unlimited());var close=context.CloseAsync(OperationContext.Unlimited());
        await UniTask.Yield();Assert.AreEqual(0,a.DisposeCount);Assert.AreEqual(1,b.DisposeCount);
        registry.Pending=false;Assert.IsTrue((await close).IsSuccess);Assert.AreEqual(1,a.DisposeCount);
    });

    [UnityTest] public IEnumerator SharedRegistryIsRejectedWithoutDoubleClosingIt() => UniTask.ToCoroutine(async () =>
    {
        var registry=new Registry();var trace=new List<string>();var a=new Module("a",trace);var b=new Module("b",trace);
        var context=new GameContext(new[]{R(a),R(b)},_=>registry,new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan));
        Assert.IsFalse((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
        await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,registry.CloseCount);
        Assert.AreEqual(1,a.DisposeCount);Assert.AreEqual(1,b.DisposeCount);
    });

}
