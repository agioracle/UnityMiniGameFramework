using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Storage
{
    // One address per module instance. No filesystem/thread-pool assumption and no implicit write retry.
    public sealed class StorageService:IStorageRecoveryService,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly StorageRecovery recovery;readonly IStorageProvider provider;readonly StorageCapabilities capabilities;readonly StorageAddress address;readonly StorageLimits limits;
        readonly IReadOnlyList<StorageModuleRegistration> modules;readonly Dictionary<string,StorageRecord> baseline=new Dictionary<string,StorageRecord>(StringComparer.Ordinal);
        readonly List<Commit> operations=new List<Commit>();readonly Queue<Commit> queue=new Queue<Commit>();
        IStorageSession session;UniTaskCompletionSource<Result> loaded;UniTask loadDriver,writeDriver;
        OperationContext loadPolicy;long loadStarted;
        bool loadAbandoned,initialized,stopped,capturing,driving,uncertain,cleanupFailed;byte[] lastFailedBytes,readFailureBytes;
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public StorageState State {get;private set;}public long CommittedSequence {get;private set;}public string CommittedDigest {get;private set;}="";
        public IStorageReadSnapshot Restored {get;private set;}
        public int PendingCount=>operations.Count;public StorageCommitReceipt LastReceipt {get;private set;}
        public byte[] CopyFailedCommit()=>lastFailedBytes==null?null:(byte[])lastFailedBytes.Clone();
        public byte[] CopyRejectedRead()=>readFailureBytes==null?null:(byte[])readFailureBytes.Clone();
        public StorageService(IStorageProvider provider,StorageAddress address,StorageRegistry registry,StorageLimits limits=null,string id="storage",string providerModuleId=null,StorageRecovery recovery=null)
        {
            Main();this.recovery=recovery;this.provider=provider??throw new ArgumentNullException(nameof(provider));capabilities=provider.Capabilities??throw new ArgumentException("Provider capabilities required.");
            if(!address.IsValid||registry==null||string.IsNullOrWhiteSpace(id)||providerModuleId!=null&&string.IsNullOrWhiteSpace(providerModuleId))throw new ArgumentException("Storage composition invalid.");
            this.address=address;this.limits=limits??new StorageLimits();modules=registry.Freeze();Id=id;Dependencies=providerModuleId==null?Array.Empty<string>():new[]{providerModuleId};
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Storage coordinator requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,code);
        static FrameworkError? Interrupt(OperationContext wait,long start)
        {
            if(wait.Cancellation.IsCancellationRequested)return Error("storage.wait.canceled",ErrorKind.Canceled);
            if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Error("storage.wait.timeout",ErrorKind.Timeout);return null;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var start=Stopwatch.GetTimestamp();var interrupted=Interrupt(operation,start);if(interrupted.HasValue)return UniTask.FromResult(Result.Failure(interrupted.Value));
            if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("storage.initialize.state",ErrorKind.InvalidState)));
            initialized=true;var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            loadPolicy=operation;loadStarted=start;loaded=new UniTaskCompletionSource<Result>();State=StorageState.Loading;loadDriver=Load().Preserve();return ObserveLoad(operation,start);
        }
        async UniTask Load()
        {
            Result result;
            try
            {
                if(!capabilities.SupportsCompareExchange)throw new StorageFailure(Error("storage.provider.conditional_required",ErrorKind.NotSupported));
                if(capabilities.MaxContainerBytes<limits.MaxContainerBytes||modules.Count>limits.MaxModules)throw new StorageFailure(Error("storage.capacity",ErrorKind.InvalidArgument));
                session=provider.Create(address);if(session==null)throw new StorageFailure(Error("storage.provider.null",ErrorKind.Provider));
                var read=await session.ReadAsync();await UniTask.SwitchToMainThread();
                if(!read.IsSuccess)throw new StorageFailure(read.Error);if(read.Value==null)throw new StorageFailure(Error("storage.read.null",ErrorKind.Provider));
                if(read.Value.Exists)
                {
                    readFailureBytes=read.Value.CopyBytes();var decoded=StorageFormat.Decode(readFailureBytes,limits);if(!decoded.IsSuccess)throw new StorageFailure(decoded.Error);
                    var value=decoded.Value;if(value.Address.Namespace!=address.Namespace||value.Address.Slot!=address.Slot)throw new StorageFailure(Error("storage.address.mismatch",ErrorKind.Integrity));
                    if(recovery!=null)
                    {
                        var prepared=recovery.Prepare(value,modules,limits);if(!prepared.IsSuccess)throw new StorageFailure(prepared.Error);
                        var changed=value.Records.Any(r=>prepared.Value.Records.Any(n=>n.ModuleId==r.ModuleId&&(n.SchemaVersion!=r.SchemaVersion||n.CodecId!=r.CodecId)));
                        if(changed)
                        {
                            if(!MayPublish())throw new StorageFailure(Interrupt(loadPolicy,loadStarted)??Error("storage.initialize.abandoned",ErrorKind.Canceled));
                            if(value.Sequence==long.MaxValue)throw new StorageFailure(Error("storage.sequence.exhausted",ErrorKind.Integrity));
                            var candidate=new StorageContainer(address,value.Sequence+1,prepared.Value.Records);
                            var encoded=StorageFormat.Encode(candidate,limits);if(!encoded.IsSuccess)throw new StorageFailure(encoded.Error);
                            var request=new StorageWriteRequest(address,value.Sequence,StorageFormat.Digest(readFailureBytes),candidate.Sequence,StorageFormat.Digest(encoded.Value),encoded.Value);
                            LastReceipt=await Submit(request);
                            if(LastReceipt.Disposition!=StorageCommitDisposition.Committed)
                            {
                                lastFailedBytes=request.CopyBytes();uncertain=LastReceipt.Disposition==StorageCommitDisposition.Indeterminate;
                                throw new StorageFailure(LastReceipt.Error??Error("storage.receipt.invalid",ErrorKind.Integrity));
                            }
                            value=candidate;readFailureBytes=encoded.Value;
                        }
                        else value=prepared.Value;
                    }
                    else
                    {
                        foreach(var record in value.Records)
                        {
                            var module=modules.FirstOrDefault(m=>m.ModuleId==record.ModuleId);
                            if(module==null){if(record.Required)throw new StorageFailure(Error("storage.module.unknown_required",ErrorKind.NotSupported));}
                            else
                            {
                                if(module.SchemaVersion!=record.SchemaVersion||module.CodecId!=record.CodecId||module.Required!=record.Required)throw new StorageFailure(Error("storage.module.version",ErrorKind.NotSupported));
                                var valid=module.ValidateSnapshot(record.CopyPayload());if(!valid.IsSuccess)throw new StorageFailure(valid.Error);
                            }
                        }
                        foreach(var module in modules)if(module.Required&&!value.Records.Any(r=>r.ModuleId==module.ModuleId))throw new StorageFailure(Error("storage.module.missing_required",ErrorKind.Integrity));
                    }
                    if(!MayPublish())throw new StorageFailure(Interrupt(loadPolicy,loadStarted)??Error("storage.initialize.abandoned",ErrorKind.Canceled));
                    foreach(var record in value.Records)baseline.Add(record.ModuleId,record);
                    CommittedSequence=value.Sequence;CommittedDigest=StorageFormat.Digest(readFailureBytes);readFailureBytes=null;
                    if(recovery!=null)Restored=new StorageReadSnapshot(value.Sequence,value.Records);
                }
                else if(recovery!=null&&MayPublish())Restored=new StorageReadSnapshot(0,Array.Empty<StorageRecord>());
                result=!MayPublish()?Result.Failure(Interrupt(loadPolicy,loadStarted)??Error("storage.initialize.stopped",ErrorKind.Canceled)):Result.Success();
            }
            catch(StorageFailure e){result=Result.Failure(e.Error);}
            catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("storage.read.exception",ErrorKind.Provider));}
            State=stopped?StorageState.Closed:uncertain?StorageState.RecoveryRequired:result.IsSuccess?StorageState.Ready:StorageState.Failed;loaded.TrySetResult(result);
        }
        bool MayPublish(){if(Interrupt(loadPolicy,loadStarted).HasValue)loadAbandoned=true;return !stopped&&!loadAbandoned;}
        async UniTask<Result> ObserveLoad(OperationContext wait,long start)
        {
            while(true){var error=Interrupt(wait,start);if(error.HasValue){loadAbandoned=true;Restored=null;return Result.Failure(error.Value);}if(loaded.Task.Status!=UniTaskStatus.Pending)return await loaded.Task;await UniTask.Yield();}
        }
        public Result<IStorageCommit> Capture()
        {
            Main();if(stopped||State!=StorageState.Ready)return Reject("storage.not_ready",ErrorKind.NotReady);
            if(capturing)return Reject("storage.snapshot.reentrant",ErrorKind.InvalidState);
            if(operations.Count>=limits.MaxOperations)return Reject("storage.queue.capacity",ErrorKind.Unavailable);
            if(CommittedSequence==long.MaxValue)return Reject("storage.sequence.exhausted",ErrorKind.Integrity);
            capturing=true;
            try
            {
                var records=baseline.Where(p=>!modules.Any(m=>m.ModuleId==p.Key)).Select(p=>p.Value).ToList();
                foreach(var module in modules)
                {
                    var captured=module.CaptureSnapshot();if(!captured.IsSuccess)return Result<IStorageCommit>.Failure(captured.Error);
                    if(captured.Value==null||captured.Value.Length>limits.MaxPayloadBytes)return Reject("storage.snapshot.payload",ErrorKind.Integrity);
                    // Clone before validation; a consumer validator never gets the buffer retained by this transaction.
                    var bytes=(byte[])captured.Value.Clone();var validated=module.ValidateSnapshot((byte[])bytes.Clone());if(!validated.IsSuccess)return Result<IStorageCommit>.Failure(validated.Error);
                    records.Add(new StorageRecord(module.ModuleId,module.SchemaVersion,module.CodecId,module.Required,bytes));
                }
                if(stopped)return Reject("storage.snapshot.stopped",ErrorKind.Canceled);
                // Bound the complete snapshot before accepting it. Final sequence is assigned only at write start.
                var bounded=StorageFormat.Encode(new StorageContainer(address,1,records),limits);if(!bounded.IsSuccess)return Result<IStorageCommit>.Failure(bounded.Error);
                var operation=new Commit(this,records);operations.Add(operation);return Result<IStorageCommit>.Success(operation);
            }
            catch(Exception){return Reject("storage.snapshot.exception",ErrorKind.Unexpected);}
            finally{capturing=false;}
        }
        static Result<IStorageCommit> Reject(string code,ErrorKind kind)=>Result<IStorageCommit>.Failure(Error(code,kind));
        void Enqueue(Commit operation)
        {
            operation.State=StorageCommitState.Queued;queue.Enqueue(operation);
            if(!driving){driving=true;writeDriver=WriteQueue().Preserve();}
        }
        async UniTask WriteQueue()
        {
            try
            {
                while(queue.Count>0)
                {
                    var operation=queue.Dequeue();
                    if(uncertain){Complete(operation,StorageCommitReceipt.Rejected(Error("storage.recovery_required")));continue;}
                    if(CommittedSequence==long.MaxValue){Complete(operation,StorageCommitReceipt.Rejected(Error("storage.sequence.exhausted",ErrorKind.Integrity)));continue;}
                    var encoded=StorageFormat.Encode(new StorageContainer(address,CommittedSequence+1,operation.Records),limits);
                    if(!encoded.IsSuccess){Complete(operation,StorageCommitReceipt.Rejected(encoded.Error));continue;}
                    var request=new StorageWriteRequest(address,CommittedSequence,CommittedDigest,CommittedSequence+1,StorageFormat.Digest(encoded.Value),encoded.Value);
                    operation.State=StorageCommitState.Writing;var receipt=await Submit(request);
                    if(receipt.Disposition==StorageCommitDisposition.Committed)
                    {CommittedSequence=request.Sequence;CommittedDigest=request.Digest;baseline.Clear();foreach(var record in operation.Records)baseline.Add(record.ModuleId,record);}
                    else lastFailedBytes=request.CopyBytes();
                    if(receipt.Disposition==StorageCommitDisposition.Indeterminate||receipt.Disposition==StorageCommitDisposition.Invalid)
                    {uncertain=true;if(!stopped)State=StorageState.RecoveryRequired;}
                    Complete(operation,receipt);
                }
            }
            finally{driving=false;}
        }
        async UniTask<StorageCommitReceipt> Submit(StorageWriteRequest request)
        {
            try
            {
                var receipt=await session.CommitAsync(request);await UniTask.SwitchToMainThread();
                if(receipt==null||receipt.Disposition==StorageCommitDisposition.Committed&&(receipt.Sequence!=request.Sequence||receipt.Digest!=request.Digest||receipt.Persistence!=capabilities.Persistence))return StorageCommitReceipt.Indeterminate(Error("storage.receipt.invalid",ErrorKind.Integrity));
                return receipt;
            }
            catch(Exception){await UniTask.SwitchToMainThread();return StorageCommitReceipt.Indeterminate(Error("storage.commit.exception"));}
        }
        void Complete(Commit operation,StorageCommitReceipt receipt)
        {
            LastReceipt=receipt;operation.Receipt=receipt;operation.State=receipt.Disposition==StorageCommitDisposition.Committed?StorageCommitState.Committed:receipt.Disposition==StorageCommitDisposition.Indeterminate?StorageCommitState.Indeterminate:StorageCommitState.Rejected;
            operations.Remove(operation);operation.Done.TrySetResult(receipt.Disposition==StorageCommitDisposition.Committed?Result<StorageCommitReceipt>.Success(receipt):Result<StorageCommitReceipt>.Failure(receipt.Error??Error("storage.receipt.invalid",ErrorKind.Integrity)));
        }
        sealed class Commit:IStorageCommit
        {
            readonly StorageService service;internal readonly IReadOnlyList<StorageRecord> Records;internal readonly UniTaskCompletionSource<Result<StorageCommitReceipt>> Done=new UniTaskCompletionSource<Result<StorageCommitReceipt>>();bool waited;
            public StorageCommitResult CommitResult=>new StorageCommitResult(Receipt);
            public string Id {get;}=Guid.NewGuid().ToString("N");public StorageCommitState State {get;internal set;}=StorageCommitState.Captured;public StorageCommitReceipt Receipt {get;internal set;}
            internal Commit(StorageService service,List<StorageRecord> records){this.service=service;Records=records.AsReadOnly();}
            internal void CancelCold(FrameworkError error){State=StorageCommitState.Canceled;service.operations.Remove(this);Done.TrySetResult(Result<StorageCommitReceipt>.Failure(error));}
            public async UniTask<Result<StorageCommitReceipt>> WaitAsync(OperationContext wait)
            {
                Main();if(waited)return Result<StorageCommitReceipt>.Failure(Error("storage.wait.single_use",ErrorKind.InvalidState));waited=true;var start=Stopwatch.GetTimestamp();var error=Interrupt(wait,start);
                if(State==StorageCommitState.Captured)
                {
                    if(error.HasValue||service.stopped){CancelCold(error??Error("storage.stopped",ErrorKind.Canceled));return await Done.Task;}
                    service.Enqueue(this);
                }
                while(true){error=Interrupt(wait,start);if(error.HasValue)return Result<StorageCommitReceipt>.Failure(error.Value);if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {Main();stopped=true;Restored=null;State=StorageState.Closed;foreach(var operation in operations.ToArray())if(operation.State==StorageCommitState.Captured)operation.CancelCold(Error("storage.stopped",ErrorKind.Canceled));return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main();await StopAsync(cleanup);if(loaded!=null)await loaded.Task;if(driving)await writeDriver;
            if(session!=null)
            {
                Result result;try{result=await session.CleanupAsync(cleanup);await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("storage.cleanup.exception",ErrorKind.CleanupFailed));}
                cleanupFailed=!result.IsSuccess;if(!result.IsSuccess)return result;session=null;
            }
            lastFailedBytes=readFailureBytes=null;baseline.Clear();return Result.Success();
        }
        public DiagnosticCounters ReadDiagnostics(){Main();return new DiagnosticCounters{PendingOperations=operations.Count+(State==StorageState.Loading?1:0),FailedCleanup=cleanupFailed?1:0};}
        sealed class StorageFailure:Exception{internal readonly FrameworkError Error;internal StorageFailure(FrameworkError error){Error=error;}}
    }
}
