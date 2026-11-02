using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MiniFramework.Pool
{
    // Unity adapter only: Contracts and Runtime remain engine independent.
    public sealed class GameObjectPool : IPoolService<GameObject>,IModule,IAsyncCleanup,IDiagnosticSource,IResourceTrimTarget,IPoolResourceDiagnostics
    {
        private readonly IAssetsService assets;
        private readonly IResourceWorkScheduler scheduler;
        private readonly PoolCapacity capacity;
        private readonly CleanupContext reclaim;
        private readonly Dictionary<ResourceKey,Bucket> buckets=new Dictionary<ResourceKey,Bucket>();
        private readonly List<Request> requests=new List<Request>();
        private GameObject storage;
        private IDisposable ticks;
        private bool ready,stopping,polling;
        private readonly List<Request> destroyedRents=new List<Request>();
        private readonly List<Bucket> emptyBuckets=new List<Bucket>();
        private UniTask cleanupDriver,trimDriver;
        private UniTask<Result>? trimming;
        private long serial;
        private readonly bool useHost;
        private UniTask<Result>? cleanupAttempt;
        public string Id {get;}
        public IReadOnlyList<string> Dependencies {get;}
        public int BucketCount=>buckets.Count;
        public int ActiveCount {get {var n=0;foreach(var b in buckets.Values)n+=b.Active;return n;}}
        public int IdleCount {get {var n=0;foreach(var b in buckets.Values)n+=b.Idle.Count;return n;}}
        public int PendingCount {get {var n=0;foreach(var b in buckets.Values)n+=b.Pending;return n;}}
        public int RetainedInstanceCount {get {var n=0;foreach(var b in buckets.Values)n+=b.Instances.Count;return n;}}
        public int CreatedCount {get;private set;}
        public GameObjectPool(IAssetsService assets,IResourceWorkScheduler scheduler,PoolCapacity capacity,CleanupContext reclaim,
            string assetsModuleId="assets",string schedulerModuleId="resource-scheduler",string hostModuleId=null,string id="pool")
        {
            MainThread();this.assets=assets??throw new ArgumentNullException(nameof(assets));this.scheduler=scheduler??throw new ArgumentNullException(nameof(scheduler));
            this.capacity=capacity??throw new ArgumentNullException(nameof(capacity));this.reclaim=reclaim;Id=id;
            var deps=new List<string>();foreach(var dep in new[]{assetsModuleId,schedulerModuleId}){if(string.IsNullOrWhiteSpace(dep))throw new ArgumentException("Dependency id required.");if(!deps.Contains(dep))deps.Add(dep);}
            if(hostModuleId!=null){if(string.IsNullOrWhiteSpace(hostModuleId))throw new ArgumentException("Host id required.");if(!deps.Contains(hostModuleId))deps.Add(hostModuleId);useHost=true;}
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Pool id required.");Dependencies=deps.AsReadOnly();
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            MainThread();if(ready||stopping)return UniTask.FromResult(Fail("pool.initialize.state",ErrorKind.InvalidState));
            var interrupted=Interrupted(operation,Stopwatch.GetTimestamp());if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            IRuntimeScheduler host=null;if(useHost&&!context.Services.TryGet(out host))return UniTask.FromResult(Fail("pool.host.missing",ErrorKind.Dependency));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            storage=new GameObject("MiniFramework.Pool.Storage");storage.SetActive(false);Object.DontDestroyOnLoad(storage);
            if(host!=null)ticks=host.SubscribeTick((_,__)=>PollDestroyed());ready=true;return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> StopAsync(CleanupContext context){Stop();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        private void Stop(){MainThread();stopping=true;ready=false;ticks?.Dispose();ticks=null;}
        public Result<IOwnedOperation<IPoolRent<GameObject>>> Spawn(ResourceKey key,IScope owner,ResourcePriority priority=ResourcePriority.Normal)
        {
            MainThread();if(!ready||stopping)return Reject<IOwnedOperation<IPoolRent<GameObject>>>("pool.not_ready",ErrorKind.NotReady);
            if(!key.IsValid||key.Kind!=ResourceKind.Asset||key.AssetType!=typeof(GameObject)||owner==null||owner.State!=ScopeState.Open||priority<ResourcePriority.Prefetch||priority>ResourcePriority.Critical)
                return Reject<IOwnedOperation<IPoolRent<GameObject>>>("pool.spawn.arguments",ErrorKind.InvalidArgument);
            // Cold requests are not reservations. Capacity is claimed on first Wait.
            var request=new Request(this,key,owner,++serial,priority);var ticket=owner.Cleanup.Register(request);
            if(!ticket.IsSuccess)return Result<IOwnedOperation<IPoolRent<GameObject>>>.Failure(ticket.Error);
            request.Cleanup=ticket.Value;requests.Add(request);return Result<IOwnedOperation<IPoolRent<GameObject>>>.Success(request);
        }
        private Result<Bucket> Reserve(ResourceKey key)
        {
            if(!buckets.TryGetValue(key,out var b))
            {
                if(buckets.Count>=capacity.MaxBuckets)return Reject<Bucket>("pool.bucket.capacity",ErrorKind.Unavailable);
                b=new Bucket(this,key);buckets.Add(key,b);
            }
            if(b.Closing)return Reject<Bucket>("pool.bucket.closing",ErrorKind.Unavailable);
            // Active includes returns awaiting dynamic cleanup. Pending reserves both idle and new slots.
            if(b.Active+b.Pending+b.Destroying>=capacity.MaxInstancesPerBucket)return Reject<Bucket>("pool.instance.capacity",ErrorKind.Unavailable);
            b.Pending++;return Result<Bucket>.Success(b);
        }
        public void PollDestroyed()
        {
            MainThread();if(polling)return;polling=true;
            try
            {
                destroyedRents.Clear();emptyBuckets.Clear();
                foreach(var b in buckets.Values)
                {
                    for(var i=b.Instances.Count-1;i>=0;i--)
                    {
                        var slot=b.Instances[i];if(slot.Object!=null)continue;
                        if(slot.Rent!=null){if(slot.Rent.State==PoolRentState.Active)destroyedRents.Add(slot.Rent);}
                        else {b.Idle.Remove(slot);b.Instances.RemoveAt(i);}
                    }
                    if(b.Pending==0&&b.Active==0&&b.Destroying==0&&b.Instances.Count==0&&!b.Closing)emptyBuckets.Add(b);
                }
                // Cleanup may invoke arbitrary user continuations; never run it inside dictionary traversal.
                foreach(var rent in destroyedRents)rent.BeginCleanup(reclaim,false);
                foreach(var b in emptyBuckets)if(b.Pending==0&&b.Active==0&&b.Destroying==0&&b.Instances.Count==0&&!b.Closing)b.Evict(reclaim);
            }
            finally {destroyedRents.Clear();emptyBuckets.Clear();polling=false;}
        }
        public async UniTask<Result> PrewarmAsync(ResourceKey key,int count,IScope owner,OperationContext wait)
        {
            MainThread();if(count<0||count>capacity.MaxIdlePerBucket)return Fail("pool.prewarm.capacity",ErrorKind.InvalidArgument);
            if(!ready||stopping)return Fail("pool.not_ready",ErrorKind.NotReady);
            if(owner==null||owner.State!=ScopeState.Open||!key.IsValid||key.AssetType!=typeof(GameObject))return Fail("pool.prewarm.arguments",ErrorKind.InvalidArgument);
            var began=Stopwatch.GetTimestamp();var initial=Interrupted(wait,began);if(initial.HasValue)return initial.Value;
            var held=new List<IPoolRent<GameObject>>(count);Result result=Result.Success();
            // Holding distinct rentals avoids repeatedly warming the same cached instance.
            try
            {
                for(var i=0;i<count;i++)
                {
                    var interrupted=Interrupted(wait,began);if(interrupted.HasValue){result=interrupted.Value;break;}
                    var accepted=Spawn(key,owner,ResourcePriority.Prefetch);if(!accepted.IsSuccess){result=Result.Failure(accepted.Error);break;}
                    var remaining=wait.Timeout==Timeout.InfiniteTimeSpan?wait.Timeout:TimeSpan.FromSeconds(Math.Max(0,wait.Timeout.TotalSeconds-Elapsed(began)));
                    var spawned=await accepted.Value.WaitAsync(new OperationContext(wait.Cancellation,remaining));
                    if(!spawned.IsSuccess){result=Result.Failure(spawned.Error);break;}held.Add(spawned.Value);
                }
            }
            finally {foreach(var rent in held){var returned=await ((Request)rent).BeginCleanup(reclaim,false);if(!returned.IsSuccess)result=returned;}}
            return result;
        }
        public UniTask<Result> EvictAsync(ResourceKey key,CleanupContext cleanup)
        {
            MainThread();if(!buckets.TryGetValue(key,out var b))return UniTask.FromResult(Result.Success());
            if(b.Pending!=0||b.Active!=0||b.Destroying!=0)return UniTask.FromResult(Fail("pool.bucket.busy",ErrorKind.Unavailable));
            return Observe(b.Evict(cleanup),new OperationContext(cleanup.ShutdownCancellation,cleanup.Timeout));
        }
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Stop();if(cleanupAttempt.HasValue&&cleanupAttempt.Value.Status==UniTaskStatus.Pending)return cleanupAttempt.Value;
            var completion=new UniTaskCompletionSource<Result>();cleanupAttempt=completion.Task;
            cleanupDriver=CompleteDrain(cleanup,completion).Preserve();return completion.Task;
        }
        private async UniTask CompleteDrain(CleanupContext cleanup,UniTaskCompletionSource<Result> completion)
        {
            try {if(trimming.HasValue&&trimming.Value.Status==UniTaskStatus.Pending)await trimming.Value;completion.TrySetResult(await Drain(cleanup));}
            catch(Exception e){completion.TrySetResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"pool.cleanup.exception",e.Message)));}
        }
        private async UniTask<Result> Drain(CleanupContext cleanup)
        {
            var failed=false;
            foreach(var request in requests.ToArray())if(!(await request.BeginCleanup(cleanup,true)).IsSuccess)failed=true;
            if(failed)return Fail("pool.rents.cleanup",ErrorKind.CleanupFailed);
            foreach(var bucket in new List<Bucket>(buckets.Values))if(!(await bucket.Evict(cleanup)).IsSuccess)failed=true;
            if(failed)return Fail("pool.buckets.cleanup",ErrorKind.CleanupFailed);
            if(storage!=null){Object.Destroy(storage);while(storage!=null)await UniTask.Yield();}return Result.Success();
        }
        public PoolResourceSnapshot CapturePoolResources()
        {
            MainThread();var bucketRows=new List<PoolBucketSnapshot>();var rentRows=new List<PoolRentSnapshot>();
            foreach(var b in buckets.Values)bucketRows.Add(new PoolBucketSnapshot(b.Key,b.Scope.Id,b.Active,b.Idle.Count,b.Pending,b.Destroying,b.Closing,b.Scope.Cleanup.Snapshot.FailedCount));
            foreach(var request in requests)rentRows.Add(request.Snapshot);
            return new PoolResourceSnapshot(bucketRows,rentRows);
        }
        public UniTask<Result> TrimIdleAsync(int destroysPerFrame,CleanupContext cleanup)
        {
            MainThread();if(destroysPerFrame<=0)return UniTask.FromResult(Fail("pool.trim.budget",ErrorKind.InvalidArgument));
            if(stopping)return UniTask.FromResult(Fail("pool.trim.stopped",ErrorKind.NotReady));
            if(trimming.HasValue&&trimming.Value.Status==UniTaskStatus.Pending)return trimming.Value;
            var completion=new UniTaskCompletionSource<Result>();trimming=completion.Task;
            trimDriver=TrimActual(destroysPerFrame,cleanup,completion).Preserve();return completion.Task;
        }
        private async UniTask TrimActual(int perFrame,CleanupContext cleanup,UniTaskCompletionSource<Result> completion)
        {
            Result result=Result.Success();
            // Remove candidates from the idle stack before yielding. Spawn cannot rent a pending destroy.
            var candidates=new List<(Bucket bucket,Slot slot)>();var snapshot=new List<Bucket>(buckets.Values);
            foreach(var b in snapshot)if(!b.Closing){foreach(var slot in b.Idle){candidates.Add((b,slot));b.Destroying++;}b.Idle.Clear();}
            try
            {
                var units=0;
                foreach(var candidate in candidates)
                {
                    if(units==perFrame){await UniTask.NextFrame();units=0;}
                    if(candidate.slot.Object!=null)Object.Destroy(candidate.slot.Object);units++;
                }
                foreach(var candidate in candidates)
                {
                    while(candidate.slot.Object!=null)await UniTask.Yield();
                    candidate.bucket.Instances.Remove(candidate.slot);candidate.bucket.Destroying--;
                }
                foreach(var b in snapshot)
                {
                    if(b.Closing){if(b.Scope.Cleanup.Snapshot.FailedCount>0)result=Fail("pool.trim.retained_failure",ErrorKind.CleanupFailed);continue;}
                    if(b.Pending==0&&b.Active==0&&b.Destroying==0&&b.Instances.Count==0)
                        if(!(await b.Evict(cleanup)).IsSuccess)result=Fail("pool.trim.release",ErrorKind.CleanupFailed);
                }
            }
            catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"pool.trim.exception",e.Message));}
            completion.TrySetResult(result);
        }
        public DiagnosticCounters ReadDiagnostics()
        {
            var failed=0;foreach(var r in requests)if(r.State==PoolRentState.ReturnFailed)failed++;
            var evicting=0;foreach(var b in buckets.Values){failed+=b.Scope.Cleanup.Snapshot.FailedCount;if(b.IsEvicting)evicting++;}
            return new DiagnosticCounters{ActiveRentals=ActiveCount,PendingOperations=PendingCount+evicting,FailedCleanup=failed};
        }
        private sealed class Slot {internal GameObject Object;internal Request Rent;}
        private sealed class Bucket
        {
            internal readonly ResourceKey Key;internal readonly GameObjectPool Pool;internal readonly Scope Scope;
            internal readonly List<Slot> Instances=new List<Slot>();internal readonly List<Slot> Idle=new List<Slot>();
            internal int Pending,Active,Destroying;internal bool Closing;
            private IAssetLease<GameObject> prefab;
            private IOwnedOperation<IAssetLease<GameObject>> loadRequest;
            private UniTaskCompletionSource<Result<IAssetLease<GameObject>>> loaded;
            private UniTask loader,evictionDriver;
            private UniTask<Result>? eviction;
            internal bool IsEvicting=>eviction.HasValue&&eviction.Value.Status==UniTaskStatus.Pending;
            internal Bucket(GameObjectPool pool,ResourceKey key){Pool=pool;Key=key;Scope=new Scope(pool.Id+"/bucket/"+(++pool.serial));}
            internal UniTask<Result<IAssetLease<GameObject>>> Load(ResourcePriority priority)
            {
                if(loaded==null){loaded=new UniTaskCompletionSource<Result<IAssetLease<GameObject>>>();loader=LoadActual(priority).Preserve();}
                else (loadRequest as IPrioritizedOperation)?.Promote(priority);
                return loaded.Task;
            }
            private async UniTask LoadActual(ResourcePriority priority)
            {
                Result<IAssetLease<GameObject>> result;
                try {var accepted=Pool.assets.Load<GameObject>(Key,Scope,priority);if(accepted.IsSuccess){loadRequest=accepted.Value;result=await loadRequest.WaitAsync(OperationContext.Unlimited());}else result=Result<IAssetLease<GameObject>>.Failure(accepted.Error);}
                catch(Exception e){result=Reject<IAssetLease<GameObject>>("pool.prefab.exception",ErrorKind.Provider,e.Message);}
                if(result.IsSuccess)prefab=result.Value;loaded.TrySetResult(result);
            }
            internal Slot TakeOrCreate(GameObject prototype)
            {
                while(Idle.Count>0){var slot=Idle[Idle.Count-1];Idle.RemoveAt(Idle.Count-1);if(slot.Object!=null){Prepare(slot.Object,prototype);return slot;}Instances.Remove(slot);}
                // Parent is inactive: no OnEnable occurs before the rental owns the clone.
                var go=Object.Instantiate(prototype,Pool.storage.transform,false);go.SetActive(false);
                var created=new Slot{Object=go};Instances.Add(created);Pool.CreatedCount++;Prepare(go,prototype);return created;
            }
            private static void Prepare(GameObject go,GameObject prototype)
            {
                go.SetActive(false);go.transform.SetParent(null,false);
                go.transform.localPosition=prototype.transform.localPosition;go.transform.localRotation=prototype.transform.localRotation;go.transform.localScale=prototype.transform.localScale;
            }
            internal UniTask<Result> Evict(CleanupContext cleanup)
            {
                Closing=true;if(eviction.HasValue&&eviction.Value.Status==UniTaskStatus.Pending)return eviction.Value;
                var completion=new UniTaskCompletionSource<Result>();eviction=completion.Task;
                evictionDriver=CompleteEviction(cleanup,completion).Preserve();return completion.Task;
            }
            private async UniTask CompleteEviction(CleanupContext cleanup,UniTaskCompletionSource<Result> completion)
            {
                try {completion.TrySetResult(await EvictActual(cleanup));}
                catch(Exception e){completion.TrySetResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"pool.eviction.exception",e.Message)));}
            }
            private async UniTask<Result> EvictActual(CleanupContext cleanup)
            {
                if(Pending!=0||Active!=0||Destroying!=0)return Fail("pool.bucket.busy",ErrorKind.Unavailable);
                foreach(var slot in Instances)if(slot.Object!=null)Object.Destroy(slot.Object);
                foreach(var slot in Instances)while(slot.Object!=null)await UniTask.Yield();
                Idle.Clear();Instances.Clear();
                var report=Scope.State==ScopeState.Open?await Scope.CloseAsync(cleanup):await Scope.RetryFailedAsync(cleanup);
                while(report.PendingCount>0){await UniTask.Yield();report=Scope.Cleanup.Snapshot;}
                if(!report.IsFullyDrained)return Fail("pool.prefab.cleanup",ErrorKind.CleanupFailed);
                prefab=null;loaded=null;loadRequest=null;if(Pool.buckets.TryGetValue(Key,out var current)&&ReferenceEquals(current,this))Pool.buckets.Remove(Key);return Result.Success();
            }
        }
        private sealed class Request:IOwnedOperation<IPoolRent<GameObject>>,IPoolRent<GameObject>,IAsyncCleanup
        {
            private readonly GameObjectPool pool;private readonly ResourceKey key;private readonly IScope owner;private readonly ResourcePriority priority;
            private Bucket bucket;private Slot slot;private bool waited,cleaning,spawnDone;
            private OperationContext wait;private long began;
            private UniTaskCompletionSource<Result<IPoolRent<GameObject>>> spawned=new UniTaskCompletionSource<Result<IPoolRent<GameObject>>>();
            private UniTask spawnDriver,cleanupDriver;private UniTask<Result>? cleanup;
            private IScheduledOperation<Slot> frame;
            public PoolRentSnapshot Snapshot=>new PoolRentSnapshot(key,Id,owner.Id,RentScope?.Id,State);
            public string Id {get;}public ICleanupTicket Cleanup{get;internal set;}
            public BackendState BackendState{get;private set;}=BackendState.Running;
            public PoolRentState State{get;private set;}=PoolRentState.Pending;
            public IScope RentScope{get;private set;}
            public GameObject Value {get{MainThread();if(State!=PoolRentState.Active||owner.State!=ScopeState.Open||slot.Object==null)throw new InvalidOperationException("Rental is not active.");return slot.Object;}}
            private bool Wanted=>!cleaning&&!pool.stopping&&owner.State==ScopeState.Open&&!Interrupted(wait,began).HasValue;
            internal Request(GameObjectPool pool,ResourceKey key,IScope owner,long id,ResourcePriority priority){this.pool=pool;this.key=key;this.owner=owner;this.priority=priority;Id=pool.Id+"/rent/"+id;}
            public async UniTask<Result<IPoolRent<GameObject>>> WaitAsync(OperationContext operation)
            {
                MainThread();if(waited)return Reject<IPoolRent<GameObject>>("pool.wait.once",ErrorKind.InvalidState);waited=true;wait=operation;began=Stopwatch.GetTimestamp();
                if(Wanted)spawnDriver=SpawnActual().Preserve();else {spawnDone=true;BackendState=BackendState.Failed;}
                while(true)
                {
                    var interrupted=Interrupted(wait,began);
                    if(!Wanted){BeginCleanup(pool.reclaim,false);return Result<IPoolRent<GameObject>>.Failure(interrupted?.Error??new FrameworkError(ErrorKind.Canceled,"pool.spawn.canceled"));}
                    if(spawned.Task.Status!=UniTaskStatus.Pending){var result=await spawned.Task;if(!result.IsSuccess)BeginCleanup(pool.reclaim,false);return result;}
                    await UniTask.Yield();
                }
            }
            private async UniTask SpawnActual()
            {
                Result<IPoolRent<GameObject>> result;
                try
                {
                    var reserved=pool.Reserve(key);
                    if(!reserved.IsSuccess)result=Result<IPoolRent<GameObject>>.Failure(reserved.Error);
                    else
                    {
                        bucket=reserved.Value;var loaded=await bucket.Load(priority);
                        if(!loaded.IsSuccess)result=Result<IPoolRent<GameObject>>.Failure(loaded.Error);
                        else if(!Wanted)result=Reject<IPoolRent<GameObject>>("pool.spawn.canceled",ErrorKind.Canceled);
                        else
                        {
                            var scheduled=pool.scheduler.ScheduleFrame(()=>Result<Slot>.Success(bucket.TakeOrCreate(loaded.Value.Value)),priority,()=>Wanted);
                            if(!scheduled.IsSuccess)result=Result<IPoolRent<GameObject>>.Failure(scheduled.Error);
                            else
                            {
                                frame=scheduled.Value;var actual=await frame.Completion;frame=null;
                                if(!actual.IsSuccess)result=Result<IPoolRent<GameObject>>.Failure(actual.Error);
                                else
                                {
                                    slot=actual.Value;slot.Rent=this;bucket.Active++;
                                    var child=owner.CreateChild(Id,ScopeKind.Rent);
                                    if(!child.IsSuccess)result=Result<IPoolRent<GameObject>>.Failure(child.Error);
                                    else {RentScope=child.Value;State=PoolRentState.Active;result=Result<IPoolRent<GameObject>>.Success(this);}
                                }
                            }
                        }
                    }
                }
                catch(Exception e){result=Reject<IPoolRent<GameObject>>("pool.spawn.exception",ErrorKind.Provider,e.Message);}
                finally {if(bucket!=null)bucket.Pending--;spawnDone=true;}
                BackendState=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;spawned.TrySetResult(result);
            }
            public UniTask<Result> ReturnAsync(CleanupContext context)
            {
                MainThread();if(State==PoolRentState.Returned)return UniTask.FromResult(Fail("pool.return.duplicate",ErrorKind.InvalidState));
                if(State==PoolRentState.ReturnFailed)return UniTask.FromResult(Fail("pool.return.retry_required",ErrorKind.InvalidState));
                return Observe(BeginCleanup(context,false),new OperationContext(context.ShutdownCancellation,context.Timeout));
            }
            public UniTask<Result> RetryReturnAsync(CleanupContext context)
            {MainThread();if(State!=PoolRentState.ReturnFailed)return UniTask.FromResult(Fail("pool.return.retry_state",ErrorKind.InvalidState));return Observe(BeginCleanup(context,true),new OperationContext(context.ShutdownCancellation,context.Timeout));}
            public UniTask<Result> CleanupAsync(CleanupContext context)=>BeginCleanup(context,true);
            internal UniTask<Result> BeginCleanup(CleanupContext context,bool retry)
            {
                if(cleanup.HasValue&&(cleanup.Value.Status==UniTaskStatus.Pending||State==PoolRentState.Returned||!retry))return cleanup.Value;
                cleaning=true;State=PoolRentState.Returning;frame?.CancelQueued();
                var completion=new UniTaskCompletionSource<Result>();cleanup=completion.Task;
                cleanupDriver=CompleteCleanup(context,completion).Preserve();return completion.Task;
            }
            private async UniTask CompleteCleanup(CleanupContext context,UniTaskCompletionSource<Result> completion)
            {
                Result result;
                try {result=await CleanActual(context);}
                catch(Exception e){State=PoolRentState.ReturnFailed;result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"pool.return.exception",e.Message));}
                completion.TrySetResult(result);
            }
            private async UniTask<Result> CleanActual(CleanupContext context)
            {
                if(waited)while(!spawnDone)await UniTask.Yield();
                if(slot!=null&&slot.Object!=null)slot.Object.SetActive(false);
                if(RentScope!=null)
                {
                    var report=RentScope.State==ScopeState.Open?await RentScope.CloseAsync(context):await RentScope.RetryFailedAsync(context);
                    while(report.PendingCount>0){await UniTask.Yield();report=RentScope.Cleanup.Snapshot;}
                    if(!report.IsFullyDrained){State=PoolRentState.ReturnFailed;return Fail("pool.rent.cleanup",ErrorKind.CleanupFailed);}
                }
                if(slot!=null)
                {
                    if(slot.Object!=null&&!pool.stopping&&!bucket.Closing&&bucket.Idle.Count<pool.capacity.MaxIdlePerBucket)
                    {
                        slot.Object.transform.SetParent(pool.storage.transform,false);
                        slot.Object.transform.localPosition=Vector3.zero;slot.Object.transform.localRotation=Quaternion.identity;slot.Object.transform.localScale=Vector3.one;
                        bucket.Idle.Add(slot);
                    }
                    else {if(slot.Object!=null)Object.Destroy(slot.Object);while(slot.Object!=null)await UniTask.Yield();bucket.Instances.Remove(slot);}
                    slot.Rent=null;slot=null;bucket.Active--;
                }
                if(bucket!=null&&bucket.Pending==0&&bucket.Active==0&&bucket.Destroying==0&&bucket.Instances.Count==0)
                {
                    var evicted=await bucket.Evict(context);
                    if(!evicted.IsSuccess){State=PoolRentState.ReturnFailed;return evicted;}
                }
                bucket=null;pool.requests.Remove(this);State=PoolRentState.Returned;return Result.Success();
            }
        }
        private static void MainThread(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Pool belongs to main thread.");}
        private static double Elapsed(long began)=>(Stopwatch.GetTimestamp()-began)/(double)Stopwatch.Frequency;
        private static Result? Interrupted(OperationContext wait,long began)
        {if(wait.Cancellation.IsCancellationRequested)return Fail("pool.wait.canceled",ErrorKind.Canceled);if(wait.Timeout!=Timeout.InfiniteTimeSpan&&Elapsed(began)>=wait.Timeout.TotalSeconds)return Fail("pool.wait.timeout",ErrorKind.Timeout);return null;}
        private static async UniTask<Result> Observe(UniTask<Result> actual,OperationContext wait)
        {var began=Stopwatch.GetTimestamp();while(true){var interrupted=Interrupted(wait,began);if(interrupted.HasValue)return interrupted.Value;if(actual.Status!=UniTaskStatus.Pending)return await actual;await UniTask.Yield();}}
        private static Result Fail(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
        private static Result<T> Reject<T>(string code,ErrorKind kind,string detail=null)=>Result<T>.Failure(new FrameworkError(kind,code,detail));
    }
}
