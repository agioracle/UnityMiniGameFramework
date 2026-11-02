using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Host
{
    public sealed class RuntimeHostModule : IModule, IRuntimeScheduler, IApplicationLifecycle
    {
        private readonly RuntimeHost host;
        public RuntimeHostModule(RuntimeHost host) { this.host=host?host:throw new ArgumentNullException(nameof(host)); }
        private HostScheduler scheduler;
        private HostScheduler Scheduler => scheduler ?? throw new InvalidOperationException("Host is not running.");
        public double ScaledTime => Scheduler.ScaledTime;
        public double UnscaledTime => Scheduler.UnscaledTime;
        public double Realtime => Scheduler.Realtime;
        public bool IsPaused => Scheduler.IsPaused;
        public IDisposable Schedule(double delay,ClockDomain clock,Action callback,double repeatInterval=0)=>Scheduler.Schedule(delay,clock,callback,repeatInterval);
        public IDisposable SubscribeTick(Action<float,float> tick)=>Scheduler.SubscribeTick(tick);
        public IHostOperation Observe(OperationContext operation,Action<FrameworkError> interrupted)=>Scheduler.Observe(operation,interrupted);
        // Cached managed scheduler: this forwarding path performs no Unity object access.
        public bool Post(Action dispatch,Action discard)=>Scheduler.Post(dispatch,discard);
        public IDisposable SubscribePause(Action<bool> changed)=>Scheduler.SubscribePause(changed);
        public string Id => "runtime-host";
        public IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.Canceled,"host.init.canceled")));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.Timeout,"host.init.timeout")));
            scheduler=host.StartHost();return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> DisposeAsync(CleanupContext cleanup){if(host)host.StopHost();return UniTask.FromResult(Result.Success());}
    }
}
