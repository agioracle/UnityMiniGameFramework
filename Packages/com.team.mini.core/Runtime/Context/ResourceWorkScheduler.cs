using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;

namespace MiniFramework.Runtime
{
    public sealed class ResourceWorkScheduler : IResourceWorkScheduler, IModule, IAsyncCleanup, IDiagnosticSource, IPrefetchControl
    {
        private readonly List<Work>[] queues = { new List<Work>(), new List<Work>(), new List<Work>() };
        private readonly int[] active = new int[3];
        private readonly ResourceScheduleBudget budget;
        private readonly IMonotonicClock clock;
        private IDisposable ticks;
        private bool stopped, initialized, ticking;
        private long sequence;
        private int prefetchGates;
        public bool IsPrefetchSuppressed=>prefetchGates>0;
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public int ActiveDownloads => active[0];
        public int ActiveLoads => active[1];
        public int QueuedDownloads => queues[0].Count;
        public int QueuedLoads => queues[1].Count;
        public int QueuedFrameUnits => queues[2].Count;
        public int LastFrameUnits { get; private set; }
        public int FrameBudgetOverruns { get; private set; }
        public ResourceWorkScheduler(ResourceScheduleBudget budget, string id="resource-scheduler", string hostModuleId=null, IMonotonicClock clock=null)
        {
            CleanupWait.MainThread();this.budget=budget??throw new ArgumentNullException(nameof(budget));
            if(string.IsNullOrWhiteSpace(id)||hostModuleId!=null&&string.IsNullOrWhiteSpace(hostModuleId))throw new ArgumentException("Stable module ids required.");
            Id=id;Dependencies=hostModuleId==null?Array.Empty<string>():Array.AsReadOnly(new[]{hostModuleId});
            this.clock=clock??new StopwatchClock();
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            CleanupWait.MainThread();
            if(initialized||stopped)return UniTask.FromResult(Failure("schedule.initialize.state",ErrorKind.InvalidState));
            if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Failure("schedule.initialize.canceled",ErrorKind.Canceled));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Failure("schedule.initialize.timeout",ErrorKind.Timeout));
            IRuntimeScheduler host=null;
            if(Dependencies.Count!=0&&!context.Services.TryGet(out host))return UniTask.FromResult(Failure("schedule.host.missing",ErrorKind.Dependency));
            var ticket=context.Cleanup.Register(this);if(!ticket.IsSuccess)return UniTask.FromResult(Result.Failure(ticket.Error));
            initialized=true;if(host!=null)ticks=host.SubscribeTick((_,__)=>Tick());
            return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup){Stop();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Stop();while(active[0]+active[1]+active[2]!=0)await UniTask.Yield();return Result.Success();
        }
        private void Stop()
        {
            CleanupWait.MainThread();if(stopped)return;stopped=true;ticks?.Dispose();ticks=null;
            foreach(var queue in queues)while(queue.Count!=0)queue[0].CancelQueued();
        }
        public Result<IScheduledOperation<T>> Schedule<T>(ResourceLane lane,Func<Result<IProviderOperation<T>>> start,
            ResourcePriority priority=ResourcePriority.Normal,Func<bool> stillWanted=null)
        {
            if(lane!=ResourceLane.Download&&lane!=ResourceLane.Load)return Reject<T>("schedule.lane",ErrorKind.InvalidArgument);
            return Enqueue((int)lane,start,priority,stillWanted);
        }
        public Result<IScheduledOperation<T>> ScheduleFrame<T>(Func<Result<T>> work,
            ResourcePriority priority=ResourcePriority.Normal,Func<bool> stillWanted=null)
        {
            if(work==null)return Reject<T>("schedule.work.null",ErrorKind.InvalidArgument);
            return Enqueue(2,()=>Result<IProviderOperation<T>>.Success(new Immediate<T>(work())),priority,stillWanted);
        }
        private Result<IScheduledOperation<T>> Enqueue<T>(int lane,Func<Result<IProviderOperation<T>>> start,ResourcePriority priority,Func<bool> wanted)
        {
            CleanupWait.MainThread();
            if(stopped)return Reject<T>("schedule.stopped",ErrorKind.NotReady);
            if(start==null||!Valid(priority))return Reject<T>("schedule.arguments",ErrorKind.InvalidArgument);
            if(priority==ResourcePriority.Prefetch&&IsPrefetchSuppressed)return Reject<T>("schedule.prefetch.suppressed",ErrorKind.Unavailable);
            if(queues[lane].Count>=budget.QueueCapacityPerLane)return Reject<T>("schedule.queue.full",ErrorKind.Unavailable);
            var work=new Work<T>(this,lane,++sequence,start,priority,wanted);queues[lane].Add(work);
            return Result<IScheduledOperation<T>>.Success(work);
        }
        public IDisposable SuppressPrefetch()
        {
            CleanupWait.MainThread();prefetchGates++;
            // Gate first: cancellation continuations cannot replenish speculative work.
            foreach(var queue in queues)foreach(var work in queue.ToArray())if(work.Priority==ResourcePriority.Prefetch)work.CancelQueued();
            return new PrefetchGate(this);
        }
        private sealed class PrefetchGate:IDisposable
        {
            private ResourceWorkScheduler owner;internal PrefetchGate(ResourceWorkScheduler owner){this.owner=owner;}
            public void Dispose(){CleanupWait.MainThread();if(owner==null)return;owner.prefetchGates--;owner=null;}
        }
        // Called once per frame by RuntimeHost, or explicitly by deterministic/manual integrations.
        public void Tick()
        {
            CleanupWait.MainThread();if(stopped||ticking)return;ticking=true;
            try
            {
                Pump(0,budget.Downloads);Pump(1,budget.Loads);
                LastFrameUnits=0;var began=clock.Seconds;
                while(!stopped&&LastFrameUnits<budget.FrameUnits&&clock.Seconds-began<budget.FrameTime.TotalSeconds)
                {
                    var work=Next(2);if(work==null)break;LastFrameUnits++;work.Start();
                }
                if(LastFrameUnits!=0&&clock.Seconds-began>budget.FrameTime.TotalSeconds)FrameBudgetOverruns++;
            }
            finally{ticking=false;}
        }
        private void Pump(int lane,int limit)
        {
            // Prune even when all slots are occupied, so closed owners don't retain queued callbacks.
            Prune(lane);
            for(var count=0;!stopped&&active[lane]<limit&&count<budget.MaxStartsPerLanePerTick;count++)
            {var work=Next(lane);if(work==null)break;work.Start();}
        }
        private void Prune(int lane)
        {
            // A demand callback must be a pure main-thread predicate. Work can disappear via continuations.
            for(var i=queues[lane].Count-1;i>=0;i--)
            {if(i>=queues[lane].Count)continue;var work=queues[lane][i];work.CheckDemand();}
        }
        private Work Next(int lane)
        {
            Prune(lane);Work best=null;
            foreach(var work in queues[lane])
                if(best==null||work.Priority>best.Priority||work.Priority==best.Priority&&work.Sequence<best.Sequence)best=work;
            return best;
        }
        public DiagnosticCounters ReadDiagnostics()=>new DiagnosticCounters { PendingOperations=active[0]+active[1]+active[2],QueuedCallbacks=QueuedDownloads+QueuedLoads+QueuedFrameUnits };
        private static bool Valid(ResourcePriority p)=>p>=ResourcePriority.Prefetch&&p<=ResourcePriority.Critical;
        private static Result Failure(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
        private static Result<IScheduledOperation<T>> Reject<T>(string code,ErrorKind kind)=>Result<IScheduledOperation<T>>.Failure(new FrameworkError(kind,code));
        private sealed class StopwatchClock:IMonotonicClock {public double Seconds=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}
        private abstract class Work:IScheduledWork
        {
            protected readonly ResourceWorkScheduler Owner;
            protected readonly int Lane;
            private Func<bool> wanted;
            public long Sequence { get; }
            public ResourcePriority Priority { get; private set; }
            public ResourceWorkState SchedulingState { get; protected set; }=ResourceWorkState.Queued;
            protected Work(ResourceWorkScheduler owner,int lane,long sequence,ResourcePriority priority,Func<bool> wanted)
            {Owner=owner;Lane=lane;Sequence=sequence;Priority=priority;this.wanted=wanted;}
            public bool Promote(ResourcePriority priority)
            {CleanupWait.MainThread();if(!Valid(priority)||SchedulingState!=ResourceWorkState.Queued)return false;if(priority>Priority)Priority=priority;return true;}
            public bool CancelQueued()
            {
                CleanupWait.MainThread();if(SchedulingState!=ResourceWorkState.Queued)return false;
                Owner.queues[Lane].Remove(this);wanted=null;Reject(new FrameworkError(ErrorKind.Canceled,"schedule.queued.canceled"));return true;
            }
            internal void CheckDemand()
            {
                if(SchedulingState!=ResourceWorkState.Queued||wanted==null)return;
                try{if(!wanted())CancelQueued();}
                catch(Exception e){Owner.queues[Lane].Remove(this);wanted=null;Reject(new FrameworkError(ErrorKind.Unexpected,"schedule.demand.exception",e.Message));}
            }
            internal void Start()
            {
                // Cancel completions can enqueue new work during pruning. Recheck at admission.
                CheckDemand();
                if(SchedulingState!=ResourceWorkState.Queued || Owner.stopped)return;
                Owner.queues[Lane].Remove(this);wanted=null;SchedulingState=ResourceWorkState.Running;Owner.active[Lane]++;Run();
            }
            protected void Finished(){Owner.active[Lane]--;SchedulingState=ResourceWorkState.Completed;}
            protected abstract void Run();
            protected abstract void Reject(FrameworkError error);
        }
        private sealed class Work<T>:Work,IScheduledOperation<T>
        {
            private Func<Result<IProviderOperation<T>>> factory;
            private readonly UniTaskCompletionSource<Result<T>> source=new UniTaskCompletionSource<Result<T>>();
            private UniTask driver;
            public string Id {get;}=Guid.NewGuid().ToString("N");
            public BackendState State {get;private set;}=BackendState.Running;
            public UniTask<Result<T>> Completion=>source.Task;
            internal Work(ResourceWorkScheduler owner,int lane,long sequence,Func<Result<IProviderOperation<T>>> factory,ResourcePriority priority,Func<bool> wanted)
                :base(owner,lane,sequence,priority,wanted){this.factory=factory;}
            protected override void Run(){driver=RunActual().Preserve();}
            private async UniTask RunActual()
            {
                Result<T> result;
                try
                {
                    var start=factory;factory=null;var accepted=start();
                    result=!accepted.IsSuccess?Result<T>.Failure(accepted.Error):accepted.Value==null?
                        Result<T>.Failure(new FrameworkError(ErrorKind.Integrity,"schedule.operation.null")):await accepted.Value.Completion;
                }
                catch(Exception e){result=Result<T>.Failure(new FrameworkError(ErrorKind.Provider,"schedule.operation.exception",e.Message));}
                await UniTask.SwitchToMainThread();Finished();Publish(result);
            }
            protected override void Reject(FrameworkError error){factory=null;SchedulingState=ResourceWorkState.Completed;Publish(Result<T>.Failure(error));}
            private void Publish(Result<T> result){State=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;source.TrySetResult(result);}
        }
        private sealed class Immediate<T>:IProviderOperation<T>
        {
            private readonly Result<T> result;
            public string Id {get;}=Guid.NewGuid().ToString("N");
            public BackendState State=>result.IsSuccess?BackendState.Succeeded:BackendState.Failed;
            public UniTask<Result<T>> Completion=>UniTask.FromResult(result);
            internal Immediate(Result<T> result){this.result=result;}
        }
    }
}
