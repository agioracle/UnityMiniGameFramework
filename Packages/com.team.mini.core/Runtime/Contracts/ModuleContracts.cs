using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Kernel;
namespace MiniFramework.Contracts
{
    public interface IServiceResolver
    {
        // Only declared, Ready dependencies are visible; missing dependencies are not auto-created.
        bool TryGet<T>(out T service) where T : class;
    }
    public interface IModuleContext
    {
        IServiceResolver Services { get; }
        CancellationToken LifetimeCancellation { get; }
        ICleanupRegistry Cleanup { get; }
    }
    // Default GameContext supplies this extension. Custom legacy registry factories may not.
    public interface IScopedModuleContext : IModuleContext
    {
        IScope Scope { get; }
    }
    public interface IModule
    {
        // Stable composition key, NOT the persistent save ModuleId from S4-01.
        string Id { get; }
        IReadOnlyList<string> Dependencies { get; }
        UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation);
        // Stop quiesces new work and subscriptions; dispose releases owned resources.
        UniTask<Result> StopAsync(CleanupContext cleanup);
        UniTask<Result> DisposeAsync(CleanupContext cleanup);
    }
}
