using System;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Ads;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Providers.AdsMock;
using MiniFramework.Providers.StorageFake;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Boot
{
    public sealed class ReviveSampleBootstrap:MonoBehaviour
    {
        public RectTransform LayerRoot,SafeRoot;public Text Status;
        public LocalSampleGame Game {get;private set;}public bool Ready {get;private set;}
        public readonly FakeStorageStore VolatileStore=new FakeStorageStore();public readonly AdReceiptJournal Facts=new AdReceiptJournal();public readonly MockRewardedAdBackend MockAds=new MockRewardedAdBackend();
        IStorageProvider provider;UniTask driver,shutdown;bool changing,destroying;ReviveSnapshot shown;Rect safe;
        async void Start(){driver=OpenAsync(new FakeStorageProvider(VolatileStore)).Preserve();await driver;}
        public async UniTask<Result> OpenAsync(IStorageProvider storage=null,bool retryCleanup=false)
        {
            if(storage!=null&&provider!=null&&!ReferenceEquals(storage,provider)&&Game!=null&&(Game.Revive.RecoveryRequired||Facts.PendingCount>0))return Result.Failure(new FrameworkError(ErrorKind.InvalidState,"sample.backend_switch.pending_claim"));
            if(retryCleanup&&(Game==null||Game.Context.State!=GameContextState.CleanupFailed))return Result.Failure(new FrameworkError(ErrorKind.InvalidState,"sample.cleanup_retry.no_failure"));
            if(changing||destroying)return Result.Failure(new FrameworkError(ErrorKind.Unavailable,"sample.revive.reopening"));changing=true;Ready=false;
            try
            {
                if(Game!=null)
                {
                    var closed=await Game.CloseAsync();
                    if(retryCleanup)closed=await Game.Context.RetryCleanupAsync(LocalSampleGame.Cleanup,LocalSampleGame.Wait);
                    if(!closed.IsSuccess){Status.text="Cleanup retained. Fix fault, then explicitly Retry cleanup.";return closed;}
                }
                provider=storage??provider??new FakeStorageProvider(VolatileStore);var options=new ReviveSampleOptions(provider,Facts,MockAds);options.Reopen=()=>{driver=OpenAsync().Preserve();driver.Forget();};
                Game=new LocalSampleGame(GetComponent<RuntimeHost>(),LayerRoot,revive:options);Game.StatusChanged=s=>{if(Status)Status.text=s;};var started=await Game.StartAsync();Ready=started.IsSuccess;shown=Game.ReviveStore.Snapshot;
                if(!started.IsSuccess)Status.text="Restore failed: "+started.Error.Code;return started;
            }
            finally{changing=false;}
        }
        void Update()
        {
            if(SafeRoot&&Screen.width>0&&Screen.height>0&&safe!=Screen.safeArea){safe=Screen.safeArea;SafeRoot.anchorMin=new Vector2(safe.xMin/Screen.width,safe.yMin/Screen.height);SafeRoot.anchorMax=new Vector2(safe.xMax/Screen.width,safe.yMax/Screen.height);SafeRoot.offsetMin=SafeRoot.offsetMax=Vector2.zero;}
            if(Ready&&Game!=null&&shown!=Game.ReviveStore.Snapshot){shown=Game.ReviveStore.Snapshot;Game.RefreshReviveStatus();}
        }
        public async UniTask<Result> StopAsync(){Ready=false;return Game==null?Result.Success():await Game.CloseAsync();}
        void OnDestroy(){destroying=true;if(Game!=null){shutdown=StopAsync().Preserve();shutdown.Forget();}}
    }
}
