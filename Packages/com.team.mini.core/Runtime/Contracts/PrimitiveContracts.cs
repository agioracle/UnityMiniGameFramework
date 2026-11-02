using System;
namespace MiniFramework.Contracts
{
    public interface IEventStream<T> { IDisposable Subscribe(Action<T> listener); }
    public interface IReadOnlyState<T> : IEventStream<T> { T Value { get; } }
    public interface IStateNode
    {
        Result Enter();
        Result Tick(float deltaTime);
        Result Exit();
    }
    public enum StateMachineStatus { Stopped, Running, Faulted, Closed }
}
