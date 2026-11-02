using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MiniFramework.Scenes
{
    // Built-in scene adapter. Editor fixtures inject EditorSceneManager's real async loader.
    // Deferred Unity loads stall the engine queue, so only one native load may await activation.
    public sealed class UnitySceneProvider : ISceneProvider
    {
        readonly Dictionary<string,string> paths;
        readonly string id,package,version;
        readonly Func<string,AsyncOperation> load;
        readonly Func<Scene,AsyncOperation> unload;
        bool pending;
        public bool SupportsDeferredActivation=>true;
        public int LoadStartCount {get;private set;}
        public int RetainedHandleCount {get;private set;}
        public UnitySceneProvider(string id,string package,string version,IReadOnlyDictionary<string,string> addresses,
            Func<string,AsyncOperation> load=null,Func<Scene,AsyncOperation> unload=null)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(package)||string.IsNullOrWhiteSpace(version)||addresses==null)throw new ArgumentException("Scene mapping required.");
            this.id=id;this.package=package;this.version=version;paths=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in addresses){if(string.IsNullOrWhiteSpace(pair.Key)||string.IsNullOrWhiteSpace(pair.Value)||!pair.Value.StartsWith("Assets/",StringComparison.Ordinal)||!pair.Value.EndsWith(".unity",StringComparison.Ordinal)||pair.Value.Contains("..")||pair.Value.Contains("\\"))throw new ArgumentException("Exact Assets scene path required.");paths.Add(pair.Key,pair.Value);}
            this.load=load??(path=>SceneManager.LoadSceneAsync(path,LoadSceneMode.Additive));
            this.unload=unload??(scene=>SceneManager.UnloadSceneAsync(scene));
        }
        public Result<IProviderOperation<IProviderScene>> BeginLoadScene(ResourceKey key,SceneLoadOptions options)
        {
            SceneService.Main();
            if(!key.IsValid||key.Kind!=ResourceKind.Scene||key.Provider!=id||key.Package!=package||key.ContentVersion!=version)return SceneService.Fail<IProviderOperation<IProviderScene>>("unity.scene.key",ErrorKind.InvalidArgument);
            if(options.Mode!=SceneLoadMode.Additive)return SceneService.Fail<IProviderOperation<IProviderScene>>("unity.scene.requires_staging",ErrorKind.NotSupported);
            if(pending)return SceneService.Fail<IProviderOperation<IProviderScene>>("unity.scene.queue.busy");
            if(!paths.TryGetValue(key.Address,out var path))return SceneService.Fail<IProviderOperation<IProviderScene>>("unity.scene.address",ErrorKind.Unavailable);
            try
            {
                var existing=new HashSet<int>();for(var i=0;i<SceneManager.sceneCount;i++)existing.Add(SceneManager.GetSceneAt(i).handle);
                var native=load(path);if(native==null)return SceneService.Fail<IProviderOperation<IProviderScene>>("unity.scene.load.null",ErrorKind.Provider);
                native.allowSceneActivation=options.ActivateOnLoad;pending=true;LoadStartCount++;RetainedHandleCount++;
                var handle=new Handle(this,key,options,path,existing,native);var operation=new Operation<IProviderScene>();Load(handle,operation).Forget();
                return Result<IProviderOperation<IProviderScene>>.Success(operation);
            }
            catch(Exception e){return Result<IProviderOperation<IProviderScene>>.Failure(new FrameworkError(ErrorKind.Provider,"unity.scene.load.start",e.Message));}
        }
        async UniTask Load(Handle handle,Operation<IProviderScene> operation)
        {
            // Native AsyncOperation has no cancellation. Retain it until actual activation/unload.
            while(!handle.native.isDone && (handle.Options.ActivateOnLoad||handle.native.progress<0.9f))await UniTask.Yield();
            if(handle.Options.ActivateOnLoad)handle.ConfirmActive();
            else handle.State=SceneLeaseState.AwaitingActivation;
            // Even a failed activation returns its owned handle; SceneService reclaims it.
            operation.Complete(Result<IProviderScene>.Success(handle));
        }
        sealed class Operation<T>:IProviderOperation<T>
        {
            readonly UniTaskCompletionSource<Result<T>> completion=new UniTaskCompletionSource<Result<T>>();
            public string Id {get;}=Guid.NewGuid().ToString("N");
            public BackendState State {get;private set;}=BackendState.Running;
            public UniTask<Result<T>> Completion=>completion.Task;
            internal void Complete(Result<T> result){State=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;completion.TrySetResult(result);}
        }
        sealed class Handle:IProviderScene
        {
            readonly UnitySceneProvider owner;readonly string path;readonly HashSet<int> existing;
            internal readonly AsyncOperation native;Scene scene;Operation<Unit> activation,unloading;bool failedUnload;
            public ResourceKey Key {get;}public SceneLoadOptions Options {get;}
            public SceneLeaseState State {get;internal set;}=SceneLeaseState.Loading;
            public bool HoldsDependencies=>State!=SceneLeaseState.Released;
            internal Handle(UnitySceneProvider owner,ResourceKey key,SceneLoadOptions options,string path,HashSet<int> existing,AsyncOperation native)
            {this.owner=owner;Key=key;Options=options;this.path=path;this.existing=existing;this.native=native;}
            internal bool ConfirmActive()
            {
                owner.pending=false;
                for(var i=0;i<SceneManager.sceneCount;i++)
                {var candidate=SceneManager.GetSceneAt(i);if(candidate.path==path&&!existing.Contains(candidate.handle)){scene=candidate;break;}}
                var success=scene.IsValid()&&scene.isLoaded&&SceneManager.SetActiveScene(scene);
                State=success?SceneLeaseState.Active:SceneLeaseState.Failed;return success;
            }
            public Result<IProviderOperation<Unit>> BeginActivation()
            {
                SceneService.Main();if(activation!=null&&activation.State==BackendState.Running)return Result<IProviderOperation<Unit>>.Success(activation);
                if(State!=SceneLeaseState.AwaitingActivation)return SceneService.Fail<IProviderOperation<Unit>>("unity.scene.activation.state");
                activation=new Operation<Unit>();Activate(activation).Forget();return Result<IProviderOperation<Unit>>.Success(activation);
            }
            async UniTask Activate(Operation<Unit> operation)
            {native.allowSceneActivation=true;while(!native.isDone)await UniTask.Yield();operation.Complete(ConfirmActive()?Result<Unit>.Success(default):SceneService.Fail<Unit>("unity.scene.activation.failed",ErrorKind.Provider));}
            public Result<IProviderOperation<Unit>> BeginUnload(CleanupContext cleanup)
            {
                SceneService.Main();if(unloading!=null&&unloading.State==BackendState.Running)return Result<IProviderOperation<Unit>>.Success(unloading);
                if(State==SceneLeaseState.Released||failedUnload||activation!=null&&activation.State==BackendState.Running)return SceneService.Fail<IProviderOperation<Unit>>("unity.scene.unload.state");
                return StartUnload();
            }
            public Result<IProviderOperation<Unit>> RetryUnload(CleanupContext cleanup)
            {SceneService.Main();return failedUnload?StartUnload():SceneService.Fail<IProviderOperation<Unit>>("unity.scene.retry.state");}
            Result<IProviderOperation<Unit>> StartUnload()
            {failedUnload=false;State=SceneLeaseState.Unloading;unloading=new Operation<Unit>();Unload(unloading).Forget();return Result<IProviderOperation<Unit>>.Success(unloading);}
            async UniTask Unload(Operation<Unit> operation)
            {
                Result<Unit> result;
                try
                {
                    if(!native.isDone){native.allowSceneActivation=true;while(!native.isDone)await UniTask.Yield();ConfirmActive();State=SceneLeaseState.Unloading;}
                    // An externally unloaded scene is already confirmed absent.
                    if(scene.IsValid()&&scene.isLoaded)
                    {
                        var actual=owner.unload(scene);if(actual==null)throw new InvalidOperationException("Native unload rejected.");
                        while(!actual.isDone)await UniTask.Yield();
                        if(scene.IsValid()&&scene.isLoaded)throw new InvalidOperationException("Scene still loaded.");
                    }
                    State=SceneLeaseState.Released;owner.RetainedHandleCount--;owner.pending=false;result=Result<Unit>.Success(default);
                }
                catch(Exception e){State=SceneLeaseState.Failed;failedUnload=true;result=Result<Unit>.Failure(new FrameworkError(ErrorKind.CleanupFailed,"unity.scene.unload.failed",e.Message));}
                operation.Complete(result);
            }
        }
    }
}
