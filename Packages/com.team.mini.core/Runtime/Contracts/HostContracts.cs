using System;
namespace MiniFramework.Contracts
{
    public enum ClockDomain { Scaled, Unscaled, Realtime }
    public interface IMonotonicClock { double Seconds { get; } }
    public interface IHostOperation : IDisposable
    {
        bool IsPending { get; }
        Result TryComplete(Action commit);
    }
    // Delivered immediately when Tick is suspended; one Host remains pause authority.
    public interface IApplicationLifecycle
    {
        bool IsPaused { get; }
        IDisposable SubscribePause(Action<bool> changed);
    }
    public interface IRuntimeScheduler
    {
        double ScaledTime { get; }
        double UnscaledTime { get; }
        double Realtime { get; }
        bool IsPaused { get; }
        IDisposable Schedule(double delay, ClockDomain clock, Action callback, double repeatInterval = 0);
        IDisposable SubscribeTick(Action<float,float> tick);
        IHostOperation Observe(OperationContext operation, Action<FrameworkError> interrupted);
        // Any thread. On false, caller retains payload; accepted work is dispatched OR discarded on main thread.
        bool Post(Action dispatch, Action discard);
    }
}
