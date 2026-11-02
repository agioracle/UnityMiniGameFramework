using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;

namespace MiniFramework.Platform
{
    // Explicit module: platform -> this source -> MemoryPressureController.
    // Notifications request bounded idle-resource trim; they never unload active content.
    public sealed class PlatformMemoryPressureSource : IMemoryPressureSource, IModule, IAsyncCleanup, IDiagnosticSource
    {
        readonly PlatformService platform;
        readonly EventChannel<bool> notifications = new EventChannel<bool>();
        Scope scope;
        bool initialized, stopped, cleanupEntered;
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public int SubscriberCount => notifications.ListenerCount;
        public int CallbackErrors { get; private set; }
        public PlatformMemoryPressureSource(PlatformService platform, string id = "platform-memory-pressure")
        {
            Main(); this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(nameof(id));
            Id = id; Dependencies = Array.AsReadOnly(new[] { platform.Id });
        }
        static void Main() { if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("Memory pressure source requires main thread."); }
        public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation)
        {
            Main();
            if (initialized || stopped) return UniTask.FromResult(Fail(ErrorKind.InvalidState, "platform.memory.state"));
            if (operation.Cancellation.IsCancellationRequested) return UniTask.FromResult(Fail(ErrorKind.Canceled, "platform.memory.canceled"));
            if (operation.Timeout == TimeSpan.Zero) return UniTask.FromResult(Fail(ErrorKind.Timeout, "platform.memory.timeout"));
            var tracked = context.Cleanup.Register(this);
            if (!tracked.IsSuccess) return UniTask.FromResult(Result.Failure(tracked.Error));
            if (platform.State != PlatformState.Ready) return UniTask.FromResult(Fail(ErrorKind.NotReady, "platform.memory.not_ready"));
            var capability = platform.Capabilities.Get(PlatformFeature.MemoryWarning);
            if (!capability.IsUsable)
                return UniTask.FromResult(Fail(capability.Support == CapabilitySupport.Unsupported ? ErrorKind.NotSupported : ErrorKind.Unavailable, "platform.memory.unavailable"));
            scope = new Scope(Id + ".subscriptions");
            var attached = platform.SubscribeLifecycle(scope, Id, Receive);
            initialized = attached.IsSuccess;
            return UniTask.FromResult(attached.IsSuccess ? Result.Success() : Result.Failure(attached.Error));
        }
        public IDisposable Subscribe(Action notification)
        {
            Main(); if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (!initialized || stopped) throw new InvalidOperationException("platform.memory.not_ready");
            return notifications.Subscribe(_ => { if (!stopped && platform.State == PlatformState.Ready) notification(); });
        }
        void Receive(PlatformLifecycleEvent notification)
        {
            if (stopped || notification.Kind != PlatformLifecycleKind.MemoryWarning || platform.State != PlatformState.Ready) return;
            if (!notifications.Publish(true).IsSuccess) CallbackErrors++;
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        { Main(); stopped = true; notifications.Dispose(); return UniTask.FromResult(Result.Success()); }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main(); await StopAsync(cleanup);
            if (scope == null) return Result.Success();
            var retry = cleanupEntered; cleanupEntered = true;
            var report = retry ? await scope.RetryFailedAsync(cleanup) : await scope.CloseAsync(cleanup);
            return report.IsFullyDrained ? Result.Success() : Fail(ErrorKind.CleanupFailed, "platform.memory.detach_failed");
        }
        public DiagnosticCounters ReadDiagnostics() => new DiagnosticCounters { Listeners = SubscriberCount, ActiveScopes = scope != null && scope.State != ScopeState.Closed ? 1 : 0 };
        static Result Fail(ErrorKind kind, string code) => Result.Failure(new FrameworkError(kind, code));
    }
}
