using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum ClaimOwnerMode { LocalOnly, AccountScoped }
    public readonly struct ClaimOwnerSnapshot
    {
        public ClaimOwnerMode Mode {get;}public string OwnerId {get;}public long SessionGeneration {get;}public string StorageNamespace {get;}
        public bool IsValid=>Enum.IsDefined(typeof(ClaimOwnerMode),Mode)&&IsToken(OwnerId)&&SessionGeneration>0&&StorageIdentifier.IsValid(StorageNamespace);
        public ClaimOwnerSnapshot(ClaimOwnerMode mode,string ownerId,long sessionGeneration,string storageNamespace)
        {Mode=mode;OwnerId=ownerId;SessionGeneration=sessionGeneration;StorageNamespace=storageNamespace;if(!IsValid)throw new ArgumentException("Fixed Claim owner required.");}
        public bool SameAs(ClaimOwnerSnapshot other)=>Mode==other.Mode&&OwnerId==other.OwnerId&&SessionGeneration==other.SessionGeneration&&StorageNamespace==other.StorageNamespace;
        public static bool IsToken(string value)=>!string.IsNullOrEmpty(value)&&value.Length<=128&&value.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='.'||c=='_'||c=='-');
        public override string ToString()=>"ClaimOwnerSnapshot("+Mode+", redacted)";
    }
    public sealed class RewardedAdRequest
    {
        public string PlacementId {get;}public string ClaimId {get;}public ClaimOwnerSnapshot Owner {get;}public bool UserInitiated {get;}
        public RewardedAdRequest(string placementId,string claimId,ClaimOwnerSnapshot owner,bool userInitiated)
        {if(!PlatformIdentifier.IsValid(placementId)||!ClaimOwnerSnapshot.IsToken(claimId)||!owner.IsValid)throw new ArgumentException("Explicit placement/claim/owner required.");PlacementId=placementId;ClaimId=claimId;Owner=owner;UserInitiated=userInitiated;}
        public override string ToString()=>"RewardedAdRequest(redacted)";
    }
    public enum AdPhase { Captured, Queued, Loading, Ready, Showing, Draining, PendingFinalization, Completed, Skipped, Failed, NotShown }
    public enum AdObserverState { Created, Waiting, Succeeded, Failed, Canceled, TimedOut }
    public enum AdWatchOutcome { Completed, Skipped, Failed, NotShown }
    public sealed class AdPresentationReceipt
    {
        public string AttemptId {get;}public string ClaimId {get;}public ClaimOwnerSnapshot Owner {get;}public string PlacementId {get;}
        public PlatformExecutionMode Mode {get;}public AdWatchOutcome Outcome {get;}public bool ShowInvoked {get;}public FrameworkError? Error {get;}
        // An observed presentation fact, never a grant or durable Claim commit.
        public bool IsCompletionObserved=>Outcome==AdWatchOutcome.Completed;
        public AdPresentationReceipt(string attemptId,RewardedAdRequest request,PlatformExecutionMode mode,AdWatchOutcome outcome,bool showInvoked,FrameworkError? error=null)
        {
            if(!ClaimOwnerSnapshot.IsToken(attemptId)||request==null||!Enum.IsDefined(typeof(PlatformExecutionMode),mode)||!Enum.IsDefined(typeof(AdWatchOutcome),outcome)||(outcome==AdWatchOutcome.Completed||outcome==AdWatchOutcome.Skipped)&&!showInvoked||error.HasValue&&error.Value.Kind==ErrorKind.None)throw new ArgumentException("Receipt invalid.");
            AttemptId=attemptId;ClaimId=request.ClaimId;Owner=request.Owner;PlacementId=request.PlacementId;Mode=mode;Outcome=outcome;ShowInvoked=showInvoked;Error=error;
        }
        public bool SameAs(AdPresentationReceipt other)=>other!=null&&AttemptId==other.AttemptId&&ClaimId==other.ClaimId&&Owner.SameAs(other.Owner)&&PlacementId==other.PlacementId&&Mode==other.Mode&&Outcome==other.Outcome&&ShowInvoked==other.ShowInvoked&&Error?.Kind==other.Error?.Kind&&Error?.Code==other.Error?.Code;
        public override string ToString()=>"AdPresentationReceipt("+Mode+", "+Outcome+", redacted)";
    }
    public interface IAdReceiptJournal
    {
        // Caller-owned bounded outbox. Success confirms retention, not persistent reward/Claim grant.
        Result Record(AdPresentationReceipt receipt);
    }
    public enum AdCallbackKind { Started, Completed, Closed, Failed }
    public sealed class AdCallback
    {
        public string Generation {get;}public string AttemptId {get;}public long Sequence {get;}public AdCallbackKind Kind {get;}public FrameworkError? Error {get;}
        public AdCallback(string generation,string attemptId,long sequence,AdCallbackKind kind,FrameworkError? error=null)
        {if(!ClaimOwnerSnapshot.IsToken(generation)||!ClaimOwnerSnapshot.IsToken(attemptId)||sequence<1||!Enum.IsDefined(typeof(AdCallbackKind),kind)||kind==AdCallbackKind.Failed&&(!error.HasValue||error.Value.Kind==ErrorKind.None))throw new ArgumentException();Generation=generation;AttemptId=attemptId;Sequence=sequence;Kind=kind;Error=error;}
        public override string ToString()=>"AdCallback("+Kind+", redacted)";
    }
    public interface IAdCallbackSink
    {
        bool TryPost(AdCallback callback); // Any thread; false leaves ownership with the producer.
    }
    public interface IRewardedAdBackend
    {
        PlatformDescriptor Descriptor {get;}
        // Cold. The SDK receives placement and AttemptId, never the Claim owner or business reward.
        IRewardedAdAttempt Create(string generation,string attemptId,string placementId,IAdCallbackSink sink);
    }
    public interface IRewardedAdAttempt
    {
        UniTask<Result> LoadAsync();
        // Actual native terminal completion. Required callbacks posted/rejected before resolving.
        UniTask<Result> ShowAsync();
        Result CancelLoad(); // Before Show invocation only. No forced closing of visible ads.
        UniTask<Result> CleanupAsync(CleanupContext cleanup);
    }
    public interface IAdOperation
    {
        string AttemptId {get;}RewardedAdRequest Request {get;}AdPhase Phase {get;}AdObserverState ObserverState {get;}
        AdPresentationReceipt Receipt {get;}FrameworkError? FinalizationError {get;}FrameworkError? CancelError {get;}
        UniTask<Result<AdPresentationReceipt>> WaitAsync(OperationContext wait);
    }
    public interface IAdsService
    {
        Result<IAdOperation> Capture(RewardedAdRequest request);
        // Retries only retained facts/cleanup, never Load/Show. Single-flight finalization.
        UniTask<Result> RetryFinalizationAsync(CleanupContext cleanup,OperationContext wait);
    }
}
