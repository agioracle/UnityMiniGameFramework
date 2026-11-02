using System;
using System.Threading;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public interface IRetryableCleanupRegistry : ICleanupRegistry
    {
        CleanupReport Snapshot { get; }
        UniTask<CleanupReport> RetryFailedAsync(CleanupContext cleanup);
    }
    public enum ScopeKind { App, Session, View, Rent, AccountScoped }
    public enum ScopeState { Open, Closing, CleanupFailed, Closed }
    public interface IScope
    {
        string Id { get; }
        ScopeKind Kind { get; }
        ScopeState State { get; }
        IScope Parent { get; }
        CancellationToken LifetimeCancellation { get; }
        IRetryableCleanupRegistry Cleanup { get; }
        Result<IScope> CreateChild(string id, ScopeKind kind);
        Result<ICleanupTicket> TrackSubscription(string id, IDisposable subscription);
        // Register the observer BEFORE invoking the factory. Failure to register never starts work.
        Result<ICleanupTicket> RunTask(string id, Func<CancellationToken, UniTask<Result>> factory);
        UniTask<CleanupReport> CloseAsync(CleanupContext cleanup);
        UniTask<CleanupReport> RetryFailedAsync(CleanupContext cleanup);
    }
}
