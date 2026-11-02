using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

public sealed class ScopeTests
{
    private static CleanupContext Unlimited => new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan);
    private static Result Failed => Result.Failure(new FrameworkError(ErrorKind.Provider,"test.release.failed"));
    private sealed class Cleanup : IAsyncCleanup
    {
        public string Id { get; set; }
        public int Calls;
        public bool Canceled;
        public Func<UniTask<Result>> Action=()=>UniTask.FromResult(Result.Success());
        public Cleanup(string id) { Id=id; }
        public UniTask<Result> CleanupAsync(CleanupContext cleanup) { Calls++;Canceled=cleanup.ShutdownCancellation.IsCancellationRequested;return Action(); }
    }
    private sealed class Subscription : IDisposable
    {
        public int Calls;
        public Action Action;
        public void Dispose() { Calls++;Action?.Invoke(); }
    }
    private sealed class Module : IModule
    {
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public IScope Scope;
        public int Disposes;
        public Func<IModuleContext,Result> Initialize;
        public Module(string id,params string[] dependencies) { Id=id;Dependencies=dependencies; }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        { Scope=((IScopedModuleContext)context).Scope;return UniTask.FromResult(Initialize?.Invoke(context) ?? Result.Success()); }
        public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        public UniTask<Result> DisposeAsync(CleanupContext cleanup) { Disposes++;return UniTask.FromResult(Result.Success()); }
    }
    private static ModuleRegistration R(Module m) => new ModuleRegistration(m.Id,m.Dependencies,()=>m);

    [UnityTest] public IEnumerator RegistrationIdentityAndOwnership() => UniTask.ToCoroutine(async () =>
    {
        var registry=new CleanupRegistry();var item=new Cleanup("a");var first=registry.Register(item);
        Assert.AreSame(first.Value,registry.Register(item).Value);
        Assert.AreEqual(ErrorKind.InvalidArgument,registry.Register(new Cleanup("a")).Error.Kind);
        item.Id="changed";Assert.IsFalse(registry.Register(item).IsSuccess);Assert.IsFalse(registry.Register(null).IsSuccess);
        Assert.AreEqual("a",first.Value.Id);Assert.AreEqual(ErrorKind.InvalidResult,first.Value.LastAttempt.Error.Kind);
        await registry.CloseAsync(Unlimited);Assert.AreEqual(1,item.Calls);
    });
    [UnityTest] public IEnumerator ClosingRejectsRegistrationWithoutTakingOwnership() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var registry=new CleanupRegistry();var a=new Cleanup("a"){Action=()=>source.Task};registry.Register(a);
        var report=await registry.CloseAsync(default);Assert.AreEqual(1,report.PendingCount);
        var rejected=new Cleanup("b");Assert.AreEqual(ErrorKind.InvalidState,registry.Register(rejected).Error.Kind);Assert.AreEqual(0,rejected.Calls);
        source.TrySetResult(Result.Success());await registry.CloseAsync(Unlimited);
        Assert.IsFalse(registry.Register(a).IsSuccess);
    });
    [UnityTest] public IEnumerator CleanupRunsInReverseRegistrationOrder() => UniTask.ToCoroutine(async () =>
    {
        var order=new List<string>();var registry=new CleanupRegistry();
        foreach(var id in new[]{"a","b","c"}) registry.Register(new Cleanup(id){Action=()=>{order.Add(id);return UniTask.FromResult(Result.Success());}});
        Assert.IsTrue((await registry.CloseAsync(Unlimited)).IsFullyDrained);CollectionAssert.AreEqual(new[]{"c","b","a"},order);
    });
    [UnityTest] public IEnumerator ConcurrentCloseInvokesActualCleanupOnce() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>source.Task};registry.Register(item);
        var a=registry.CloseAsync(Unlimited);var b=registry.CloseAsync(Unlimited);Assert.AreEqual(1,item.Calls);
        source.TrySetResult(Result.Success());Assert.IsTrue((await a).IsFullyDrained);Assert.IsTrue((await b).IsFullyDrained);
        await registry.CloseAsync(Unlimited);Assert.AreEqual(1,item.Calls);
    });
    [UnityTest] public IEnumerator TicketTimeoutDoesNotEndActualAttempt() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>source.Task};var ticket=registry.Register(item).Value;
        await registry.CloseAsync(default);Assert.AreEqual(ErrorKind.Timeout,(await ticket.WaitAsync(default)).Error.Kind);
        Assert.AreEqual(CleanupState.Running,ticket.State);Assert.AreEqual(ErrorKind.InvalidResult,ticket.LastAttempt.Error.Kind);
        source.TrySetResult(Result.Success());Assert.IsTrue((await ticket.WaitAsync(OperationContext.Unlimited())).IsSuccess);
    });
    [UnityTest] public IEnumerator CancelOneWaiterLeavesOtherWaiterAlive() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var registry=new CleanupRegistry();var ticket=registry.Register(new Cleanup("a"){Action=()=>source.Task}).Value;
        await registry.CloseAsync(default);var cancel=new CancellationTokenSource();var canceled=ticket.WaitAsync(OperationContext.Unlimited(cancel.Token));var live=ticket.WaitAsync(OperationContext.Unlimited());
        cancel.Cancel();source.TrySetResult(Result.Success());Assert.AreEqual(ErrorKind.Canceled,(await canceled).Error.Kind);Assert.IsTrue((await live).IsSuccess);cancel.Dispose();
    });
    [UnityTest] public IEnumerator RunningRetryJoinsOriginalAttempt() => UniTask.ToCoroutine(async () =>
    {
        var source=new UniTaskCompletionSource<Result>();var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>source.Task};var ticket=registry.Register(item).Value;
        Assert.AreEqual(ErrorKind.InvalidState,(await ticket.RetryAsync(Unlimited)).Error.Kind);
        await registry.CloseAsync(default);var retry=ticket.RetryAsync(Unlimited);Assert.AreEqual(1,item.Calls);
        source.TrySetResult(Result.Success());Assert.IsTrue((await retry).IsSuccess);await ticket.RetryAsync(Unlimited);Assert.AreEqual(1,item.Calls);
    });
    [UnityTest] public IEnumerator FailedTicketRequiresExplicitRetry() => UniTask.ToCoroutine(async () =>
    {
        var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>UniTask.FromResult(Failed)};var ticket=registry.Register(item).Value;
        var report=await registry.CloseAsync(Unlimited);Assert.AreEqual(1,report.FailedCount);Assert.AreEqual(0,report.PendingCount);
        await registry.CloseAsync(Unlimited);Assert.AreEqual(1,item.Calls);item.Action=()=>UniTask.FromResult(Result.Success());
        Assert.IsTrue((await ticket.RetryAsync(Unlimited)).IsSuccess);Assert.IsTrue(registry.Snapshot.IsFullyDrained);Assert.AreEqual(2,item.Calls);
    });
    [UnityTest] public IEnumerator RetryFailedDoesNotRepeatSuccessfulItems() => UniTask.ToCoroutine(async () =>
    {
        var registry=new CleanupRegistry();var a=new Cleanup("a");var b=new Cleanup("b"){Action=()=>UniTask.FromResult(Failed)};registry.Register(a);registry.Register(b);
        await registry.CloseAsync(Unlimited);b.Action=()=>UniTask.FromResult(Result.Success());Assert.IsTrue((await registry.RetryFailedAsync(Unlimited)).IsFullyDrained);
        Assert.AreEqual(1,a.Calls);Assert.AreEqual(2,b.Calls);
    });
    [UnityTest] public IEnumerator ConcurrentRetriesShareOneNewAttempt() => UniTask.ToCoroutine(async () =>
    {
        var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>UniTask.FromResult(Failed)};var ticket=registry.Register(item).Value;await registry.CloseAsync(Unlimited);
        var source=new UniTaskCompletionSource<Result>();item.Action=()=>source.Task;
        var a=ticket.RetryAsync(Unlimited);var b=ticket.RetryAsync(Unlimited);Assert.AreEqual(2,item.Calls);
        source.TrySetResult(Result.Success());Assert.IsTrue((await a).IsSuccess);Assert.IsTrue((await b).IsSuccess);
    });
    [UnityTest] public IEnumerator ExceptionAndDefaultResultRetainFailedTicket() => UniTask.ToCoroutine(async () =>
    {
        foreach(var throwing in new[]{false,true})
        {
            var registry=new CleanupRegistry();var ticket=registry.Register(new Cleanup("a"){Action=()=>throwing ? throw new InvalidOperationException("release") : UniTask.FromResult(default(Result))}).Value;
            Assert.AreEqual(1,(await registry.CloseAsync(Unlimited)).FailedCount);Assert.AreEqual(CleanupState.Failed,ticket.State);
            Assert.AreEqual(throwing ? ErrorKind.CleanupFailed : ErrorKind.InvalidResult,ticket.LastAttempt.Error.Kind);
        }
    });
    [UnityTest] public IEnumerator WaitingBeforeCloseObservesFirstAttemptNotLaterRetry() => UniTask.ToCoroutine(async () =>
    {
        var registry=new CleanupRegistry();var item=new Cleanup("a"){Action=()=>UniTask.FromResult(Failed)};var ticket=registry.Register(item).Value;
        var waiter=ticket.WaitAsync(OperationContext.Unlimited());await registry.CloseAsync(Unlimited);
        item.Action=()=>UniTask.FromResult(Result.Success());await ticket.RetryAsync(Unlimited);
        Assert.AreEqual("test.release.failed",(await waiter).Error.Code);Assert.IsTrue(ticket.LastAttempt.IsSuccess);
    });
    [UnityTest] public IEnumerator ParentCloseGatesAndCancelsWholeTreeBeforeCallbacks() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var a=root.CreateChild("session",ScopeKind.Session).Value;var b=root.CreateChild("view",ScopeKind.View).Value;
        var callbackRan=false;a.LifetimeCancellation.Register(()=>{callbackRan=true;Assert.AreEqual(ScopeState.Closing,b.State);Assert.IsFalse(b.CreateChild("late",ScopeKind.Rent).IsSuccess);Assert.IsFalse(b.Cleanup.Register(new Cleanup("late")).IsSuccess);});
        Assert.IsTrue((await root.CloseAsync(Unlimited)).IsFullyDrained);Assert.IsTrue(callbackRan);Assert.IsTrue(a.LifetimeCancellation.IsCancellationRequested);Assert.IsTrue(b.LifetimeCancellation.IsCancellationRequested);
    });
    [UnityTest] public IEnumerator ParentOwnsChildPendingTaskAfterChildWaitTimesOut() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var child=root.CreateChild("view",ScopeKind.View).Value;var source=new UniTaskCompletionSource<Result>();child.RunTask("load",token=>source.Task);
        Assert.AreEqual(1,(await child.CloseAsync(default)).PendingCount);Assert.AreEqual(1,(await root.CloseAsync(default)).PendingCount);
        Assert.AreEqual(ScopeState.Closing,root.State);source.TrySetResult(Result.Success());Assert.IsTrue((await root.CloseAsync(Unlimited)).IsFullyDrained);
    });
    [UnityTest] public IEnumerator ChildFailureCanBeRetriedThroughParentTicket() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var child=root.CreateChild("session",ScopeKind.Session).Value;var item=new Cleanup("a"){Action=()=>UniTask.FromResult(Failed)};child.Cleanup.Register(item);
        Assert.AreEqual(1,(await root.CloseAsync(Unlimited)).FailedCount);Assert.AreEqual(ScopeState.CleanupFailed,child.State);
        item.Action=()=>UniTask.FromResult(Result.Success());Assert.IsTrue((await root.RetryFailedAsync(Unlimited)).IsFullyDrained);Assert.AreEqual(ScopeState.Closed,child.State);Assert.AreEqual(2,item.Calls);
    });
    [UnityTest] public IEnumerator ClosingChildAloneDoesNotCancelParentOrSibling() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var child=root.CreateChild("a",ScopeKind.Session).Value;var sibling=root.CreateChild("b",ScopeKind.AccountScoped).Value;
        await child.CloseAsync(Unlimited);Assert.IsFalse(root.LifetimeCancellation.IsCancellationRequested);Assert.IsFalse(sibling.LifetimeCancellation.IsCancellationRequested);
        await root.CloseAsync(Unlimited);
    });
    [UnityTest] public IEnumerator SubscriptionCleanupIsIdempotentAndReentrantCloseIsSafe() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var subscription=new Subscription();UniTask<CleanupReport> reentry=default;
        subscription.Action=()=>{reentry=root.CloseAsync(Unlimited);};var a=root.TrackSubscription("event",subscription);Assert.AreSame(a.Value,root.TrackSubscription("event",subscription).Value);
        Assert.IsFalse(root.TrackSubscription("second-id",subscription).IsSuccess);
        await root.CloseAsync(Unlimited);Assert.IsTrue((await reentry).IsFullyDrained);await root.CloseAsync(Unlimited);Assert.AreEqual(1,subscription.Calls);
    });
    [UnityTest] public IEnumerator DuplicateOrClosedTaskNeverInvokesFactory() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var calls=0;Func<CancellationToken,UniTask<Result>> factory=token=>{calls++;return UniTask.FromResult(Result.Success());};
        Assert.IsTrue(root.RunTask("a",factory).IsSuccess);Assert.IsFalse(root.RunTask("a",factory).IsSuccess);await root.CloseAsync(Unlimited);
        Assert.IsFalse(root.RunTask("b",factory).IsSuccess);Assert.AreEqual(1,calls);
    });
    [UnityTest] public IEnumerator TaskFailureIsObservedButBusinessWorkIsNeverRetried() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var calls=0;root.RunTask("a",token=>{calls++;throw new InvalidOperationException("work");});
        Assert.IsTrue((await root.CloseAsync(Unlimited)).IsFullyDrained);Assert.AreEqual(1,root.TaskDiagnostics.Count);await root.RetryFailedAsync(Unlimited);Assert.AreEqual(1,calls);
    });
    [UnityTest] public IEnumerator CleanupTokenIsIndependentOfCanceledScopeLifetime() => UniTask.ToCoroutine(async () =>
    {
        var root=new Scope("app");var item=new Cleanup("a");root.Cleanup.Register(item);await root.CloseAsync(Unlimited);
        Assert.IsTrue(root.LifetimeCancellation.IsCancellationRequested);Assert.IsFalse(item.Canceled);
    });
    [UnityTest] public IEnumerator ScopeKindsAndRepeatedCyclesLeaveNoPendingCleanup() => UniTask.ToCoroutine(async () =>
    {
        for(var i=0;i<100;i++)
        {
            var root=new Scope("app");foreach(ScopeKind kind in Enum.GetValues(typeof(ScopeKind))) root.CreateChild(kind.ToString(),kind).Value.TrackSubscription("s",new Subscription());
            Assert.IsTrue((await root.CloseAsync(Unlimited)).IsFullyDrained);Assert.AreEqual(0,root.Cleanup.Snapshot.PendingCount);Assert.AreEqual(0,root.Cleanup.Snapshot.FailedCount);
        }
    });
    [UnityTest] public IEnumerator GameContextDefaultSuppliesScopeAndClosesChildren() => UniTask.ToCoroutine(async () =>
    {
        var module=new Module("a");var subscription=new Subscription();module.Initialize=ctx=>{((IScopedModuleContext)ctx).Scope.CreateChild("view",ScopeKind.View).Value.TrackSubscription("event",subscription);return Result.Success();};
        var context=new GameContext(new[]{R(module)},Unlimited);Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
        Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(1,subscription.Calls);Assert.AreEqual(ScopeState.Closed,module.Scope.State);
    });
    [UnityTest] public IEnumerator ContextRetainsProviderDependencyUntilExplicitRegistryRetry() => UniTask.ToCoroutine(async () =>
    {
        var provider=new Module("provider");var consumer=new Module("consumer","provider");var item=new Cleanup("asset"){Action=()=>UniTask.FromResult(Failed)};
        consumer.Initialize=ctx=>{ctx.Cleanup.Register(item);return Result.Success();};var context=new GameContext(new[]{R(provider),R(consumer)},Unlimited);
        await context.StartAsync(OperationContext.Unlimited());Assert.AreEqual(ErrorKind.CleanupFailed,(await context.CloseAsync(OperationContext.Unlimited())).Error.Kind);
        Assert.AreEqual(0,provider.Disposes);Assert.AreEqual(1,consumer.Disposes);await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,item.Calls);
        item.Action=()=>UniTask.FromResult(Result.Success());Assert.IsTrue((await context.RetryCleanupAsync(Unlimited,OperationContext.Unlimited())).IsSuccess);
        Assert.AreEqual(1,provider.Disposes);Assert.AreEqual(1,consumer.Disposes);Assert.AreEqual(2,item.Calls);Assert.AreEqual(0,context.RetainedModuleCount);
    });
    [UnityTest] public IEnumerator FailedInitializationRollsBackRealScopeResources() => UniTask.ToCoroutine(async () =>
    {
        var module=new Module("a");var item=new Cleanup("partial");module.Initialize=ctx=>{ctx.Cleanup.Register(item);return Failed;};var context=new GameContext(new[]{R(module)},Unlimited);
        Assert.AreEqual("test.release.failed",(await context.StartAsync(OperationContext.Unlimited())).Error.Code);await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(1,item.Calls);
    });
}
