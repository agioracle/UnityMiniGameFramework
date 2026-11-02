using System;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Kernel;
using NUnit.Framework;

public sealed class ContractValueTests
{
    [Test] public void DefaultResultsCannotMasqueradeAsSuccess()
    {
        Assert.IsFalse(default(Result).IsSuccess);
        Assert.AreEqual(ErrorKind.InvalidResult, default(Result<int>).Error.Kind);
        Assert.Throws<InvalidOperationException>(() => { var ignored = default(Result<int>).Value; });
        Assert.Throws<ArgumentException>(() => Result.Failure(default));
    }
    [Test] public void FailureCannotExposeValueAndSuccessCannotExposeError()
    {
        var failure = Result<int>.Failure(new FrameworkError(ErrorKind.NoFill, "ads.no_fill"));
        Assert.Throws<InvalidOperationException>(() => { var ignored = failure.Value; });
        Assert.Throws<InvalidOperationException>(() => { var ignored = Result.Success().Error; });
        Assert.AreEqual(0, Result<int>.Success(0).Value);
    }
    [Test] public void ErrorCategoriesDoNotCollapseDistinctPlatformOutcomes()
    {
        var kinds = new[] { ErrorKind.NotSupported, ErrorKind.PermissionDenied, ErrorKind.NoFill, ErrorKind.Network, ErrorKind.Canceled, ErrorKind.Timeout };
        foreach (var kind in kinds)
        {
            var result = Result.Failure(new FrameworkError(kind, "probe." + kind));
            Assert.AreEqual(kind, result.Error.Kind);
            Assert.AreEqual(RetryHint.Never, result.Error.Retry);
        }
        Assert.Throws<ArgumentException>(() => new FrameworkError(ErrorKind.Provider, ""));
    }
    [Test] public void CancellationAndCompletionRespectFirstAcceptedSignal()
    {
        var canceled = new CompletionArbiter();
        Assert.IsTrue(canceled.TryComplete(WaitOutcome.Canceled));
        Assert.IsFalse(canceled.TryComplete(WaitOutcome.Succeeded));
        Assert.AreEqual(WaitOutcome.Canceled, canceled.Outcome);
        var completed = new CompletionArbiter();
        Assert.IsTrue(completed.TryComplete(WaitOutcome.Succeeded));
        Assert.IsFalse(completed.TryComplete(WaitOutcome.Canceled));
        Assert.AreEqual(WaitOutcome.Succeeded, completed.Outcome);
    }
    [Test] public void TimeoutAndDuplicateFailuresCannotOverwriteTerminalState()
    {
        var arbiter = new CompletionArbiter();
        Assert.IsTrue(arbiter.TryComplete(WaitOutcome.TimedOut));
        Assert.IsFalse(arbiter.TryComplete(WaitOutcome.Failed));
        Assert.IsFalse(arbiter.TryComplete(WaitOutcome.TimedOut));
        Assert.Throws<ArgumentOutOfRangeException>(() => arbiter.TryComplete(WaitOutcome.Pending));
    }
    [Test] public void FailedInitializationMustPassThroughStopAndDispose()
    {
        Assert.IsTrue(LifecycleRules.CanTransition(ModuleState.Initializing, ModuleState.Failed));
        Assert.IsFalse(LifecycleRules.CanTransition(ModuleState.Failed, ModuleState.Ready));
        Assert.IsTrue(LifecycleRules.CanTransition(ModuleState.Failed, ModuleState.Stopping));
        Assert.IsTrue(LifecycleRules.CanTransition(ModuleState.Stopping, ModuleState.Disposing));
        Assert.IsFalse(LifecycleRules.CanTransition(ModuleState.Stopping, ModuleState.Disposed));
    }
    [Test] public void LateInitializationCannotResurrectStoppingOrDisposedModule()
    {
        Assert.IsFalse(LifecycleRules.CanTransition(ModuleState.Stopping, ModuleState.Ready));
        Assert.IsFalse(LifecycleRules.CanTransition(ModuleState.Disposed, ModuleState.Initializing));
        Assert.IsFalse(LifecycleRules.CanTransition(ModuleState.CleanupFailed, ModuleState.Disposed));
        Assert.IsTrue(LifecycleRules.CanTransition(ModuleState.CleanupFailed, ModuleState.Disposing));
    }
    [Test] public void CleanupDeadlineIsIndependentOfCanceledBusinessWait()
    {
        using (var business = new CancellationTokenSource())
        using (var shutdown = new CancellationTokenSource())
        {
            business.Cancel();
            var wait = OperationContext.Unlimited(business.Token);
            var cleanup = new CleanupContext(shutdown.Token, TimeSpan.FromSeconds(5));
            Assert.IsTrue(wait.Cancellation.IsCancellationRequested);
            Assert.IsFalse(cleanup.ShutdownCancellation.IsCancellationRequested);
        }
    }
    [Test] public void DefaultDeadlinesAreImmediateAndInvalidNegativeDurationsFail()
    {
        Assert.AreEqual(TimeSpan.Zero, default(OperationContext).Timeout);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, OperationContext.Unlimited().Timeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationContext(default, TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CleanupContext(default, TimeSpan.FromMilliseconds(-2)));
    }
    [Test] public void PendingOrFailedCleanupCannotBeReportedAsDrained()
    {
        Assert.IsFalse(default(CleanupReport).IsFullyDrained);
        Assert.Throws<ArgumentException>(() => new CleanupReport(1, 0, true));
        Assert.Throws<ArgumentException>(() => new CleanupReport(0, 1, true));
        Assert.IsFalse(new CleanupReport(0, 1, false).IsFullyDrained);
        Assert.IsTrue(new CleanupReport(0, 0, true).IsFullyDrained);
    }
}
