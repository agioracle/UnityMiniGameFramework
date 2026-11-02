using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum GameContextState { Created, Starting, Ready, Closing, CleanupFailed, Closed }
    // Read-only boundary used by Game.Access; composition/lifecycle stays in Boot.
    public interface IGameServices : IServiceResolver
    {
        GameContextState State { get; }
    }
}
