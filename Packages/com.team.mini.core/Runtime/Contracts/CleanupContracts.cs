using Cysharp.Threading.Tasks;
using MiniFramework.Kernel;
namespace MiniFramework.Contracts
{
    public enum CleanupState { Registered, Running, Succeeded, Failed }
    public enum CleanupRegistryState { Open, Closing, Closed }
    public interface IAsyncCleanup
    {
        string Id { get; }
        // Success means actual release was confirmed. Failed attempts retain ownership for retry.
        UniTask<Result> CleanupAsync(CleanupContext context);
    }
    public interface ICleanupTicket
    {
        string Id { get; }
        CleanupState State { get; }
        // Default/InvalidResult before first attempt. Each WaitAsync returns a fresh awaitable.
        Result LastAttempt { get; }
        UniTask<Result> WaitAsync(OperationContext wait);
        UniTask<Result> RetryAsync(CleanupContext cleanup);
    }
    public readonly struct CleanupReport
    {
        public int PendingCount { get; }
        public int FailedCount { get; }
        public bool IsFullyDrained { get; }
        public CleanupReport(int pendingCount, int failedCount, bool isFullyDrained)
        {
            if (pendingCount < 0 || failedCount < 0 || (isFullyDrained && (pendingCount != 0 || failedCount != 0)))
                throw new System.ArgumentException("A drained registry has no pending or failed entries.");
            PendingCount = pendingCount; FailedCount = failedCount; IsFullyDrained = isFullyDrained;
        }
    }
    public interface ICleanupRegistry
    {
        CleanupRegistryState State { get; }
        // Register BEFORE irreversible work. Rejection leaves responsibility with the caller.
        Result<ICleanupTicket> Register(IAsyncCleanup cleanup);
        // Repeat/concurrent calls join the same drain, without double invocation.
        UniTask<CleanupReport> CloseAsync(CleanupContext cleanup);
    }
}
