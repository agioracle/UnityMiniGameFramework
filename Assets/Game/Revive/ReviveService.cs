using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.Ads;
namespace Game.Revive
{
    public sealed class ReviveService:IReviveService,IAdReceiptJournal,IModule,IAsyncCleanup,IDiagnosticSource
    {
        // Bounded knowledge survives journal backpressure; Ads retains producer ownership until Record succeeds.
        readonly Dictionary<string,AdPresentationReceipt> knownFacts=new Dictionary<string,AdPresentationReceipt>(StringComparer.Ordinal);
        readonly ReviveRepository store;readonly AdReceiptJournal facts;readonly PlatformExecutionMode allowedMode;readonly List<Watch> captured=new List<Watch>();
        IAdsService ads;IClaimOwnerAuthority authority;bool ready,stopped;Watch active;Mutation mutation;
        public string Id=>"revive";public IReadOnlyList<string> Dependencies {get;}public ReviveSnapshot Snapshot=>store.Snapshot;public bool Busy=>active!=null||mutation!=null;
        public bool RecoveryRequired=>store.RecoveryRequired||Snapshot.Pending!=null;
        public bool CanRevive=>Gate()==null&&!Busy&&Snapshot.Phase==ReviveRunPhase.Failed&&!Snapshot.ReviveUsed&&Snapshot.Pending==null&&Snapshot.Claims.Count<ReviveSnapshot.MaxClaims&&Snapshot.Coins<=ReviveSnapshot.MaxCoins-5;
        public ReviveService(ReviveRepository store,AdReceiptJournal facts,PlatformExecutionMode allowedMode=PlatformExecutionMode.Mock,string ownerId="owner",string adsId="ads")
        {this.store=store??throw new ArgumentNullException(nameof(store));this.facts=facts??throw new ArgumentNullException(nameof(facts));if(!Enum.IsDefined(typeof(PlatformExecutionMode),allowedMode))throw new ArgumentException();this.allowedMode=allowedMode;Dependencies=new[]{store.Id,ownerId,adsId};}
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Revive service requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.InvalidState)=>new FrameworkError(kind,code);
        static FrameworkError? Interrupted(OperationContext wait,long start)
        {if(wait.Cancellation.IsCancellationRequested)return Error("revive.wait.canceled",ErrorKind.Canceled);if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Error("revive.wait.timeout",ErrorKind.Timeout);return null;}
        FrameworkError? Gate()
        {
            if(!ready||stopped||authority==null||!authority.CurrentOwner.HasValue)return Error("revive.not_ready",ErrorKind.NotReady);
            if(!Snapshot.Owns(authority.CurrentOwner.Value))return Error("revive.owner.mismatch",ErrorKind.PermissionDenied);
            if(store.RecoveryRequired)return Error("revive.store.reopen_required",ErrorKind.Storage);return null;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var e=Interrupted(operation,Stopwatch.GetTimestamp());if(e.HasValue)return UniTask.FromResult(Result.Failure(e.Value));
            if(ready||stopped)return UniTask.FromResult(Result.Failure(Error("revive.initialize.state")));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            if(!context.Services.TryGet(out ads)||!context.Services.TryGet(out authority))return UniTask.FromResult(Result.Failure(Error("revive.dependencies",ErrorKind.Dependency)));
            ready=true;var gate=Gate();if(gate.HasValue){ready=false;return UniTask.FromResult(Result.Failure(gate.Value));}
            // Already committed terminal claims only acknowledge their facts; never replay a grant at startup.
            foreach(var receipt in facts.Pending)if(Snapshot.Owns(receipt.Owner))
            {var c=Snapshot.Claims.FirstOrDefault(x=>x.ClaimId==receipt.ClaimId);if(c!=null&&c.State!=ReviveClaimState.Pending&&Matches(c,receipt))facts.Acknowledge(receipt.AttemptId);}
            return UniTask.FromResult(Result.Success());
        }
        static bool Matches(ReviveClaim c,AdPresentationReceipt receipt)=>receipt!=null&&c.ClaimId==receipt.ClaimId&&c.AttemptId==receipt.AttemptId&&c.Owner.SameAs(receipt.Owner)&&c.AdMode==receipt.Mode&&receipt.PlacementId=="revive";
        public Result Record(AdPresentationReceipt receipt)
        {
            Main();if(receipt==null)return Result.Failure(Error("revive.fact.null",ErrorKind.InvalidArgument));
            var c=Snapshot.Claims.FirstOrDefault(x=>x.ClaimId==receipt.ClaimId);
            if(c==null||!Matches(c,receipt)||receipt.Mode!=allowedMode||receipt.IsCompletionObserved&&(!receipt.ShowInvoked||receipt.Error.HasValue)||c.State==ReviveClaimState.Aborted&&receipt.IsCompletionObserved)return Result.Failure(Error("revive.fact.binding",ErrorKind.Integrity));
            if(knownFacts.TryGetValue(receipt.AttemptId,out var known)&&!known.SameAs(receipt))return Result.Failure(Error("revive.fact.conflict",ErrorKind.Integrity));
            if(!knownFacts.ContainsKey(receipt.AttemptId)&&knownFacts.Count>=ReviveSnapshot.MaxClaims)return Result.Failure(Error("revive.fact.capacity",ErrorKind.Unavailable));
            knownFacts[receipt.AttemptId]=receipt;
            var result=facts.Record(receipt);if(result.IsSuccess&&c.State!=ReviveClaimState.Pending){facts.Acknowledge(receipt.AttemptId);knownFacts.Remove(receipt.AttemptId);}return result;
        }
        async UniTask<Result<ReviveResult>> Save(ReviveSnapshot value,string claim=null,bool granted=false,AdPresentationReceipt receipt=null)
        {
            var saved=await store.SaveActualAsync(value,active?.Writer??mutation?.Writer);return saved.IsSuccess?Result<ReviveResult>.Success(new ReviveResult(claim,granted,saved.Value,receipt)):Result<ReviveResult>.Failure(saved.Error);
        }
        async UniTask<Result<ReviveResult>> ApplyFact(ReviveClaim claim,AdPresentationReceipt receipt)
        {
            if(!Matches(claim,receipt)||claim.State!=ReviveClaimState.Pending||receipt.Mode!=allowedMode)return Result<ReviveResult>.Failure(Error("revive.fact.binding",ErrorKind.Integrity));
            var completed=receipt.IsCompletionObserved&&receipt.ShowInvoked&&!receipt.Error.HasValue;
            var current=Snapshot;
            if(current.RunId!=claim.RunId||current.Level!=claim.Level||current.ReviveUsed)return Result<ReviveResult>.Failure(Error("revive.run.binding",ErrorKind.Integrity));
            var terminal=claim.With(completed?ReviveClaimState.Granted:ReviveClaimState.Aborted,completed?store.Sequence+1:0);
            ReviveSnapshot candidate;
            try{candidate=current.Copy(coins:completed?checked(current.Coins+claim.RewardCoins):current.Coins,phase:completed?ReviveRunPhase.Playing:current.Phase,used:completed,claims:current.Claims.Select(c=>c.ClaimId==claim.ClaimId?terminal:c));}
            catch(Exception){return Result<ReviveResult>.Failure(Error("revive.grant.invalid",ErrorKind.Integrity));}
            var result=await Save(candidate,claim.ClaimId,completed,receipt);if(result.IsSuccess){facts.Acknowledge(receipt.AttemptId);knownFacts.Remove(receipt.AttemptId);}return result;
        }
        sealed class Mutation
        {
            internal readonly UniTaskCompletionSource<Result<ReviveResult>> Done=new UniTaskCompletionSource<Result<ReviveResult>>();internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();internal UniTask Driver;internal ClaimOwnerSnapshot Owner;internal ReviveRepository.Writer Writer;
        }
        async UniTask DriveMutation(Mutation op,Func<UniTask<Result<ReviveResult>>> action)
        {
            try{op.Done.TrySetResult(await action());}catch(Exception){op.Done.TrySetResult(Result<ReviveResult>.Failure(Error("revive.mutation.exception",ErrorKind.Unexpected)));}
            finally{op.Writer.Dispose();if(mutation==op)mutation=null;op.Ended.TrySetResult();}
        }
        async UniTask<Result<ReviveResult>> Mutate(Func<UniTask<Result<ReviveResult>>> action,OperationContext wait)
        {
            Main();var start=Stopwatch.GetTimestamp();var error=Interrupted(wait,start)??Gate();if(error.HasValue)return Result<ReviveResult>.Failure(error.Value);if(Busy)return Result<ReviveResult>.Failure(Error("revive.busy",ErrorKind.Unavailable));
            var permit=store.CaptureWriter();if(!permit.IsSuccess)return Result<ReviveResult>.Failure(permit.Error);var op=new Mutation{Owner=authority.CurrentOwner.Value,Writer=permit.Value};mutation=op;op.Driver=DriveMutation(op,action).Preserve();
            while(true){error=stopped?Error("revive.owner.closed",ErrorKind.Canceled):Interrupted(wait,start);if(!error.HasValue&&!authority.ValidateCurrent(op.Owner).IsSuccess)error=Error("revive.owner.changed",ErrorKind.Canceled);if(error.HasValue)return Result<ReviveResult>.Failure(error.Value);if(op.Done.Task.Status!=UniTaskStatus.Pending)return await op.Done.Task;await UniTask.Yield();}
        }
        static async UniTask<Result> Simple(UniTask<Result<ReviveResult>> work){var r=await work;return r.IsSuccess?Result.Success():Result.Failure(r.Error);}
        public UniTask<Result> BeginRunAsync(int level,OperationContext wait)=>Simple(Mutate(()=>
        {
            if(level<1||level>100000||Snapshot.Pending!=null)return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.begin.state")));
            return Save(Snapshot.Copy(runId:Guid.NewGuid().ToString("N"),level:level,phase:ReviveRunPhase.Playing,used:false,collected:false));
        },wait));
        public UniTask<Result> FailRunAsync(OperationContext wait)=>Simple(Mutate(()=>Snapshot.Phase!=ReviveRunPhase.Playing?UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.fail.state"))):Save(Snapshot.Copy(phase:ReviveRunPhase.Failed)),wait));
        public UniTask<Result> CollectAsync(int rewardCoins,OperationContext wait)=>Simple(Mutate(()=>
        {
            if(Snapshot.Phase!=ReviveRunPhase.Playing||Snapshot.Collected||Snapshot.Pending!=null||rewardCoins<1||rewardCoins>1000||Snapshot.Coins>ReviveSnapshot.MaxCoins-rewardCoins)return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.collect.state")));
            return Save(Snapshot.Copy(coins:Snapshot.Coins+rewardCoins,collected:true,highest:Math.Max(Snapshot.HighestCollectedLevel,Snapshot.Level)));
        },wait));
        public UniTask<Result<ReviveResult>> RecoverPendingAsync(OperationContext wait)=>Mutate(()=>
        {
            var c=Snapshot.Pending;if(c==null)return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.recovery.no_pending")));
            var fact=facts.Pending.FirstOrDefault(r=>r.AttemptId==c.AttemptId&&Matches(c,r));
            if(fact==null)knownFacts.TryGetValue(c.AttemptId,out fact);
            if(fact==null)return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.recovery.no_proof",ErrorKind.NotReady)));
            var retained=Record(fact);return retained.IsSuccess?ApplyFact(c,fact):UniTask.FromResult(Result<ReviveResult>.Failure(retained.Error));
        },wait);
        public UniTask<Result> AbandonPendingAsync(OperationContext wait)=>Simple(Mutate(()=>
        {
            var c=Snapshot.Pending;if(c==null)return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.recovery.no_pending")));
            // Never discard known completion proof through the no-proof recovery path.
            if(facts.Pending.Concat(knownFacts.Values).Any(r=>Matches(c,r)&&r.IsCompletionObserved))return UniTask.FromResult(Result<ReviveResult>.Failure(Error("revive.recovery.has_proof")));
            return Save(Snapshot.Copy(claims:Snapshot.Claims.Select(x=>x==c?c.With(ReviveClaimState.Aborted):x)),c.ClaimId);
        },wait));
        public Result<IReviveOperation> CaptureRevive()
        {
            Main();var e=Gate();if(e.HasValue)return Result<IReviveOperation>.Failure(e.Value);if(captured.Count>=8)return Result<IReviveOperation>.Failure(Error("revive.capacity",ErrorKind.Unavailable));if(!CanRevive)return Result<IReviveOperation>.Failure(Error("revive.ineligible"));
            var op=new Watch(this,authority.CurrentOwner.Value,Snapshot.RunId);captured.Add(op);return Result<IReviveOperation>.Success(op);
        }
        bool Check(Watch op)
        {
            if(op.Done.Task.Status!=UniTaskStatus.Pending)return op.Abandoned;
            var e=stopped?Error("revive.owner.closed",ErrorKind.Canceled):Interrupted(op.Wait,op.Start);
            if(!e.HasValue&&authority!=null&&!authority.ValidateCurrent(op.Owner).IsSuccess)e=Error("revive.owner.changed",ErrorKind.Canceled);
            if(e.HasValue){op.Abandoned=true;op.Cancel.Cancel();op.Done.TrySetResult(Result<ReviveResult>.Failure(e.Value));}return op.Abandoned;
        }
        void Finish(Watch op,Result<ReviveResult> result){Check(op);op.Done.TrySetResult(result);}
        static bool NoFactTerminal(AdPhase phase)=>phase==AdPhase.NotShown||phase==AdPhase.Completed||phase==AdPhase.Skipped||phase==AdPhase.Failed||phase==AdPhase.PendingFinalization;
        async UniTask Drive(Watch op)
        {
            try
            {
                op.Phase=ReviveOperationPhase.Preparing;
                var requested=ads.Capture(new RewardedAdRequest("revive",op.ClaimId,op.Owner,true));if(!requested.IsSuccess){op.ActualError=requested.Error;return;}
                op.Ad=requested.Value;op.AttemptId=op.Ad.AttemptId;
                var claim=new ReviveClaim(op.ClaimId,op.AttemptId,op.RunId,op.Owner,Snapshot.Level,5,allowedMode,ReviveClaimState.Pending);
                var prepared=await store.SaveActualAsync(Snapshot.Copy(generation:Math.Max(Snapshot.LastGeneration,op.Owner.SessionGeneration),claims:Snapshot.Claims.Concat(new[]{claim})),op.Writer);
                if(!prepared.IsSuccess){await op.Ad.WaitAsync(default);op.ActualError=prepared.Error;op.Phase=store.RecoveryRequired?ReviveOperationPhase.NeedsRecovery:ReviveOperationPhase.Failed;return;}
                Check(op);op.Phase=ReviveOperationPhase.Watching;
                var observed=await op.Ad.WaitAsync(OperationContext.Unlimited(op.Cancel.Token));
                // Canceled observation can finish while the native display remains owned by Ads.
                while(op.Ad.Receipt==null&&!NoFactTerminal(op.Ad.Phase))await UniTask.Yield();
                var receipt=op.Ad.Receipt;
                if(receipt==null)
                {
                    var aborted=await Save(Snapshot.Copy(claims:Snapshot.Claims.Select(c=>c.ClaimId==op.ClaimId?c.With(ReviveClaimState.Aborted):c)),op.ClaimId);
                    if(!aborted.IsSuccess)op.ActualError=aborted.Error;else op.ActualError=observed.IsSuccess?Error("revive.no_fact",ErrorKind.Integrity):observed.Error;return;
                }
                var retained=Record(receipt);if(!retained.IsSuccess){op.ActualError=retained.Error;op.Phase=ReviveOperationPhase.NeedsRecovery;return;}
                op.Phase=ReviveOperationPhase.Committing;var result=await ApplyFact(Snapshot.Pending,receipt);
                if(!result.IsSuccess){op.ActualError=result.Error;op.Phase=ReviveOperationPhase.NeedsRecovery;return;}
                if(!result.Value.Granted&&receipt.Outcome!=AdWatchOutcome.Skipped){op.ActualError=receipt.Error??(observed.IsSuccess?Error("revive.ad.failed",ErrorKind.Provider):observed.Error);op.Phase=ReviveOperationPhase.Failed;return;}
                op.Phase=result.Value.Granted?ReviveOperationPhase.Completed:ReviveOperationPhase.Skipped;Finish(op,result);
            }
            catch(Exception){op.ActualError=Error("revive.operation.exception",ErrorKind.Unexpected);op.Phase=ReviveOperationPhase.NeedsRecovery;}
            finally
            {
                if(op.Done.Task.Status==UniTaskStatus.Pending)Finish(op,Result<ReviveResult>.Failure(op.ActualError??Error("revive.operation.failed")));
                if(op.Phase==ReviveOperationPhase.Preparing||op.Phase==ReviveOperationPhase.Watching||op.Phase==ReviveOperationPhase.Committing)op.Phase=Snapshot.Pending!=null?ReviveOperationPhase.NeedsRecovery:ReviveOperationPhase.Failed;
                op.Writer.Dispose();captured.Remove(op);if(active==op)active=null;op.Cancel.Dispose();op.Ended.TrySetResult();
            }
        }
        sealed class Watch:IReviveOperation
        {
            readonly ReviveService owner;internal readonly ClaimOwnerSnapshot Owner;internal readonly string RunId;internal readonly CancellationTokenSource Cancel=new CancellationTokenSource();internal readonly UniTaskCompletionSource<Result<ReviveResult>> Done=new UniTaskCompletionSource<Result<ReviveResult>>();internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();
            internal ReviveRepository.Writer Writer;internal OperationContext Wait;internal long Start;internal bool Started,Abandoned;internal IAdOperation Ad;internal UniTask Driver;
            public string ClaimId {get;}=Guid.NewGuid().ToString("N");public string AttemptId {get;internal set;}public ReviveOperationPhase Phase {get;internal set;}=ReviveOperationPhase.Captured;public FrameworkError? ActualError {get;internal set;}
            internal Watch(ReviveService owner,ClaimOwnerSnapshot snapshot,string run){this.owner=owner;Owner=snapshot;RunId=run;}
            public async UniTask<Result<ReviveResult>> WaitAsync(OperationContext wait)
            {
                Main();if(Started)return Result<ReviveResult>.Failure(Error("revive.wait.single_use"));Started=true;Wait=wait;Start=Stopwatch.GetTimestamp();
                if(owner.Check(this)){Phase=ReviveOperationPhase.Failed;owner.captured.Remove(this);Cancel.Dispose();Ended.TrySetResult();return await Done.Task;}
                if(!owner.CanRevive||owner.Snapshot.RunId!=RunId){Phase=ReviveOperationPhase.Failed;owner.captured.Remove(this);Cancel.Dispose();Ended.TrySetResult();return Result<ReviveResult>.Failure(Error("revive.ineligible"));}
                var permit=owner.store.CaptureWriter();if(!permit.IsSuccess){owner.captured.Remove(this);Cancel.Dispose();Ended.TrySetResult();return Result<ReviveResult>.Failure(permit.Error);}Writer=permit.Value;owner.active=this;Driver=owner.Drive(this).Preserve();while(true){owner.Check(this);if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {
            Main();stopped=true;ready=false;
            foreach(var op in captured.ToArray()){op.Abandoned=true;op.Cancel.Cancel();op.Done.TrySetResult(Result<ReviveResult>.Failure(Error("revive.owner.closed",ErrorKind.Canceled)));if(!op.Started){captured.Remove(op);op.Cancel.Dispose();op.Ended.TrySetResult();}}
            return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup){await StopAsync(cleanup);foreach(var op in captured.ToArray())await op.Ended.Task;if(mutation!=null)await mutation.Ended.Task;return Result.Success();}
        public DiagnosticCounters ReadDiagnostics()=>new DiagnosticCounters{PendingOperations=captured.Count+(mutation==null?0:1)};
    }
}
