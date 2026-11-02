using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Ownership
{
    // LocalOnly publishes ownership without an AccountSession, identity provider or login.
    public sealed class LocalOwnerService:IClaimOwnerAuthority,IModule
    {
        readonly OwnershipPolicy policy;readonly IOwnerGenerationSource generations;IScope scope;bool ready,stopped,initialized;ClaimOwnerSnapshot owner;
        public string Id {get;} public IReadOnlyList<string> Dependencies {get;}=Array.Empty<string>();public ClaimOwnerMode Mode=>ClaimOwnerMode.LocalOnly;
        public ClaimOwnerSnapshot? CurrentOwner=>ready&&!stopped&&(scope==null||scope.State==ScopeState.Open)?owner:(ClaimOwnerSnapshot?)null;
        public LocalOwnerService(OwnershipPolicy policy,IOwnerGenerationSource generations,string id="owner")
        {if(policy==null||policy.Mode!=ClaimOwnerMode.LocalOnly||generations==null||string.IsNullOrWhiteSpace(id))throw new ArgumentException();this.policy=policy;this.generations=generations;Id=id;}
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Owner service requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.NotReady)=>new FrameworkError(kind,code);
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("owner.initialize.state",ErrorKind.InvalidState)));initialized=true;
            if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Result.Failure(Error("owner.initialize.canceled",ErrorKind.Canceled)));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Result.Failure(Error("owner.initialize.timeout",ErrorKind.Timeout)));
            var generation=generations.Reserve();if(!generation.IsSuccess)return UniTask.FromResult(Result.Failure(generation.Error));
            if(stopped||operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Result.Failure(Error("owner.initialize.canceled",ErrorKind.Canceled)));
            if(generation.Value<=0)return UniTask.FromResult(Result.Failure(Error("owner.generation.invalid",ErrorKind.Integrity)));
            scope=(context as IScopedModuleContext)?.Scope;owner=new ClaimOwnerSnapshot(Mode,policy.LocalOwnerId,generation.Value,policy.LocalNamespace);ready=true;return UniTask.FromResult(Result.Success());
        }
        public Result ValidateCurrent(ClaimOwnerSnapshot candidate)
        {Main();return CurrentOwner.HasValue&&candidate.SameAs(owner)?Result.Success():Result.Failure(Error("owner.current.mismatch"));}
        public Result<StorageAddress> AddressForCurrent(string slot)
        {Main();if(!CurrentOwner.HasValue)return Result<StorageAddress>.Failure(Error("owner.not_ready"));if(!StorageIdentifier.IsValid(slot))return Result<StorageAddress>.Failure(Error("owner.slot.invalid",ErrorKind.InvalidArgument));return Result<StorageAddress>.Success(new StorageAddress(owner.StorageNamespace,slot));}
        public UniTask<Result> StopAsync(CleanupContext cleanup){Main();stopped=true;ready=false;return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
    }
}
