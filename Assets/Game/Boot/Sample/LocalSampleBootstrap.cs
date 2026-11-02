using System;
using System.IO;
using Cysharp.Threading.Tasks;
using MiniFramework.Host;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Boot
{
    public sealed class LocalSampleBootstrap:MonoBehaviour
    {
        public RectTransform LayerRoot;public RectTransform SafeRoot;public Text Status;
        public LocalSampleGame Game {get;private set;}public bool Ready {get;private set;}
        UniTask driver,shutdown;bool closing;Rect safe;
        async void Start(){driver=Run().Preserve();await driver;}
        async UniTask Run()
        {
            try
            {
                Game=new LocalSampleGame(GetComponent<RuntimeHost>(),LayerRoot);Game.StatusChanged=text=>Status.text=text;
                var result=await Game.StartAsync();if(!result.IsSuccess){Status.text=result.Error.ToString();return;}Ready=true;
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sample-smoke")>=0)await Smoke();
            }
            catch(Exception e){Status.text="Startup failed: "+e.Message;Debug.LogException(e);}
        }
        void Update()
        {
            if(SafeRoot&&Screen.width>0&&Screen.height>0&&safe!=Screen.safeArea){safe=Screen.safeArea;SafeRoot.anchorMin=new Vector2(safe.xMin/Screen.width,safe.yMin/Screen.height);SafeRoot.anchorMax=new Vector2(safe.xMax/Screen.width,safe.yMax/Screen.height);SafeRoot.offsetMin=SafeRoot.offsetMax=Vector2.zero;}
            if(Game!=null&&Ready&&UnityEngine.Input.GetKeyDown(KeyCode.Escape)&&!Game.Busy)Game.RequestHome();
        }
        public async UniTask StopAsync(){if(closing)return;closing=true;Ready=false;if(Game!=null)await Game.CloseAsync();}
        void OnDestroy(){if(!closing&&Game!=null){shutdown=StopAsync().Preserve();shutdown.Forget();}}
        async UniTask Smoke()
        {
            var ok=Game.RequestLevel(1).IsSuccess&&(await Game.Pending).IsSuccess;
            ok&=Game.Collect().IsSuccess&&Game.Coins==10;ok&=(await Game.SoundCompletion).IsSuccess&&Game.AudioBackend.SourceCount>0;ok&=Game.ToggleMute().IsSuccess;
            ok&=Game.RequestHome().IsSuccess&&(await Game.Pending).IsSuccess;
            ok&=Game.RequestLevel(2).IsSuccess&&(await Game.Pending).IsSuccess;
            ok&=Game.Collect().IsSuccess&&Game.Coins==35;
            ok&=Game.RequestHome().IsSuccess&&(await Game.Pending).IsSuccess;
            var closed=await Game.CloseAsync();closing=true;ok&=closed.IsSuccess&&Game.Context.RetainedModuleCount==0&&Game.Provider.RetainedHandleCount==0&&Game.SceneProvider.RetainedHandleCount==0;
            var args=Environment.GetCommandLineArgs();var index=Array.IndexOf(args,"--sample-evidence");if(index>=0&&index+1<args.Length)File.WriteAllText(args[index+1],"{\"passed\":"+(ok?"true":"false")+",\"coins\":"+Game.Coins+",\"retainedModules\":"+Game.Context.RetainedModuleCount+"}");
            Application.Quit(ok?0:2);
        }
    }
}
