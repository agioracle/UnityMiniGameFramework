using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // One package generation per context; dependencies close before the session is released.
    public sealed class PackageStartup : IModule, IConfigPackageReadiness, IAsyncCleanup
    {
        private readonly PackageProfile profile; private readonly IPackageBackend backend;
        private IPackageSession session; private UniTaskCompletionSource<Result> attempt,cleaning;
        private bool stopped,registered,cleanupFailed;
        public string Id {get;} public IReadOnlyList<string> Dependencies {get;}
        public string Package=>profile.Package;
        public string Schema=>profile.Schema;
        public string ConfigVersion=>ContentVersion;
        public PackageState State {get;private set;}
        public string ContentVersion {get;private set;}
        public PackageSource? SelectedSource {get;private set;}
        public Result LastResult {get;private set;}
        public bool RetainsSession=>session!=null;
        public PackageStartup(PackageProfile profile,IPackageBackend backend,string id="package",string backendModuleId=null)
        {
            this.profile=profile??throw new ArgumentNullException(nameof(profile));this.backend=backend??throw new ArgumentNullException(nameof(backend));
            if(string.IsNullOrWhiteSpace(id)||(backendModuleId!=null&&string.IsNullOrWhiteSpace(backendModuleId)))throw new ArgumentException("Module ids required.");
            Id=id;Dependencies=backendModuleId==null?Array.Empty<string>():new[]{backendModuleId};
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            CleanupWait.MainThread();
            if(registered||stopped)return UniTask.FromResult(Fail(ErrorKind.InvalidState,"package.initialize.state"));
            var ticket=context.Cleanup.Register(this);if(!ticket.IsSuccess)return UniTask.FromResult(Result.Failure(ticket.Error));
            registered=true;return StartAsync(operation);
        }
        public UniTask<Result> StartAsync(OperationContext wait)
        {
            CleanupWait.MainThread();
            if(!registered||stopped)return UniTask.FromResult(Fail(ErrorKind.NotReady,"package.not_ready"));
            var start=CleanupWait.Now;var interrupted=CleanupWait.Interrupted(wait,start);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            if(State==PackageState.Ready)return UniTask.FromResult(Result.Success());
            if(State==PackageState.Failed)return UniTask.FromResult(LastResult);
            if(attempt==null)Begin();
            return Observe(attempt,wait,start);
        }
        public UniTask<Result> RetryAsync(OperationContext wait)
        {
            CleanupWait.MainThread();
            if(stopped||!registered||State!=PackageState.Failed)return UniTask.FromResult(Fail(ErrorKind.InvalidState,"package.retry.state"));
            var start=CleanupWait.Now;var interrupted=CleanupWait.Interrupted(wait,start);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            Begin();return Observe(attempt,wait,start);
        }
        private void Begin(){State=PackageState.Initializing;attempt=new UniTaskCompletionSource<Result>();Run(attempt).Forget();}
        private async UniTask Run(UniTaskCompletionSource<Result> completion)
        {
            Result result=Fail(ErrorKind.Unavailable,"package.no_candidate");
            try
            {
                if(session!=null){result=await Release();if(!result.IsSuccess){Finish(result,completion);return;}}
                foreach(var candidate in profile.Candidates)
                {
                    if(stopped)break;
                    try
                    {
                        session=backend.Create(profile,candidate);
                        if(session==null)result=Fail(ErrorKind.Provider,"package.session.null");
                        else
                        {
                            var prepared=await session.PrepareAsync();await UniTask.SwitchToMainThread();
                            result=prepared.IsSuccess?Validate(prepared.Value,candidate):Result.Failure(prepared.Error);
                            if(result.IsSuccess&&!stopped)
                            {ContentVersion=candidate.Version;SelectedSource=candidate.Source;State=PackageState.Ready;LastResult=result;completion.TrySetResult(result);return;}
                        }
                    }
                    catch(Exception e){await UniTask.SwitchToMainThread();result=Fail(ErrorKind.Provider,"package.prepare.exception",e.Message);}
                    var released=await Release();if(!released.IsSuccess){result=released;break;}
                }
                if(stopped&&result.IsSuccess)result=Fail(ErrorKind.NotReady,"package.stopped");
            }
            catch(Exception e){result=Fail(ErrorKind.Unexpected,"package.start.exception",e.Message);}
            Finish(result,completion);
        }
        private void Finish(Result result,UniTaskCompletionSource<Result> completion){State=PackageState.Failed;LastResult=result;completion.TrySetResult(result);}
        private Result Validate(PackageManifest manifest,PackageCandidate candidate)
        {
            if(manifest==null||!manifest.IntegrityVerified)return Fail(ErrorKind.Integrity,"package.manifest.integrity");
            if(manifest.Package!=Package||manifest.ContentVersion!=candidate.Version||manifest.ConfigVersion!=candidate.Version||manifest.Schema!=profile.Schema||!Contains(manifest.CompatiblePrograms,profile.ProgramVersion))return Fail(ErrorKind.Integrity,"package.manifest.compatibility");
            foreach(var address in profile.FirstPlay)if(!Contains(manifest.AvailableFirstPlay,address))return Fail(ErrorKind.Unavailable,"package.first_play.missing");
            return Result.Success();
        }
        private static bool Contains(IReadOnlyList<string> list,string value){foreach(var item in list)if(item==value)return true;return false;}
        public Result CheckReady(string package,string contentVersion)
        {
            CleanupWait.MainThread();
            if(stopped||State!=PackageState.Ready)return Fail(ErrorKind.NotReady,"package.not_ready");
            return package==Package&&contentVersion==ContentVersion?Result.Success():Fail(ErrorKind.Integrity,"package.version.frozen");
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup){CleanupWait.MainThread();stopped=true;return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupWait.MainThread();stopped=true;
            if(cleaning!=null)return await cleaning.Task;
            var own=new UniTaskCompletionSource<Result>();cleaning=own;
            if(attempt!=null)await attempt.Task;
            // A failed release within the start attempt is retained for the registry's explicit retry.
            Result result;
            if(cleanupFailed){cleanupFailed=false;result=Fail(ErrorKind.CleanupFailed,"package.cleanup.retained");}
            else {result=await Release();cleanupFailed=false;}
            if(State==PackageState.Ready){State=PackageState.Failed;LastResult=Fail(ErrorKind.NotReady,"package.stopped");}
            cleaning=null;own.TrySetResult(result);return result;
        }
        private async UniTask<Result> Release()
        {
            if(session==null)return Result.Success();
            Result result;
            try{result=await session.CleanupAsync();await UniTask.SwitchToMainThread();}
            catch(Exception e){await UniTask.SwitchToMainThread();result=Fail(ErrorKind.CleanupFailed,"package.cleanup.exception",e.Message);}
            if(result.IsSuccess){session=null;cleanupFailed=false;}else cleanupFailed=true;
            return result;
        }
        private static async UniTask<Result> Observe(UniTaskCompletionSource<Result> operation,OperationContext wait,long start)
        {while(true){var interrupted=CleanupWait.Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;if(operation.Task.Status!=UniTaskStatus.Pending)return await operation.Task;await UniTask.Yield();}}
        private static Result Fail(ErrorKind kind,string code,string message="")=>Result.Failure(new FrameworkError(kind,code,message));
    }
}
