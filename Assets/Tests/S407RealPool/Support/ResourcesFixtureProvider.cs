using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.S407RealPool
{
    // Validation fixture, not a new shipping provider. Resources owns the source prefab.
    // Release acknowledges framework handles only; no AssetBundle/Resources unload claim.
    public sealed class ResourcesFixtureProvider : IAssetProvider
    {
        public string Id => "s407-resources-fixture";
        public int Handles, ReleaseStarts, ReleaseOrderViolations, PendingReleases;
        public bool HoldRelease;
        public Func<string,bool> HasLiveInstances;
        readonly List<Action> held = new List<Action>();
        public ResourceKey Key(string address) => new ResourceKey(Id,"main","v1",address,typeof(GameObject));
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T:class
        {
            if(typeof(T)!=typeof(GameObject)||key.Provider!=Id||key.Package!="main"||key.ContentVersion!="v1"||(key.Address!="mixed"&&key.Address!="cold"))
                return Fail<IProviderOperation<IProviderAsset<T>>>("fixture.key");
            var op=new Operation<IProviderAsset<T>>(); op.Keep(Load(key,op));
            return Result<IProviderOperation<IProviderAsset<T>>>.Success(op);
        }
        async UniTask Load<T>(ResourceKey key,Operation<IProviderAsset<T>> op) where T:class
        {
            try
            {
                var request=Resources.LoadAsync<GameObject>("S407RealPool/template");
                while(!request.isDone) await UniTask.NextFrame();
                if(!request.asset) { op.Complete(Fail<IProviderAsset<T>>("fixture.prefab_missing")); return; }
                Handles++; op.Complete(Result<IProviderAsset<T>>.Success(new Handle<T>(this,key,(T)(object)request.asset)));
            }
            catch(Exception ex) { op.Complete(Result<IProviderAsset<T>>.Failure(new FrameworkError(ErrorKind.Provider,"fixture.load",ex.Message))); }
        }
        public void ReleaseHeld()
        { HoldRelease=false; var copy=held.ToArray(); held.Clear(); foreach(var finish in copy)finish(); }
        sealed class Handle<T> : IProviderAsset<T> where T:class
        {
            readonly ResourcesFixtureProvider owner; T value;
            public ResourceKey Key {get;}
            public AssetLeaseState State {get;private set;}=AssetLeaseState.Active;
            public T Value => State==AssetLeaseState.Active?value:throw new InvalidOperationException("fixture.released");
            public Handle(ResourcesFixtureProvider owner,ResourceKey key,T value) {this.owner=owner;Key=key;this.value=value;}
            public Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup)
            {
                if(State!=AssetLeaseState.Active)return Fail<IProviderOperation<Unit>>("fixture.release_state");
                State=AssetLeaseState.Releasing; owner.ReleaseStarts++; owner.PendingReleases++;
                if(owner.HasLiveInstances?.Invoke(Key.Address)==true)owner.ReleaseOrderViolations++;
                var op=new Operation<Unit>();
                Action finish=()=>{value=null; State=AssetLeaseState.Released; owner.Handles--; owner.PendingReleases--; op.Complete(Result<Unit>.Success(default));};
                if(owner.HoldRelease)owner.held.Add(finish); else finish();
                return Result<IProviderOperation<Unit>>.Success(op);
            }
            public Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup)=>Fail<IProviderOperation<Unit>>("fixture.retry_state");
        }
        static Result<T> Fail<T>(string code)=>Result<T>.Failure(new FrameworkError(ErrorKind.Provider,code));
        sealed class Operation<T> : IProviderOperation<T>
        {
            readonly UniTaskCompletionSource<Result<T>> completion=new UniTaskCompletionSource<Result<T>>();
            UniTask driver;
            public string Id {get;}=Guid.NewGuid().ToString("N");
            public BackendState State {get;private set;}=BackendState.Running;
            public UniTask<Result<T>> Completion=>completion.Task;
            public void Keep(UniTask task) {driver=task.Preserve();}
            public void Complete(Result<T> result) {State=result.IsSuccess?BackendState.Succeeded:BackendState.Failed; completion.TrySetResult(result);}
        }
    }
}
