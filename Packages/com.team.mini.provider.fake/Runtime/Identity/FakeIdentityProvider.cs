using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.IdentityFake
{
    public sealed class FakeIdentityProvider:IIdentityProvider
    {
        readonly List<FakeIdentityAttempt> attempts=new List<FakeIdentityAttempt>();public IReadOnlyList<FakeIdentityAttempt> Attempts=>attempts.AsReadOnly();
        public string Id=>"fake-identity";public IdentityMode Mode=>IdentityMode.Simulated;
        public string NextOwnerId {get;set;}="account-A";public bool HoldResolve {get;set;}public bool HoldCleanup {get;set;}
        public bool FailCleanup {get;set;}public bool FailCancel {get;set;}public bool ThrowCreate {get;set;}public bool ThrowResolve {get;set;}public bool ThrowCleanup {get;set;}
        public Action OnCreate {get;set;}
        public FrameworkError? ResolveError {get;set;}public BusinessIdentity OverrideIdentity {get;set;}
        public IIdentityAttempt Create(){if(ThrowCreate)throw new InvalidOperationException();var a=new FakeIdentityAttempt(this,OverrideIdentity??new BusinessIdentity(Id,NextOwnerId,Mode),ResolveError);attempts.Add(a);OnCreate?.Invoke();return a;}
    }
    public sealed class FakeIdentityAttempt:IIdentityAttempt
    {
        readonly FakeIdentityProvider owner;readonly BusinessIdentity identity;readonly FrameworkError? error;
        readonly UniTaskCompletionSource<Result<BusinessIdentity>> resolved=new UniTaskCompletionSource<Result<BusinessIdentity>>();readonly UniTaskCompletionSource<Result> cleaned=new UniTaskCompletionSource<Result>();
        bool actualEnded,closed;public int ResolveCount {get;private set;}public int CancelCount {get;private set;}public int CleanupCount {get;private set;}
        internal FakeIdentityAttempt(FakeIdentityProvider owner,BusinessIdentity identity,FrameworkError? error){this.owner=owner;this.identity=identity;this.error=error;}
        public UniTask<Result<BusinessIdentity>> ResolveAsync()
        {
            if(closed||ResolveCount>0)return UniTask.FromResult(Result<BusinessIdentity>.Failure(new FrameworkError(ErrorKind.InvalidState,"identity.fake.state")));
            ResolveCount++;if(owner.ThrowResolve){actualEnded=true;throw new OperationCanceledException("Injected unrelated exception.");}if(!owner.HoldResolve)Complete();return resolved.Task;
        }
        public bool Complete(Result<BusinessIdentity>? result=null){actualEnded=true;return resolved.TrySetResult(result??(error.HasValue?Result<BusinessIdentity>.Failure(error.Value):Result<BusinessIdentity>.Success(identity)));}
        public Result Cancel(){CancelCount++;return owner.FailCancel?Result.Failure(new FrameworkError(ErrorKind.Provider,"identity.fake.cancel")):Result.Success();}
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupCount++;if(owner.ThrowCleanup)throw new InvalidOperationException();if(owner.FailCleanup||ResolveCount>0&&!actualEnded)return Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"identity.fake.cleanup"));
            if(owner.HoldCleanup){var r=await cleaned.Task;if(!r.IsSuccess)return r;}closed=true;return Result.Success();
        }
        public bool CompleteCleanup(Result? result=null)=>cleaned.TrySetResult(result??Result.Success());
    }
}
