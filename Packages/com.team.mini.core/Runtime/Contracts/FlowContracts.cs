using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum SessionFlowState { Uninitialized, Ready, Transitioning, Recovering, Closed }
    public interface IGameSession
    {
        string Id {get;}
        IScope Scope {get;}
        ISceneLease Scene {get;}
    }
    public interface ISessionFlow
    {
        SessionFlowState State {get;}
        IGameSession Current {get;}
        // Null key creates a session without changing Unity scenes.
        UniTask<Result<IGameSession>> EnterAsync(string id,ResourceKey? scene,OperationContext operation);
        UniTask<Result> RetryRecoveryAsync(CleanupContext cleanup);
    }
}
