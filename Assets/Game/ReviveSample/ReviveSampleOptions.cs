using System;
using System.Collections.Generic;
using Game.Access;
using Game.Revive;
using MiniFramework.Ads;
using MiniFramework.Contracts;
using MiniFramework.Ownership;
using MiniFramework.Platform;
using MiniFramework.Providers.AdsMock;
using MiniFramework.Providers.PlatformMock;
namespace Game.Boot
{
    public sealed class ReviveSampleOptions:ILocalSampleExtension
    {
        public const string Scene="Assets/MiniFrameworkReviveSample/Scenes/ReviveBootstrap.unity";
        public readonly IStorageProvider Storage;public readonly AdReceiptJournal Facts;public readonly MockRewardedAdBackend Ads;
        public readonly string OwnerId,Namespace;public Action Reopen {get;set;}
        public IReadOnlyList<SampleModule> Modules {get;}public IReviveService Revive {get;}public IRevivePersistenceView Store {get;}public AdsService Coordinator {get;}
        public ReviveSampleOptions(IStorageProvider storage,AdReceiptJournal facts=null,MockRewardedAdBackend ads=null,string ownerId="sample-local-installation",string ns="local.revive-sample")
        {Storage=storage??throw new ArgumentNullException(nameof(storage));Facts=facts??new AdReceiptJournal();Ads=ads??new MockRewardedAdBackend();OwnershipPolicy.LocalOnly(ownerId,ns);OwnerId=ownerId;Namespace=ns;
            var repository=new ReviveRepository(Storage,ClaimOwnerMode.LocalOnly,ownerId,new StorageAddress(ns,"main"));var owner=new DurableLocalOwnerModule(repository,OwnershipPolicy.LocalOnly(ownerId,ns));var platform=Platform();var revive=new ReviveService(repository,Facts);Coordinator=new AdsService(Ads,revive,ownerModuleId:owner.Id);Store=repository;Revive=revive;
            Modules=Array.AsReadOnly(new[]{new SampleModule(repository,typeof(IRevivePersistenceView)),new SampleModule(owner,typeof(IClaimOwnerAuthority)),new SampleModule(platform,typeof(IPlatformCapabilities),typeof(IPlatformLifecycle)),new SampleModule(Coordinator,typeof(IAdsService)),new SampleModule(revive,typeof(IReviveService))});
        }
        public PlatformService Platform()=>new PlatformService(new MockPlatformBackend{Startup=new PlatformStartup(new PlatformCapabilities(new[]{new PlatformCapability(PlatformFeature.Lifecycle,CapabilitySupport.Supported,CapabilityAvailability.Available,"sample.mock.lifecycle"),new PlatformCapability(PlatformFeature.RewardedAds,CapabilitySupport.Supported,CapabilityAvailability.Available,"sample.mock.ads",CapabilityConstraints.ForegroundOnly|CapabilityConstraints.UserGestureRequired,1)}),PlatformVisibility.Foreground)});
    }
}
