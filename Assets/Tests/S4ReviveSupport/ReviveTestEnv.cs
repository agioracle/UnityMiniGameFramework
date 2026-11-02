using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Revive;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using MiniFramework.Ownership;
using MiniFramework.Platform;
using MiniFramework.Ads;
using MiniFramework.Providers.StorageFake;
using MiniFramework.Providers.PlatformMock;
using MiniFramework.Providers.AdsMock;
using NUnit.Framework;
namespace ReviveTests
{
    public sealed class GateStorage:IStorageProvider
    {
        readonly IStorageProvider inner;public string Id=>inner.Id;public StorageCapabilities Capabilities=>inner.Capabilities;
        public int Writes,Cleanups;public bool HoldNext,FailCleanup;public readonly List<StorageWriteRequest> Requests=new List<StorageWriteRequest>();readonly List<UniTaskCompletionSource> held=new List<UniTaskCompletionSource>();
        public GateStorage(IStorageProvider inner){this.inner=inner;}public void Release(){HoldNext=false;foreach(var h in held.ToArray())h.TrySetResult();}
        public IStorageSession Create(StorageAddress address)=>new Session(this,inner.Create(address));
        sealed class Session:IStorageSession
        {
            readonly GateStorage owner;readonly IStorageSession inner;public Session(GateStorage owner,IStorageSession inner){this.owner=owner;this.inner=inner;}
            public UniTask<Result<StorageReadResult>> ReadAsync()=>inner.ReadAsync();
            public async UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request){owner.Writes++;owner.Requests.Add(request);if(owner.HoldNext){owner.HoldNext=false;var h=new UniTaskCompletionSource();owner.held.Add(h);await h.Task;}return await inner.CommitAsync(request);}
            public UniTask<Result> CleanupAsync(CleanupContext cleanup){owner.Cleanups++;return owner.FailCleanup?UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.store.cleanup"))):inner.CleanupAsync(cleanup);}
        }
    }
    public sealed class MutableOwner:IModule,IClaimOwnerAuthority
    {
        public string Id=>"owner";public IReadOnlyList<string> Dependencies {get;}public ClaimOwnerMode Mode=>ClaimOwnerMode.LocalOnly;public ClaimOwnerSnapshot? CurrentOwner {get;set;}
        public MutableOwner(ClaimOwnerSnapshot owner,string store){CurrentOwner=owner;Dependencies=new[]{store};}
        public Result ValidateCurrent(ClaimOwnerSnapshot value)=>CurrentOwner.HasValue&&CurrentOwner.Value.SameAs(value)?Result.Success():Result.Failure(new FrameworkError(ErrorKind.NotReady,"test.owner.changed"));
        public Result<StorageAddress> AddressForCurrent(string slot)=>Result<StorageAddress>.Success(new StorageAddress(CurrentOwner.Value.StorageNamespace,slot));
        public UniTask<Result> InitializeAsync(IModuleContext c,OperationContext o)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> StopAsync(CleanupContext c){CurrentOwner=null;return UniTask.FromResult(Result.Success());}public UniTask<Result> DisposeAsync(CleanupContext c)=>UniTask.FromResult(Result.Success());
    }
    public sealed class Env
    {
        public static OperationContext Wait=>OperationContext.Unlimited();public static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
        public const string OwnerId="installation-A",Ns="local.a";public static StorageAddress Address=>new StorageAddress(Ns,"main");
        public readonly FakeStorageStore Disk;public readonly AdReceiptJournal Facts;public readonly GateStorage Storage;public readonly ReviveRepository Store;public readonly DurableLocalOwnerModule Owner;public readonly MutableOwner SwitchOwner;public readonly MockRewardedAdBackend Backend=new MockRewardedAdBackend();
        public readonly PlatformService Platform;public readonly AdsService Ads;public readonly ReviveService Service;public readonly GameContext Context;
        public Env(FakeStorageStore disk=null,AdReceiptJournal facts=null,IStorageProvider provider=null,string ownerId=OwnerId,PlatformExecutionMode allowed=PlatformExecutionMode.Mock,bool switchingOwner=false)
        {
            Disk=disk??new FakeStorageStore();Facts=facts??new AdReceiptJournal();Storage=new GateStorage(provider??new FakeStorageProvider(Disk));Store=new ReviveRepository(Storage,ClaimOwnerMode.LocalOnly,ownerId,Address);Owner=switchingOwner?null:new DurableLocalOwnerModule(Store,OwnershipPolicy.LocalOnly(ownerId,Ns));SwitchOwner=switchingOwner?new MutableOwner(new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,ownerId,1,Ns),Store.Id):null;
            Platform=new PlatformService(new MockPlatformBackend{Startup=new PlatformStartup(new PlatformCapabilities(new[]{new PlatformCapability(PlatformFeature.Lifecycle,CapabilitySupport.Supported,CapabilityAvailability.Available,"test.life"),new PlatformCapability(PlatformFeature.RewardedAds,CapabilitySupport.Supported,CapabilityAvailability.Available,"test.ads",CapabilityConstraints.ForegroundOnly|CapabilityConstraints.UserGestureRequired,1)}),PlatformVisibility.Foreground)});
            Service=new ReviveService(Store,Facts,allowed);Ads=new AdsService(Backend,Service,ownerModuleId:"owner");
            Context=new GameContext(new[]{Reg(Store,typeof(IRevivePersistenceView)),Reg((IModule)Owner??SwitchOwner,typeof(IClaimOwnerAuthority)),Reg(Platform,typeof(IPlatformCapabilities),typeof(IPlatformLifecycle)),Reg(Ads,typeof(IAdsService)),Reg(Service,typeof(IReviveService))},Cleanup);
        }
        static ModuleRegistration Reg(IModule module,params Type[] services)=>new ModuleRegistration(module.Id,module.Dependencies,()=>module,services);
        public static void Check(Result r)=>Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);
        public async UniTask Start()=>Check(await Context.StartAsync(Wait));
        public async UniTask FailedRun(){Check(await Service.BeginRunAsync(1,Wait));Check(await Service.FailRunAsync(Wait));}
        public IReviveOperation Capture(){var c=Service.CaptureRevive();Assert.IsTrue(c.IsSuccess,c.IsSuccess?"":c.Error.Code);return c.Value;}
        public MockRewardedAdAttempt Attempt=>Backend.Attempts[Backend.Attempts.Count-1];
        public static async UniTask Frames(int n=5){for(var i=0;i<n;i++)await UniTask.Yield();}
        public async UniTask Finish()
        {
            Storage.FailCleanup=false;Disk.FailCleanup=false;Disk.Faults.Clear();Backend.FailCleanup=Backend.ThrowCleanup=Backend.HoldCleanup=false;Storage.Release();var close=Context.CloseAsync(Wait);
            for(var i=0;i<10;i++){Storage.Release();foreach(var a in Backend.Attempts){a.CompleteLoad();a.CompleteShow();a.CompleteCleanup();}await UniTask.Yield();}
            var r=await close;if(!r.IsSuccess)r=await Context.RetryCleanupAsync(Cleanup,Wait);Check(r);Assert.AreEqual(0,Context.RetainedModuleCount);
        }
        public ReviveSnapshot ReadRaw(){var decoded=StorageFormat.Decode(Disk.CopyRaw(Address),new StorageLimits());Assert.IsTrue(decoded.IsSuccess);var data=ReviveCodec.Decode(decoded.Value.Records[0].CopyPayload());Assert.IsTrue(data.IsSuccess);return data.Value;}
        public void Seed(ReviveSnapshot value,long sequence=1){var bytes=ReviveCodec.Encode(value).Value;Disk.SeedRaw(Address,StorageFormat.Encode(new StorageContainer(Address,sequence,new[]{new StorageRecord(ReviveCodec.ModuleId,1,ReviveCodec.CodecId,true,bytes)}),new StorageLimits()).Value);}
    }
}
