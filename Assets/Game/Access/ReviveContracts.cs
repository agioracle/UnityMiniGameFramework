using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace Game.Access
{
    public enum ReviveRunPhase { NoRun, Playing, Failed }
    public enum ReviveClaimState { Pending, Granted, Aborted }
    public sealed class ReviveClaim
    {
        public string ClaimId {get;}public string AttemptId {get;}public string RunId {get;}public ClaimOwnerSnapshot Owner {get;}
        public int Level {get;}public int RewardCoins {get;}public PlatformExecutionMode AdMode {get;}public ReviveClaimState State {get;}public long GrantSequence {get;}
        public ReviveClaim(string claimId,string attemptId,string runId,ClaimOwnerSnapshot owner,int level,int rewardCoins,PlatformExecutionMode adMode,ReviveClaimState state,long grantSequence=0)
        {
            if(!ClaimOwnerSnapshot.IsToken(claimId)||!ClaimOwnerSnapshot.IsToken(attemptId)||!ClaimOwnerSnapshot.IsToken(runId)||!owner.IsValid||level<1||level>100000||rewardCoins<1||rewardCoins>100||!Enum.IsDefined(typeof(PlatformExecutionMode),adMode)||!Enum.IsDefined(typeof(ReviveClaimState),state)||grantSequence<0||(state==ReviveClaimState.Granted)!=(grantSequence>0))throw new ArgumentException("Claim invalid.");
            ClaimId=claimId;AttemptId=attemptId;RunId=runId;Owner=owner;Level=level;RewardCoins=rewardCoins;AdMode=adMode;State=state;GrantSequence=grantSequence;
        }
        public ReviveClaim With(ReviveClaimState state,long sequence=0)=>new ReviveClaim(ClaimId,AttemptId,RunId,Owner,Level,RewardCoins,AdMode,state,sequence);
        public override string ToString()=>"ReviveClaim("+State+", redacted)";
    }
    public sealed class ReviveSnapshot
    {
        public const int MaxClaims=64;public const long MaxCoins=1000000000;
        public ClaimOwnerMode Mode {get;}public string OwnerId {get;}public string Namespace {get;}public long LastGeneration {get;}public long Coins {get;}
        public string RunId {get;}public int Level {get;}public ReviveRunPhase Phase {get;}public bool ReviveUsed {get;}public bool Collected {get;}public int HighestCollectedLevel {get;}
        public IReadOnlyList<ReviveClaim> Claims {get;}
        public ReviveClaim Pending=>Claims.FirstOrDefault(c=>c.State==ReviveClaimState.Pending);
        public ReviveSnapshot(ClaimOwnerMode mode,string ownerId,string ns,long generation=0,long coins=0,string runId="",int level=0,ReviveRunPhase phase=ReviveRunPhase.NoRun,bool reviveUsed=false,bool collected=false,int highest=0,IEnumerable<ReviveClaim> claims=null)
        {
            var array=(claims??Array.Empty<ReviveClaim>()).ToArray();
            if(!Enum.IsDefined(typeof(ClaimOwnerMode),mode)||!ClaimOwnerSnapshot.IsToken(ownerId)||!StorageIdentifier.IsValid(ns)||generation<0||coins<0||coins>MaxCoins||!Enum.IsDefined(typeof(ReviveRunPhase),phase)||level<0||level>100000||highest<0||highest>100000||array.Length>MaxClaims||array.Any(c=>c==null)||array.Select(c=>c.ClaimId).Distinct(StringComparer.Ordinal).Count()!=array.Length||array.Select(c=>c.AttemptId).Distinct(StringComparer.Ordinal).Count()!=array.Length)throw new ArgumentException("Snapshot invalid.");
            if(phase==ReviveRunPhase.NoRun?(runId!=""||level!=0||reviveUsed||collected):(!ClaimOwnerSnapshot.IsToken(runId)||level<1))throw new ArgumentException("Run invalid.");
            if(array.Any(c=>c.Owner.Mode!=mode||c.Owner.OwnerId!=ownerId||c.Owner.StorageNamespace!=ns||c.Owner.SessionGeneration>generation)||array.Count(c=>c.State==ReviveClaimState.Pending)>1||array.Where(c=>c.State==ReviveClaimState.Granted).GroupBy(c=>c.RunId).Any(g=>g.Count()>1))throw new ArgumentException("Claim owner/run conflict.");
            if(coins<array.Where(c=>c.State==ReviveClaimState.Granted).Sum(c=>(long)c.RewardCoins)||collected&&highest<level)throw new ArgumentException("Reward/progress invalid.");
            var pending=array.FirstOrDefault(c=>c.State==ReviveClaimState.Pending);
            if(pending!=null&&(pending.RunId!=runId||pending.Level!=level||phase!=ReviveRunPhase.Failed||reviveUsed)||reviveUsed!=array.Any(c=>c.State==ReviveClaimState.Granted&&c.RunId==runId))throw new ArgumentException("Progress/claim conflict.");
            Mode=mode;OwnerId=ownerId;Namespace=ns;LastGeneration=generation;Coins=coins;RunId=runId;Level=level;Phase=phase;ReviveUsed=reviveUsed;Collected=collected;HighestCollectedLevel=highest;Claims=Array.AsReadOnly(array);
        }
        public bool Owns(ClaimOwnerSnapshot owner)=>owner.IsValid&&Mode==owner.Mode&&OwnerId==owner.OwnerId&&Namespace==owner.StorageNamespace;
        public ReviveSnapshot Copy(long? generation=null,long? coins=null,string runId=null,int? level=null,ReviveRunPhase? phase=null,bool? used=null,bool? collected=null,int? highest=null,IEnumerable<ReviveClaim> claims=null)=>new ReviveSnapshot(Mode,OwnerId,Namespace,generation??LastGeneration,coins??Coins,runId??RunId,level??Level,phase??Phase,used??ReviveUsed,collected??Collected,highest??HighestCollectedLevel,claims??Claims);
        public override string ToString()=>"ReviveSnapshot("+Phase+", redacted)";
    }
    public enum ReviveOperationPhase { Captured, Preparing, Watching, Committing, NeedsRecovery, Completed, Skipped, Failed }
    public sealed class ReviveResult
    {
        public string ClaimId {get;}public bool Granted {get;}public StorageCommitResult Commit {get;}public AdPresentationReceipt Presentation {get;}
        public bool IsDurable=>Granted&&Commit.IsRestartRecoverable;
        public ReviveResult(string claimId,bool granted,StorageCommitResult commit,AdPresentationReceipt presentation){ClaimId=claimId;Granted=granted;Commit=commit;Presentation=presentation;}
    }
    public interface IReviveOperation
    {
        string ClaimId {get;}string AttemptId {get;}ReviveOperationPhase Phase {get;}FrameworkError? ActualError {get;}
        UniTask<Result<ReviveResult>> WaitAsync(OperationContext wait);
    }
    public interface IReviveService
    {
        ReviveSnapshot Snapshot {get;}bool Busy {get;}bool CanRevive {get;}bool RecoveryRequired {get;}
        UniTask<Result> BeginRunAsync(int level,OperationContext wait);
        UniTask<Result> FailRunAsync(OperationContext wait);
        UniTask<Result> CollectAsync(int rewardCoins,OperationContext wait);
        Result<IReviveOperation> CaptureRevive();
        UniTask<Result<ReviveResult>> RecoverPendingAsync(OperationContext wait);
        UniTask<Result> AbandonPendingAsync(OperationContext wait);
    }
    public interface IRevivePersistenceView
    {
        ReviveSnapshot Snapshot {get;}StoragePersistence Persistence {get;}bool RecoveryRequired {get;}
    }
    public sealed class SampleModule
    {
        public IModule Module {get;}public IReadOnlyList<Type> Services {get;}
        public SampleModule(IModule module,params Type[] services){Module=module;Services=Array.AsReadOnly(services);}
    }
    public interface ILocalSampleExtension
    {
        IReadOnlyList<SampleModule> Modules {get;}IReviveService Revive {get;}IRevivePersistenceView Store {get;}
        Action Reopen {get;}
    }
    public interface IReviveSampleCommands
    {
        Result RequestFailure();Result RequestWatch(CancellationToken view);
        Result RequestRecoverClaim();Result RequestAbandonClaim();Result RequestReopen();
        string ReviveStatus {get;}
    }
}
