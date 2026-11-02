using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // Platform-independent injectable source. No Unity lowMemory/mini-game SDK is wired in S2-06.
    public sealed class ManualMemoryPressureSource:IMemoryPressureSource
    {
        private event Action notifications;
        public int SubscriberCount=>notifications?.GetInvocationList().Length??0;
        public IDisposable Subscribe(Action notification)
        {CleanupWait.MainThread();if(notification==null)throw new ArgumentNullException(nameof(notification));notifications+=notification;return new Subscription(this,notification);}
        public void Raise(){CleanupWait.MainThread();notifications?.Invoke();}
        private sealed class Subscription:IDisposable
        {
            private ManualMemoryPressureSource owner;private Action action;
            internal Subscription(ManualMemoryPressureSource owner,Action action){this.owner=owner;this.action=action;}
            public void Dispose(){CleanupWait.MainThread();if(owner==null)return;owner.notifications-=action;owner=null;action=null;}
        }
    }
    public sealed class MemoryPressureController:IMemoryPressureHandler,IModule,IAsyncCleanup,IDiagnosticSource
    {
        private readonly IMemoryPressureSource source;private readonly IPrefetchControl prefetch;private readonly IResourceTrimTarget target;
        private readonly int destroysPerFrame;private readonly CleanupContext cleanup;
        private IDisposable subscription;private bool ready,stopped;private UniTask<Result>? active;private UniTask driver;
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public int PassCount {get;private set;}public int CoalescedCount {get;private set;}public Result LastResult {get;private set;}
        public bool IsRunning=>active.HasValue&&active.Value.Status==UniTaskStatus.Pending;
        public MemoryPressureController(IMemoryPressureSource source,IPrefetchControl prefetch,IResourceTrimTarget target,int destroysPerFrame,CleanupContext cleanup,
            string schedulerModuleId="resource-scheduler",string poolModuleId="pool",string id="memory-pressure",string sourceModuleId=null)
        {
            CleanupWait.MainThread();this.source=source??throw new ArgumentNullException(nameof(source));this.prefetch=prefetch??throw new ArgumentNullException(nameof(prefetch));this.target=target??throw new ArgumentNullException(nameof(target));
            if(destroysPerFrame<=0||string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(schedulerModuleId)||string.IsNullOrWhiteSpace(poolModuleId)||sourceModuleId!=null&&string.IsNullOrWhiteSpace(sourceModuleId))throw new ArgumentException("Valid ids/budget required.");
            Id=id;this.destroysPerFrame=destroysPerFrame;this.cleanup=cleanup;
            var dependencies=new List<string>{schedulerModuleId};
            if(!dependencies.Contains(poolModuleId))dependencies.Add(poolModuleId);
            if(sourceModuleId!=null&&!dependencies.Contains(sourceModuleId))dependencies.Add(sourceModuleId);
            Dependencies=dependencies.AsReadOnly();
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            CleanupWait.MainThread();if(ready||stopped)return UniTask.FromResult(Fail("pressure.state",ErrorKind.InvalidState));
            var canceled=CleanupWait.Interrupted(operation,CleanupWait.Now);if(canceled.HasValue)return UniTask.FromResult(canceled.Value);
            var ticket=context.Cleanup.Register(this);if(!ticket.IsSuccess)return UniTask.FromResult(Result.Failure(ticket.Error));
            ready=true;subscription=source.Subscribe(()=>Start());return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> NotifyAsync(OperationContext wait)
        {
            CleanupWait.MainThread();if(!ready||stopped)return UniTask.FromResult(Fail("pressure.not_ready",ErrorKind.NotReady));
            var interrupted=CleanupWait.Interrupted(wait,CleanupWait.Now);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            return Observe(Start(),wait);
        }
        private UniTask<Result> Start()
        {
            if(stopped)return UniTask.FromResult(Fail("pressure.stopped",ErrorKind.NotReady));
            if(IsRunning){CoalescedCount++;return active.Value;}
            var completion=new UniTaskCompletionSource<Result>();active=completion.Task;PassCount++;driver=Run(completion).Preserve();return completion.Task;
        }
        private async UniTask Run(UniTaskCompletionSource<Result> completion)
        {
            Result result;IDisposable gate=null;
            try {gate=prefetch.SuppressPrefetch();result=await target.TrimIdleAsync(destroysPerFrame,cleanup);}
            catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"pressure.exception",e.Message));}
            finally {await UniTask.SwitchToMainThread();gate?.Dispose();}
            LastResult=result;completion.TrySetResult(result);
        }
        public UniTask<Result> StopAsync(CleanupContext context){Stop();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        private void Stop(){CleanupWait.MainThread();stopped=true;ready=false;subscription?.Dispose();subscription=null;}
        public async UniTask<Result> CleanupAsync(CleanupContext context)
        {Stop();if(active.HasValue)await active.Value;return Result.Success();} // Pool owns failed release tickets; observer owns only pass lifetime.
        public DiagnosticCounters ReadDiagnostics()=>new DiagnosticCounters{PendingOperations=IsRunning?1:0};
        private static async UniTask<Result> Observe(UniTask<Result> actual,OperationContext wait)
        {var began=CleanupWait.Now;while(true){var interrupted=CleanupWait.Interrupted(wait,began);if(interrupted.HasValue)return interrupted.Value;if(actual.Status!=UniTaskStatus.Pending)return await actual;await UniTask.Yield();}}
        private static Result Fail(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
    }
}
