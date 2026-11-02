using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // Explicit opt-in for modules that acquire NO asynchronous resources. Never accepts ownership.
    // Resource-bearing modules must inject the S1-03 registry instead.
    public sealed class NoResourcesCleanupRegistry : ICleanupRegistry
    {
        public CleanupRegistryState State { get; private set; }
        public Result<ICleanupTicket> Register(IAsyncCleanup cleanup) => Result<ICleanupTicket>.Failure(
            new FrameworkError(ErrorKind.NotSupported, "context.resources.disabled", "No async resource ownership is supported by this registry."));
        public UniTask<CleanupReport> CloseAsync(CleanupContext cleanup)
        {
            State = CleanupRegistryState.Closed;
            return UniTask.FromResult(new CleanupReport(0, 0, true));
        }
    }
}
