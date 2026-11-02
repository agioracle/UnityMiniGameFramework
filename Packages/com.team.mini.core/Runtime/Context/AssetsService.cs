using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class AssetsService : IAssetsService, IModule, IDiagnosticSource, IAssetResourceDiagnostics
    {
        private interface IEntry
        {
            bool Loading {get;}
            bool Downloading {get;}
            bool Pending {get;}
            bool Reclaiming {get;}
            bool ReleaseFailed {get;}
            int Interests {get;}
        }
        private interface IRequest
        {
            ResourceOwnerSnapshot Snapshot {get;}
            UniTask<Result> Clean(CleanupContext cleanup,bool retry);
        }
        private readonly IAssetProvider provider;
        private readonly IPackageReadiness packageReadiness;
        private readonly IResourceWorkScheduler scheduler;
        private readonly string package;
        private string version;
        private readonly bool selectPackageVersion;
        private readonly CleanupContext reclaimPolicy;
        private readonly string instance=Guid.NewGuid().ToString("N");
        private readonly Dictionary<ResourceKey,IEntry> entries=new Dictionary<ResourceKey,IEntry>();
        private readonly HashSet<IRequest> requests=new HashSet<IRequest>();
        private int next;
        private bool ready,stopping;
        public string Id {get;}
        public IReadOnlyList<string> Dependencies { get; }
        public int RetainedEntryCount {get{CleanupWait.MainThread();return entries.Count;}}
        public int RetainedRequestCount {get{CleanupWait.MainThread();return requests.Count;}}
        public int ActualLoadingCount {get{CleanupWait.MainThread();var count=0;foreach(var e in entries.Values)if(e.Loading)count++;return count;}}
        public int ActualDownloadingCount {get{CleanupWait.MainThread();var count=0;foreach(var e in entries.Values)if(e.Downloading)count++;return count;}}
        public int PendingLoadCount {get{CleanupWait.MainThread();var count=0;foreach(var e in entries.Values)if(e.Pending)count++;return count;}}
        public AssetsService(IAssetProvider provider,string package,string contentVersion,CleanupContext reclaimPolicy,string id="assets",string providerModuleId=null,IResourceWorkScheduler scheduler=null,string schedulerModuleId=null,IPackageReadiness packageReadiness=null,string packageModuleId=null)
        {
            CleanupWait.MainThread();this.provider=provider??throw new ArgumentNullException(nameof(provider));
            if(string.IsNullOrWhiteSpace(package)||string.IsNullOrWhiteSpace(contentVersion)||string.IsNullOrWhiteSpace(id))throw new ArgumentException("Frozen package/version/service id required.");
            if(providerModuleId!=null&&string.IsNullOrWhiteSpace(providerModuleId))throw new ArgumentException("Provider module id must be nonblank.");
            if(schedulerModuleId!=null&&(string.IsNullOrWhiteSpace(schedulerModuleId)||scheduler==null))throw new ArgumentException("Scheduler dependency requires an instance and nonblank id.");
            if((packageReadiness==null)!=(packageModuleId==null)||packageModuleId!=null&&string.IsNullOrWhiteSpace(packageModuleId))throw new ArgumentException("Package readiness requires its declared dependency.");
            this.packageReadiness=packageReadiness;
            this.scheduler=scheduler;var deps=new List<string>();if(providerModuleId!=null)deps.Add(providerModuleId);if(schedulerModuleId!=null&&!deps.Contains(schedulerModuleId))deps.Add(schedulerModuleId);if(packageModuleId!=null&&!deps.Contains(packageModuleId))deps.Add(packageModuleId);Dependencies=deps.AsReadOnly();
            this.package=package;version=contentVersion;this.reclaimPolicy=reclaimPolicy;Id=id;
        }
        // Package module selects first; this module freezes that selection during initialization.
        public AssetsService(IAssetProvider provider,IPackageReadiness packageReadiness,CleanupContext reclaimPolicy,string packageModuleId,string id="assets",string providerModuleId=null,IResourceWorkScheduler scheduler=null,string schedulerModuleId=null)
            :this(provider,(packageReadiness??throw new ArgumentNullException(nameof(packageReadiness))).Package,"unselected",reclaimPolicy,id,providerModuleId,scheduler,schedulerModuleId,packageReadiness,packageModuleId)
        {selectPackageVersion=true;}
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            CleanupWait.MainThread();
            if(ready||stopping)return UniTask.FromResult(Error("assets.initialize.state"));
            if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Error("assets.initialize.canceled",ErrorKind.Canceled));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Error("assets.initialize.timeout",ErrorKind.Timeout));
            if(packageReadiness!=null){var selected=selectPackageVersion?packageReadiness.ContentVersion:version;var check=packageReadiness.CheckReady(package,selected);if(!check.IsSuccess)return UniTask.FromResult(check);version=selected;}
            var registered=context.Cleanup.Register(new ServiceCleanup(this));
            if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            ready=true;return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup){CleanupWait.MainThread();stopping=true;return UniTask.FromResult(Result.Success());}
        // The module registry owns asynchronous service drain, enabling Context registry retry.
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public Result<IOwnedOperation<IAssetLease<T>>> Load<T>(ResourceKey key,IScope owner) where T:class=>Load<T>(key,owner,ResourcePriority.Normal);
        public Result<IOwnedOperation<IAssetLease<T>>> Load<T>(ResourceKey key,IScope owner,ResourcePriority priority) where T:class
        {
            CleanupWait.MainThread();
            if(!ready||stopping)return Reject<IOwnedOperation<IAssetLease<T>>>("assets.not_ready",ErrorKind.NotReady);
            if(packageReadiness!=null){var check=packageReadiness.CheckReady(package,version);if(!check.IsSuccess)return Result<IOwnedOperation<IAssetLease<T>>>.Failure(check.Error);}
            if(priority<ResourcePriority.Prefetch||priority>ResourcePriority.Critical)return Reject<IOwnedOperation<IAssetLease<T>>>("assets.priority",ErrorKind.InvalidArgument);
            if(!key.IsValid||key.Kind!=ResourceKind.Asset||key.AssetType!=typeof(T)||key.Provider!=provider.Id||key.Package!=package||key.ContentVersion!=version)
                return Reject<IOwnedOperation<IAssetLease<T>>>("assets.key",ErrorKind.InvalidArgument);
            if(owner==null||owner.State!=ScopeState.Open)return Reject<IOwnedOperation<IAssetLease<T>>>("assets.owner",ErrorKind.InvalidState);
            var request=new Request<T>(this,key,owner,instance+"/request/"+(++next),priority);
            var ticket=owner.Cleanup.Register(request);
            if(!ticket.IsSuccess)return Result<IOwnedOperation<IAssetLease<T>>>.Failure(ticket.Error);
            request.Cleanup=ticket.Value;requests.Add(request);
            return Result<IOwnedOperation<IAssetLease<T>>>.Success(request);
        }
        private Result<Entry<T>> Join<T>(Request<T> request) where T:class
        {
            var key=request.Key;
            if(entries.TryGetValue(key,out var existing))
            {
                var entry=(Entry<T>)existing;
                if(entry.Releasing||entry.ReleaseFailed)return Reject<Entry<T>>("assets.reclaim.pending",ErrorKind.Unavailable);
                entry.Add(request);return Result<Entry<T>>.Success(entry);
            }
            var created=new Entry<T>(this,key);created.Add(request);entries.Add(key,created);return Result<Entry<T>>.Success(created);
        }
        private void Remove(ResourceKey key,IEntry entry)
        {if(entries.TryGetValue(key,out var found)&&ReferenceEquals(found,entry))entries.Remove(key);}
        public DiagnosticCounters ReadDiagnostics()
        {
            CleanupWait.MainThread();var result=new DiagnosticCounters{PendingOperations=PendingLoadCount};
            foreach(var entry in entries.Values){if(entry.ReleaseFailed)result.FailedCleanup++;if(entry.Reclaiming)result.PendingOperations++;}
            foreach(var request in requests)if(request is ILeaseDiagnostic live)result.ActiveRentals+=live.ActiveLease;
            return result;
        }
        public AssetResourceSnapshot CaptureResources()
        {
            CleanupWait.MainThread();var entryRows=new List<ResourceEntrySnapshot>();var ownerRows=new List<ResourceOwnerSnapshot>();
            foreach(var item in entries){var e=item.Value;entryRows.Add(new ResourceEntrySnapshot(item.Key,e.Interests,e.Pending,e.Loading,e.Downloading,e.Reclaiming,e.ReleaseFailed));}
            foreach(var request in requests)ownerRows.Add(request.Snapshot);
            return new AssetResourceSnapshot(entryRows,ownerRows);
        }
        private interface ILeaseDiagnostic {int ActiveLease {get;}}
        public UniTask<Result> CloseAsync(CleanupContext cleanup)=>Drain(cleanup,false);
        public UniTask<Result> RetryCleanupAsync(CleanupContext cleanup)=>Drain(cleanup,true);
        private async UniTask<Result> Drain(CleanupContext cleanup,bool retry)
        {
            CleanupWait.MainThread();stopping=true;
            var snapshot=new List<IRequest>(requests);var tasks=new List<UniTask<Result>>();
            foreach(var request in snapshot)tasks.Add(request.Clean(cleanup,retry));
            var failed=false;foreach(var task in tasks)if(!(await task).IsSuccess)failed=true;
            return failed?Error("assets.cleanup.failed",ErrorKind.CleanupFailed):Result.Success();
        }
        private sealed class ServiceCleanup:IAsyncCleanup
        {
            private readonly AssetsService owner;private bool attempted;
            public string Id=>"assets/drain/"+owner.instance;
            internal ServiceCleanup(AssetsService owner){this.owner=owner;}
            public UniTask<Result> CleanupAsync(CleanupContext cleanup){var retry=attempted;attempted=true;return owner.Drain(cleanup,retry);}
        }
        private static Result Error(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result.Failure(new FrameworkError(kind,code));
        private static Result<T> Reject<T>(string code,ErrorKind kind)=>Result<T>.Failure(new FrameworkError(kind,code));
        private sealed class Entry<T>:IEntry where T:class
        {
            private readonly AssetsService service;
            internal readonly ResourceKey Key;
            internal readonly UniTaskCompletionSource<Result<IProviderAsset<T>>> Loaded=new UniTaskCompletionSource<Result<IProviderAsset<T>>>();
            internal Result<IProviderAsset<T>> Outcome;
            internal IProviderAsset<T> Handle;
            internal bool Done,Releasing;
            private bool started,loadingStarted;
            private readonly List<Request<T>> demands=new List<Request<T>>();
            private ResourcePriority priority;
            private IScheduledWork queued;
            private IScheduledOperation<Unit> download;
            internal bool HasDemand {get{foreach(var request in demands)if(request.WantsWork)return true;return false;}}
            internal void Add(Request<T> request){demands.Add(request);Interests++;Promote(request.Priority);}
            internal void Promote(ResourcePriority requested){if(requested>priority)priority=requested;queued?.Promote(priority);}
            public bool Reclaiming=>Releasing;
            public int Interests {get;set;}
            public bool Loading=>loadingStarted&&!Done;
            public bool Downloading=>download!=null&&download.SchedulingState==ResourceWorkState.Running;
            public bool Pending=>!Done;
            public bool ReleaseFailed {get;private set;}
            private UniTaskCompletionSource<Result> release;
            private UniTask loadDriver,releaseDriver;
            internal Entry(AssetsService service,ResourceKey key){this.service=service;Key=key;}
            internal void Start(){if(started)return;started=true;loadDriver=LoadActual().Preserve();}
            private async UniTask LoadActual()
            {
                Result<IProviderAsset<T>> result;
                try
                {
                    if(service.scheduler==null)result=await DirectLoad();
                    else
                    {
                        Result downloadResult=Result.Success();
                        if(service.provider is IAssetDownloadProvider downloads)
                        {
                            var scheduled=service.scheduler.Schedule(ResourceLane.Download,()=>downloads.BeginDownload(Key),priority,()=>HasDemand);
                            if(!scheduled.IsSuccess)downloadResult=Result.Failure(scheduled.Error);
                            else {download=scheduled.Value;queued=download;var actual=await download.Completion;downloadResult=actual.IsSuccess?Result.Success():Result.Failure(actual.Error);}
                        }
                        if(!downloadResult.IsSuccess)result=Result<IProviderAsset<T>>.Failure(downloadResult.Error);
                        else if(!HasDemand)result=Reject<IProviderAsset<T>>("assets.queue.no_demand",ErrorKind.Canceled);
                        else
                        {
                            var scheduled=service.scheduler.Schedule(ResourceLane.Load,BeginActualLoad,priority,()=>HasDemand);
                            if(!scheduled.IsSuccess)result=Result<IProviderAsset<T>>.Failure(scheduled.Error);
                            else {queued=scheduled.Value;result=await scheduled.Value.Completion;}
                        }
                    }
                }
                catch(Exception){result=Reject<IProviderAsset<T>>("assets.provider.load.exception",ErrorKind.Provider);}
                await UniTask.SwitchToMainThread();
                if(result.IsSuccess)
                {
                    Handle=result.Value;
                    try
                    {
                        if(Handle==null||!Handle.Key.Equals(Key)||Handle.State!=AssetLeaseState.Active||Handle.Value==null)
                            result=Reject<IProviderAsset<T>>("assets.provider.payload",ErrorKind.Integrity);
                    }
                    catch(Exception){result=Reject<IProviderAsset<T>>("assets.provider.payload.exception",ErrorKind.Integrity);}
                }
                queued=null;download=null;Outcome=result;Done=true;
                // Actual ownership is set before any continuations run.
                Loaded.TrySetResult(result);
                if(Interests==0)EnsureRelease(service.reclaimPolicy,false);
            }
            private Result<IProviderOperation<IProviderAsset<T>>> BeginActualLoad()
            {
                loadingStarted=true;
                return service.scheduler!=null&&service.provider is IAssetDownloadProvider prepared?
                    prepared.BeginLoadPrepared<T>(Key):service.provider.BeginLoad<T>(Key);
            }
            private async UniTask<Result<IProviderAsset<T>>> DirectLoad()
            {
                var begun=BeginActualLoad();
                return begun.IsSuccess&&begun.Value!=null?await begun.Value.Completion:
                    Result<IProviderAsset<T>>.Failure(begun.IsSuccess?new FrameworkError(ErrorKind.Integrity,"assets.provider.operation.null"):begun.Error);
            }
            internal void Leave(CleanupContext cleanup,Request<T> request)
            {
                if(Interests<=0)throw new InvalidOperationException("Asset interest underflow.");
                demands.Remove(request);Interests--;if(Interests==0)queued?.CancelQueued();if(Interests==0&&Done)EnsureRelease(cleanup,false);
            }
            private void EnsureRelease(CleanupContext cleanup,bool retry)
            {
                if(!Done||Interests!=0||Releasing)return;
                if(Handle==null){service.Remove(Key,this);return;}
                if(ReleaseFailed&&!retry)return;
                Releasing=true;ReleaseFailed=false;release=new UniTaskCompletionSource<Result>();releaseDriver=ReleaseActual(cleanup,retry).Preserve();
            }
            private async UniTask ReleaseActual(CleanupContext cleanup,bool retry)
            {
                Result result;
                try
                {
                    var started=retry?Handle.RetryRelease(cleanup):Handle.BeginRelease(cleanup);
                    if(!started.IsSuccess)result=Result.Failure(started.Error);
                    else if(started.Value==null)result=Error("assets.provider.release.null",ErrorKind.Integrity);
                    else {var actual=await started.Value.Completion;result=actual.IsSuccess?Result.Success():Result.Failure(actual.Error);}
                }
                catch(Exception){result=Error("assets.provider.release.exception",ErrorKind.CleanupFailed);}
                await UniTask.SwitchToMainThread();
                Releasing=false;ReleaseFailed=!result.IsSuccess;
                if(result.IsSuccess){Handle=null;service.Remove(Key,this);}
                release.TrySetResult(result);
            }
            internal async UniTask<Result> Drain(CleanupContext cleanup,bool retry)
            {
                await Loaded.Task;
                if(Interests!=0)return Result.Success();
                EnsureRelease(cleanup,retry);
                if(Handle==null)return Result.Success();
                return await release.Task;
            }
        }
        private sealed class Request<T>:IOwnedOperation<IAssetLease<T>>,IAsyncCleanup,IRequest,ILeaseDiagnostic,IPrioritizedOperation where T:class
        {
            private readonly AssetsService service;
            internal readonly IScope Owner;
            internal readonly ResourceKey Key;
            internal Entry<T> Entry;
            internal ResourcePriority Priority;
            private OperationContext workWait;
            private long workStart;
            internal bool WantsWork=>!cleaning&&Owner.State==ScopeState.Open&&!service.stopping&&!CleanupWait.Interrupted(workWait,workStart).HasValue;
            private bool waited,detached,cleaning,cleanupDone,cleanupAttemptedByRegistry;
            private UniTaskCompletionSource<Result> cleaned;
            private UniTask cleanupDriver;
            private Result cleanupResult;
            private BackendState backendFinal=BackendState.Running;
            internal Lease<T> Lease;
            public string Id {get;}
            public ICleanupTicket Cleanup {get;internal set;}
            public BackendState BackendState=>Entry==null?backendFinal:!Entry.Done?BackendState.Running:Entry.Outcome.IsSuccess?BackendState.Succeeded:BackendState.Failed;
            public ResourceOwnerSnapshot Snapshot=>new ResourceOwnerSnapshot(Key,Id,Owner.Id,Owner.State,Lease!=null&&Lease.State!=AssetLeaseState.Released,cleaning);
            public int ActiveLease=>Lease!=null&&Lease.State!=AssetLeaseState.Released?1:0;
            internal Request(AssetsService service,ResourceKey key,IScope owner,string id,ResourcePriority priority){this.service=service;Key=key;Owner=owner;Id=id;Priority=priority;}
            public bool Promote(ResourcePriority priority)
            {
                CleanupWait.MainThread();if(cleaning||Entry!=null&&Entry.Done||priority<ResourcePriority.Prefetch||priority>ResourcePriority.Critical)return false;
                if(priority>Priority)Priority=priority;Entry?.Promote(Priority);return true;
            }
            public async UniTask<Result<IAssetLease<T>>> WaitAsync(OperationContext wait)
            {
                CleanupWait.MainThread();if(waited)return Reject<IAssetLease<T>>("assets.wait.once",ErrorKind.InvalidState);waited=true;
                var start=CleanupWait.Now;workStart=start;workWait=wait;
                while(true)
                {
                    Result? interrupted=null;
                    if(cleaning||Owner.State!=ScopeState.Open||service.stopping)interrupted=Error("assets.owner.closed",ErrorKind.Canceled);
                    else interrupted=CleanupWait.Interrupted(wait,start);
                    if(interrupted.HasValue){Clean(service.reclaimPolicy,false);return Result<IAssetLease<T>>.Failure(interrupted.Value.Error);}
                    if(Entry==null)
                    {
                        var joined=service.Join(this);
                        if(!joined.IsSuccess){Clean(service.reclaimPolicy,false);return Result<IAssetLease<T>>.Failure(joined.Error);}
                        Entry=joined.Value;Entry.Start();continue; // Recheck cancellation/deadline after a synchronous Provider callback.
                    }
                    if(Entry.Done)
                    {
                        if(!Entry.Outcome.IsSuccess){var error=Entry.Outcome.Error;Clean(service.reclaimPolicy,false);return Result<IAssetLease<T>>.Failure(error);}
                        Lease=new Lease<T>(this);return Result<IAssetLease<T>>.Success(Lease);
                    }
                    await UniTask.Yield();
                }
            }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {var retry=cleanupAttemptedByRegistry;cleanupAttemptedByRegistry=true;return Clean(cleanup,retry);}
            public UniTask<Result> Clean(CleanupContext cleanup,bool retry)
            {
                CleanupWait.MainThread();
                if(cleaned!=null&&(!cleanupDone||cleanupResult.IsSuccess||!retry))return cleaned.Task;
                cleaning=true;cleanupDone=false;
                var completion=new UniTaskCompletionSource<Result>();cleaned=completion;
                // Capture this attempt before synchronous continuations can start a retry.
                var actual=completion.Task;cleanupDriver=CleanActual(cleanup,retry,completion).Preserve();return actual;
            }
            private async UniTask CleanActual(CleanupContext cleanup,bool retry,UniTaskCompletionSource<Result> completion)
            {
                if(Lease!=null)Lease.SetState(AssetLeaseState.Releasing);
                if(Entry!=null&&!detached){detached=true;Entry.Leave(cleanup,this);}
                var result=Entry==null?Result.Success():await Entry.Drain(cleanup,retry);
                if(Lease!=null)Lease.SetState(result.IsSuccess?AssetLeaseState.Released:AssetLeaseState.ReleaseFailed);
                cleanupResult=result;cleanupDone=true;
                if(result.IsSuccess){backendFinal=BackendState;Entry=null;service.requests.Remove(this);}
                completion.TrySetResult(result);
            }
            internal async UniTask<Result> ObserveCleanup(CleanupContext cleanup,bool retry)
            {
                var actual=Clean(cleanup,retry);var start=CleanupWait.Now;var wait=CleanupWait.Operation(cleanup);
                while(true)
                {
                    var interrupted=CleanupWait.Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
                    if(actual.Status!=UniTaskStatus.Pending)return await actual;
                    await UniTask.Yield();
                }
            }
        }
        private sealed class Lease<T>:IAssetLease<T> where T:class
        {
            private readonly Request<T> request;
            public ResourceKey Key=>request.Key;
            public string Id=>request.Id+"/lease";
            public string OwnerId=>request.Owner.Id;
            public AssetLeaseState State {get;private set;}=AssetLeaseState.Active;
            public T Value {get{CleanupWait.MainThread();if(State!=AssetLeaseState.Active||request.Owner.State!=ScopeState.Open)throw new InvalidOperationException("Lease is not active.");return request.Entry.Handle.Value;}}
            internal Lease(Request<T> request){this.request=request;}
            internal void SetState(AssetLeaseState state){State=state;}
            public UniTask<Result> ReleaseAsync(CleanupContext cleanup)
            {
                CleanupWait.MainThread();if(State==AssetLeaseState.Released)return UniTask.FromResult(Error("assets.lease.double_return"));
                return request.ObserveCleanup(cleanup,false);
            }
            public UniTask<Result> RetryReleaseAsync(CleanupContext cleanup)
            {
                CleanupWait.MainThread();if(State!=AssetLeaseState.ReleaseFailed)return UniTask.FromResult(Error("assets.lease.retry.state"));
                return request.ObserveCleanup(cleanup,true);
            }
        }
    }
}
