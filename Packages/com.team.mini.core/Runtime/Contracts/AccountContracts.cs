using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum IdentityMode { Simulated, BackendVerified }
    public sealed class BusinessIdentity
    {
        public string Issuer {get;} public string OwnerId {get;} public IdentityMode Mode {get;}
        public BusinessIdentity(string issuer,string ownerId,IdentityMode mode)
        {if(!PlatformIdentifier.IsValid(issuer)||!ClaimOwnerSnapshot.IsToken(ownerId)||!System.Enum.IsDefined(typeof(IdentityMode),mode))throw new System.ArgumentException();Issuer=issuer;OwnerId=ownerId;Mode=mode;}
        public override string ToString()=>"BusinessIdentity("+Mode+", redacted)";
    }
    // Trusted application boundary. Platform authorization codes are not BusinessIdentity.
    public interface IIdentityProvider
    {
        string Id {get;} IdentityMode Mode {get;}
        IIdentityAttempt Create(); // Cold; capture owned attempt before actual IO.
    }
    public interface IIdentityAttempt
    {
        UniTask<Result<BusinessIdentity>> ResolveAsync(); // Actual terminal result, no hidden retry.
        Result Cancel(); // Intent only; still await ResolveAsync and CleanupAsync.
        UniTask<Result> CleanupAsync(CleanupContext cleanup);
    }
    public interface IOwnerGenerationSource { Result<long> Reserve(); }
    public interface IClaimOwnerAuthority
    {
        ClaimOwnerMode Mode {get;}
        ClaimOwnerSnapshot? CurrentOwner {get;}
        Result ValidateCurrent(ClaimOwnerSnapshot owner);
        Result<StorageAddress> AddressForCurrent(string slot);
    }
    public interface IAccountSession
    {
        string ContextGeneration {get;} ClaimOwnerSnapshot Owner {get;} IScope Scope {get;} bool IsAccepting {get;}
    }
    public enum AccountState { Created, SignedOut, Resolving, Ready, PendingCleanup, Closing, Closed }
    public interface IAccountSwitch
    {
        string AttemptId {get;} FrameworkError? CancelError {get;} FrameworkError? CleanupError {get;}
        UniTask<Result<IAccountSession>> WaitAsync(OperationContext wait);
    }
    public interface IAccountService:IClaimOwnerAuthority
    {
        AccountState State {get;} IAccountSession Current {get;}
        Result<IAccountSwitch> CaptureSwitch();
        UniTask<Result> RetryRetirementAsync(CleanupContext cleanup,OperationContext wait);
    }
}
