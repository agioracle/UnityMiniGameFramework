using Cysharp.Threading.Tasks;
using MiniFramework.Kernel;
namespace MiniFramework.Contracts
{
    public enum BackendState { Running, Succeeded, Failed }
    public interface IOwnedOperation<T>
    {
        string Id { get; }
        BackendState BackendState { get; }
        ICleanupTicket Cleanup { get; }
        // Exactly one call per logical request. Sharing is implemented underneath, with separate requests.
        // Cancellation/timeout settles this waiter only, never claims the backend has stopped.
        UniTask<Result<T>> WaitAsync(OperationContext wait);
    }
    public interface ILateCompletionSink<T>
    {
        // Service-owned reclaim on the required backend thread; never delivers to UI/gameplay.
        // A failed reclaim retains T for an explicit retry.
        UniTask<Result> ReclaimAsync(T value, CleanupContext cleanup);
    }
}
