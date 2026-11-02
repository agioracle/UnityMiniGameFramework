using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Ownership;
namespace Game.Revive
{
    public sealed class ReviveRepository:IModule,IDiagnosticSource,IRevivePersistenceView
    {
        readonly ClaimOwnerMode mode;readonly string ownerId;readonly StorageAddress address;readonly StorageService storage;ReviveSnapshot snapshot,staged;bool ready,stopped,writing;int writers;
        public string Id=>"revive.store";public IReadOnlyList<string> Dependencies {get;}=Array.Empty<string>();
        public ReviveSnapshot Snapshot=>snapshot;public long Sequence=>storage.CommittedSequence;public StoragePersistence Persistence {get;}public bool RecoveryRequired=>storage.State==StorageState.RecoveryRequired;public StorageCommitReceipt LastReceipt=>storage.LastReceipt;
        public ReviveRepository(IStorageProvider provider,ClaimOwnerMode mode,string ownerId,StorageAddress address,StorageLimits limits=null)
        {
            if(provider==null||!Enum.IsDefined(typeof(ClaimOwnerMode),mode)||!ClaimOwnerSnapshot.IsToken(ownerId)||!address.IsValid)throw new ArgumentException();this.mode=mode;this.ownerId=ownerId;this.address=address;Persistence=provider.Capabilities.Persistence;snapshot=new ReviveSnapshot(mode,ownerId,address.Namespace);
            var registry=new StorageRegistry();registry.Register(new StorageModuleRegistration(ReviveCodec.ModuleId,ReviveCodec.Schema,ReviveCodec.CodecId,true,typeof(ReviveSnapshot),()=>ReviveCodec.Encode(staged??snapshot),bytes=>{var d=ReviveCodec.Decode(bytes);return d.IsSuccess?Result.Success():Result.Failure(d.Error);}));
            storage=new StorageService(provider,address,registry,limits,id:"revive.storage.io",recovery:new StorageRecovery());
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Revive repository requires main thread.");}
        static Result Fail(string code,ErrorKind kind=ErrorKind.Storage)=>Result.Failure(new FrameworkError(kind,code));
        public async UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();if(ready||stopped)return Fail("revive.store.state",ErrorKind.InvalidState);var result=await storage.InitializeAsync(context,operation);if(!result.IsSuccess)return result;
            if(!storage.Restored.IsNew)
            {
                var read=storage.Restored.Read(ReviveCodec.ModuleId);if(!read.IsSuccess)return Result.Failure(read.Error);var decoded=ReviveCodec.Decode(read.Value);if(!decoded.IsSuccess)return Result.Failure(decoded.Error);
                if(decoded.Value.Mode!=mode||decoded.Value.OwnerId!=ownerId||decoded.Value.Namespace!=address.Namespace||decoded.Value.Claims.Any(c=>c.GrantSequence>storage.CommittedSequence))return Fail("revive.store.owner_mismatch",ErrorKind.Integrity);snapshot=decoded.Value;
            }
            ready=true;return Result.Success();
        }
        bool Legal(ReviveSnapshot next)
        {
            if(next==null||next.Mode!=mode||next.OwnerId!=ownerId||next.Namespace!=address.Namespace||next.LastGeneration<snapshot.LastGeneration||next.Coins<snapshot.Coins)return false;
            foreach(var prior in snapshot.Claims)
            {
                var c=next.Claims.FirstOrDefault(x=>x.ClaimId==prior.ClaimId);
                if(c==null||c.AttemptId!=prior.AttemptId||c.RunId!=prior.RunId||!c.Owner.SameAs(prior.Owner)||c.Level!=prior.Level||c.RewardCoins!=prior.RewardCoins||c.AdMode!=prior.AdMode)return false;
                if(prior.State!=ReviveClaimState.Pending&&(prior.State!=c.State||prior.GrantSequence!=c.GrantSequence))return false;
            }
            return next.Claims.Where(c=>!snapshot.Claims.Any(p=>p.ClaimId==c.ClaimId)).All(c=>c.State==ReviveClaimState.Pending);
        }
        // Only work accepted before Stop holds this internal permit. No fresh business write can enter after Stop.
        internal sealed class Writer:IDisposable
        {
            internal ReviveRepository Owner;internal Writer(ReviveRepository owner){Owner=owner;}
            public void Dispose(){if(Owner==null)return;Main();Owner.writers--;Owner=null;}
        }
        internal Result<Writer> CaptureWriter()
        {
            Main();if(!ready||stopped||RecoveryRequired||writers!=0)return Result<Writer>.Failure(new FrameworkError(ErrorKind.NotReady,"revive.store.writer.closed"));
            writers++;return Result<Writer>.Success(new Writer(this));
        }
        // Actual completion only. Caller cancellation is implemented by the owning business observer.
        internal async UniTask<Result<StorageCommitResult>> SaveActualAsync(ReviveSnapshot next,Writer writer=null)
        {
            Main();if(!ready||stopped&&(writer==null||writer.Owner!=this)||writing||RecoveryRequired)return Result<StorageCommitResult>.Failure(new FrameworkError(ErrorKind.NotReady,"revive.store.not_ready"));
            if(!Legal(next))return Result<StorageCommitResult>.Failure(new FrameworkError(ErrorKind.Integrity,"revive.store.transition"));writing=true;
            try
            {
                staged=next;var capture=storage.Capture();staged=null;if(!capture.IsSuccess)return Result<StorageCommitResult>.Failure(capture.Error);
                var result=await capture.Value.WaitAsync(OperationContext.Unlimited());if(result.IsSuccess){snapshot=next;return Result<StorageCommitResult>.Success(capture.Value.CommitResult);}return Result<StorageCommitResult>.Failure(result.Error);
            }
            finally{staged=null;writing=false;}
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup){Main();stopped=true;return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)
        {
            Main();if(writers!=0)return UniTask.FromResult(Fail("revive.store.writer.retained",ErrorKind.CleanupFailed));ready=false;return storage.StopAsync(cleanup);
        }
        public DiagnosticCounters ReadDiagnostics()=>storage.ReadDiagnostics();
    }
    // Durable reserve happens before LocalOwnerService publication, with Storage retained as dependency.
    public sealed class DurableLocalOwnerModule:IModule,IClaimOwnerAuthority
    {
        readonly ReviveRepository store;readonly OwnershipPolicy policy;LocalOwnerService inner;bool stopped;
        public string Id=>"owner";public IReadOnlyList<string> Dependencies {get;}
        public ClaimOwnerMode Mode=>ClaimOwnerMode.LocalOnly;public ClaimOwnerSnapshot? CurrentOwner=>stopped?null:inner?.CurrentOwner;
        public DurableLocalOwnerModule(ReviveRepository store,OwnershipPolicy policy){this.store=store??throw new ArgumentNullException(nameof(store));if(policy==null||policy.Mode!=ClaimOwnerMode.LocalOnly)throw new ArgumentException();this.policy=policy;Dependencies=new[]{store.Id};}
        public async UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            var started=Stopwatch.GetTimestamp();
            if(stopped||inner!=null)return Failure("revive.owner.state",ErrorKind.InvalidState);
            if(operation.Cancellation.IsCancellationRequested)return Failure("revive.owner.canceled",ErrorKind.Canceled);if(operation.Timeout==TimeSpan.Zero)return Failure("revive.owner.timeout",ErrorKind.Timeout);
            var old=store.Snapshot;if(old.LastGeneration==long.MaxValue)return Failure("revive.owner.exhausted",ErrorKind.Integrity);
            if(old.OwnerId!=policy.LocalOwnerId||old.Namespace!=policy.LocalNamespace||old.Mode!=ClaimOwnerMode.LocalOnly)return Failure("revive.owner.binding",ErrorKind.Integrity);
            var committed=await store.SaveActualAsync(old.Copy(generation:old.LastGeneration+1));if(!committed.IsSuccess)return Result.Failure(committed.Error);
            if(stopped||operation.Cancellation.IsCancellationRequested)return Failure("revive.owner.canceled",ErrorKind.Canceled);
            if(operation.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-started)/(double)Stopwatch.Frequency>=operation.Timeout.TotalSeconds)return Failure("revive.owner.timeout",ErrorKind.Timeout);
            inner=new LocalOwnerService(policy,new OwnerGenerationSequence(old.LastGeneration),Id);return await inner.InitializeAsync(context,operation);
        }
        static Result Failure(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
        public Result ValidateCurrent(ClaimOwnerSnapshot owner)=>!stopped&&inner!=null?inner.ValidateCurrent(owner):Failure("revive.owner.not_ready",ErrorKind.NotReady);
        public Result<StorageAddress> AddressForCurrent(string slot)=>!stopped&&inner!=null?inner.AddressForCurrent(slot):Result<StorageAddress>.Failure(new FrameworkError(ErrorKind.NotReady,"revive.owner.not_ready"));
        public UniTask<Result> StopAsync(CleanupContext cleanup){stopped=true;return inner==null?UniTask.FromResult(Result.Success()):inner.StopAsync(cleanup);}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
    }
}
