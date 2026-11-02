using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Kernel;
using NUnit.Framework;
using UnityEngine.TestTools;

// A manually clocked protocol witness, not the production scheduler/Scope/Provider.
public sealed class AsyncContractTests
{
    private sealed class ControlledOperation : IOwnedOperation<int>, IAsyncCleanup, ILateCompletionSink<int>, ICleanupTicket
    {
        private readonly CompletionArbiter arbiter = new CompletionArbiter();
        private readonly UniTaskCompletionSource<Result<int>> waiter = new UniTaskCompletionSource<Result<int>>();
        private readonly UniTaskCompletionSource<bool> backendDone = new UniTaskCompletionSource<bool>();
        private bool waited, delivered, released;
        private int value;
        public bool FailReclaim;
        public int Releases;
        public FrameworkError ObservedBackendError;
        public string Id => "controlled-operation";
        public BackendState BackendState { get; private set; } = BackendState.Running;
        public ICleanupTicket Cleanup => this;
        public CleanupState State { get; private set; } = CleanupState.Registered;
        public Result LastAttempt { get; private set; }
        public async UniTask<Result<int>> WaitAsync(OperationContext wait)
        {
            if (waited) return Result<int>.Failure(new FrameworkError(ErrorKind.InvalidState, "operation.already_waited"));
            waited = true;
            // Entry precedence: canceled -> zero deadline -> backend completion.
            if (wait.Cancellation.IsCancellationRequested) Cancel();
            else if (wait.Timeout == TimeSpan.Zero) Expire();
            using (wait.Cancellation.Register(Cancel))
            {
                if (BackendState == BackendState.Succeeded) Deliver();
                return await waiter.Task;
            }
        }
        private void Cancel()
        {
            if (arbiter.TryComplete(WaitOutcome.Canceled)) waiter.TrySetResult(Result<int>.Failure(new FrameworkError(ErrorKind.Canceled, "operation.canceled")));
        }
        public void Expire()
        {
            if (arbiter.TryComplete(WaitOutcome.TimedOut)) waiter.TrySetResult(Result<int>.Failure(new FrameworkError(ErrorKind.Timeout, "operation.timeout")));
        }
        public void Complete(int result)
        {
            if (BackendState != BackendState.Running) return; // duplicate SDK callback
            value = result; BackendState = BackendState.Succeeded;
            if (waited) Deliver();
            backendDone.TrySetResult(true);
        }
        private void Deliver()
        {
            delivered = arbiter.TryComplete(WaitOutcome.Succeeded);
            if (delivered) waiter.TrySetResult(Result<int>.Success(value));
        }
        public void Fail(FrameworkError error)
        {
            if (BackendState != BackendState.Running) return;
            BackendState = BackendState.Failed; ObservedBackendError = error;
            if (arbiter.TryComplete(WaitOutcome.Failed)) waiter.TrySetResult(Result<int>.Failure(error));
            backendDone.TrySetResult(true);
        }
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            if (State == CleanupState.Succeeded) return LastAttempt;
            State = CleanupState.Running;
            await backendDone.Task.Preserve();
            LastAttempt = delivered || BackendState == BackendState.Failed ? Result.Success() : await ReclaimAsync(value, cleanup);
            State = LastAttempt.IsSuccess ? CleanupState.Succeeded : CleanupState.Failed;
            return LastAttempt;
        }
        public UniTask<Result> ReclaimAsync(int result, CleanupContext cleanup)
        {
            if (FailReclaim) return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed, "probe.release_failed")));
            if (!released) { released = true; Releases++; }
            return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> RetryAsync(CleanupContext cleanup) => CleanupAsync(cleanup);
        // Not exercised by this witness: real ticket wait/drain semantics are implemented in S1-03.
        UniTask<Result> ICleanupTicket.WaitAsync(OperationContext wait) =>
            UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.NotSupported, "probe.ticket_wait_not_implemented")));
    }
    private static CleanupContext CleanupBudget => new CleanupContext(CancellationToken.None, TimeSpan.FromSeconds(5));

    [UnityTest] public IEnumerator CanceledWaitRetainsBackendUntilLateSuccessIsReclaimed() => UniTask.ToCoroutine(async () =>
    {
        using (var cts = new CancellationTokenSource())
        {
            var operation = new ControlledOperation();
            var wait = operation.WaitAsync(OperationContext.Unlimited(cts.Token));
            cts.Cancel();
            Assert.AreEqual(ErrorKind.Canceled, (await wait).Error.Kind);
            Assert.AreEqual(BackendState.Running, operation.BackendState);
            var cleanup = operation.CleanupAsync(CleanupBudget);
            await UniTask.Yield();
            Assert.AreEqual(CleanupState.Running, operation.State);
            Assert.AreEqual(0, operation.Releases);
            operation.Complete(42); operation.Complete(42);
            Assert.IsTrue((await cleanup).IsSuccess);
            Assert.AreEqual(1, operation.Releases);
            Assert.IsTrue((await operation.CleanupAsync(CleanupBudget)).IsSuccess);
            Assert.AreEqual(1, operation.Releases);
        }
    });
    [UnityTest] public IEnumerator TimeoutDoesNotPermitLateDeliveryOrHideCleanupFailure() => UniTask.ToCoroutine(async () =>
    {
        var operation = new ControlledOperation { FailReclaim = true };
        var result = await operation.WaitAsync(default);
        Assert.AreEqual(ErrorKind.Timeout, result.Error.Kind);
        await UniTask.Yield(); operation.Complete(9);
        Assert.IsFalse((await operation.CleanupAsync(CleanupBudget)).IsSuccess);
        Assert.AreEqual(CleanupState.Failed, operation.State);
        Assert.AreEqual(0, operation.Releases);
        operation.FailReclaim = false;
        Assert.IsTrue((await operation.RetryAsync(CleanupBudget)).IsSuccess);
        Assert.AreEqual(1, operation.Releases);
    });
    [UnityTest] public IEnumerator SuccessfulDeliveryWinsOverLaterCancellation() => UniTask.ToCoroutine(async () =>
    {
        using (var cts = new CancellationTokenSource())
        {
            var operation = new ControlledOperation();
            var wait = operation.WaitAsync(OperationContext.Unlimited(cts.Token));
            operation.Complete(7); cts.Cancel();
            Assert.AreEqual(7, (await wait).Value);
            Assert.IsTrue((await operation.CleanupAsync(CleanupBudget)).IsSuccess);
            Assert.AreEqual(0, operation.Releases); // successful value belongs to caller
            Assert.AreEqual(ErrorKind.InvalidState, (await operation.WaitAsync(OperationContext.Unlimited())).Error.Kind);
            await UniTask.Yield();
        }
    });
    [UnityTest] public IEnumerator PreCanceledWaitWinsOverBufferedCompletion() => UniTask.ToCoroutine(async () =>
    {
        using (var cts = new CancellationTokenSource())
        {
            var operation = new ControlledOperation();
            operation.Complete(17); cts.Cancel();
            Assert.AreEqual(ErrorKind.Canceled, (await operation.WaitAsync(OperationContext.Unlimited(cts.Token))).Error.Kind);
            Assert.IsTrue((await operation.CleanupAsync(CleanupBudget)).IsSuccess);
            Assert.AreEqual(1, operation.Releases);
        }
    });
    [UnityTest] public IEnumerator LateFailureIsObservedWithoutRewritingTimeout() => UniTask.ToCoroutine(async () =>
    {
        var operation = new ControlledOperation();
        var result = await operation.WaitAsync(default);
        operation.Fail(new FrameworkError(ErrorKind.Network, "probe.late_network_failure"));
        Assert.AreEqual(ErrorKind.Timeout, result.Error.Kind);
        Assert.AreEqual(ErrorKind.Network, operation.ObservedBackendError.Kind);
        Assert.IsTrue((await operation.CleanupAsync(CleanupBudget)).IsSuccess);
        Assert.AreEqual(0, operation.Releases);
    });
    private sealed class PartialModule : IModule
    {
        public string Id => "probe.partial";
        public IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public bool OwnsResource;
        public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation)
        {
            OwnsResource = true;
            return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.Provider, "probe.partial_init")));
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)
        {
            if (cleanup.ShutdownCancellation.IsCancellationRequested) return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.Canceled, "probe.cleanup_canceled")));
            OwnsResource = false; return UniTask.FromResult(Result.Success());
        }
    }
    [UnityTest] public IEnumerator PartialInitializationCanBeReleasedWithIndependentCleanupBudget() => UniTask.ToCoroutine(async () =>
    {
        using (var cts = new CancellationTokenSource())
        {
            var module = new PartialModule();
            Assert.IsFalse((await module.InitializeAsync(null, OperationContext.Unlimited(cts.Token))).IsSuccess);
            cts.Cancel();
            Assert.IsTrue((await module.StopAsync(CleanupBudget)).IsSuccess);
            Assert.IsTrue((await module.DisposeAsync(CleanupBudget)).IsSuccess);
            Assert.IsFalse(module.OwnsResource);
            await UniTask.Yield();
        }
    });
}
