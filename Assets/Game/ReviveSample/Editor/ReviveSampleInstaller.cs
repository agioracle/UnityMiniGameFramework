using System;
using System.IO;
using Game.Features;
using MiniFramework.Contracts;
using MiniFramework.Providers.StorageEditor;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace Game.Boot.Editor
{
    public static class ReviveSampleInstaller
    {
        public const string Root="Assets/MiniFrameworkReviveSample",Prefab=Root+"/Resources/MiniFrameworkReviveSample/ui/revive.prefab";
        [MenuItem("Tools/MiniFramework/Revive Sample/Install or Validate")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before installing.");
            if(Directory.Exists(Root)&&!File.Exists(Root+"/sample-owner.txt"))throw new InvalidOperationException("Unowned sample folder.");
            LocalSampleInstaller.Validate();Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Path.GetDirectoryName(Prefab));File.WriteAllText(Root+"/sample-owner.txt","MiniFramework Revive sample v1\n");AssetDatabase.Refresh();
            if(!File.Exists(Prefab))CreatePanel();
            AdaptPanel("home",false);AdaptPanel("level",false);AdaptPanel("revive",true);
            if(!File.Exists(ReviveSampleOptions.Scene))
            {
                for(var i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save authored scenes before installing.");
                if(!AssetDatabase.CopyAsset(SampleContent.BootstrapScene,ReviveSampleOptions.Scene))throw new IOException("Scene copy failed.");
                var setup=EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    var scene=EditorSceneManager.OpenScene(ReviveSampleOptions.Scene,OpenSceneMode.Single);var old=UnityEngine.Object.FindObjectOfType<LocalSampleBootstrap>();if(!old)throw new InvalidOperationException("Base bootstrap missing.");old.enabled=false;
                    var boot=old.gameObject.AddComponent<ReviveSampleBootstrap>();boot.LayerRoot=old.LayerRoot;boot.SafeRoot=old.SafeRoot;boot.Status=old.Status;EditorSceneManager.SaveScene(scene);
                }
                finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
            }
            Validate();AssetDatabase.SaveAssets();
        }
        static void AdaptPanel(string name,bool overlay)
        {
            var path=Root+"/Resources/MiniFrameworkReviveSample/ui/"+name+".prefab";
            if(!File.Exists(path)&&!AssetDatabase.CopyAsset("Assets/MiniFrameworkSample/Resources/MiniFrameworkSample/ui/"+name+".prefab",path))throw new IOException("Owned UI copy failed.");
            var go=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rect=(RectTransform)go.transform;rect.anchorMin=new Vector2(overlay ? .64f : .02f,.08f);rect.anchorMax=new Vector2(overlay ? .99f : .62f,.94f);rect.offsetMin=rect.offsetMax=Vector2.zero;
                foreach(var button in go.GetComponentsInChildren<Button>(true))
                {
                    var r=(RectTransform)button.transform;var y=r.anchoredPosition.y;var height=r.sizeDelta.y;r.anchorMin=new Vector2(.05f,.5f);r.anchorMax=new Vector2(.95f,.5f);r.sizeDelta=new Vector2(0,height);r.anchoredPosition=new Vector2(0,y);button.targetGraphic=button.GetComponent<Image>();
                }
                foreach(var text in go.GetComponentsInChildren<Text>(true))
                {
                    var r=(RectTransform)text.transform;
                    if(text.transform.parent.GetComponent<Button>()){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(4,2);r.offsetMax=new Vector2(-4,-2);}
                    else{var y=r.anchoredPosition.y;var height=r.sizeDelta.y;r.anchorMin=new Vector2(.05f,.5f);r.anchorMax=new Vector2(.95f,.5f);r.sizeDelta=new Vector2(0,overlay?120:height);r.anchoredPosition=new Vector2(0,overlay?190:y);}
                    text.resizeTextForBestFit=true;text.resizeTextMinSize=10;text.resizeTextMaxSize=text.fontSize;text.horizontalOverflow=HorizontalWrapMode.Wrap;
                }
                PrefabUtility.SaveAsPrefabAsset(go,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(go);}
        }
        public static void Validate()
        {
            foreach(var name in new[]{"home","level"})
            {
                var path=Root+"/Resources/MiniFrameworkReviveSample/ui/"+name+".prefab";var owned=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!owned||AssetDatabase.AssetPathToGUID(path)==AssetDatabase.AssetPathToGUID("Assets/MiniFrameworkSample/Resources/MiniFrameworkSample/ui/"+name+".prefab"))throw new InvalidOperationException("Owned UI missing.");
                foreach(var button in owned.GetComponentsInChildren<Button>(true)){var r=(RectTransform)button.transform;if(r.anchorMin.x!=.05f||r.anchorMax.x!=.95f||r.sizeDelta.x!=0)throw new InvalidOperationException("Owned UI width must adapt.");}
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);if(!prefab)throw new InvalidOperationException("Revive prefab missing.");var p=prefab.GetComponent<SampleRevivePanel>();
            if(!p||!p.State||!p.FailButton||!p.WatchButton||!p.RecoverButton||!p.AbandonButton||!p.ReopenButton)throw new InvalidOperationException("Revive bindings missing.");
        }
        static void CreatePanel()
        {
            var root=new GameObject("RevivePanel",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(SampleRevivePanel));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=new Vector2(.64f,.08f);rect.anchorMax=new Vector2(.99f,.94f);rect.offsetMin=rect.offsetMax=Vector2.zero;root.GetComponent<Image>().color=new Color(.06f,.12f,.17f,.98f);
                var panel=root.GetComponent<SampleRevivePanel>();panel.State=Text(root.transform,"State","MOCK / Volatile\nConfirmed state only",new Vector2(0,180),new Vector2(310,100),18);
                panel.FailButton=Button(root.transform,"Fail","1. Simulate failure",100);panel.WatchButton=Button(root.transform,"Watch","2. Watch MOCK ad / revive",40);panel.RecoverButton=Button(root.transform,"Recover","Recover same claim",-20);panel.AbandonButton=Button(root.transform,"Abandon","Abandon unproven claim",-80);panel.ReopenButton=Button(root.transform,"Reopen","Close / reopen Context",-140);
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static Text Text(Transform parent,string name,string value,Vector2 position,Vector2 size,int font)
        {var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=position;var t=go.GetComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=value;t.fontSize=font;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;return t;}
        static Button Button(Transform parent,string name,string label,float y)
        {var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(300,48);r.anchoredPosition=new Vector2(0,y);go.GetComponent<Image>().color=new Color(.12f,.3f,.38f,1);Text(go.transform,"Label",label,Vector2.zero,r.sizeDelta,17);return go.GetComponent<Button>();}
        [MenuItem("Tools/MiniFramework/Revive Sample/Open Bootstrap")]
        public static void Open(){Install();EditorSceneManager.OpenScene(ReviveSampleOptions.Scene);}
    }
    public sealed class RevivePersistenceLab:EditorWindow
    {
        UniTaskHolder running;
        [MenuItem("Tools/MiniFramework/Revive Sample/Persistence Lab")]
        public static void ShowLab()=>GetWindow<RevivePersistenceLab>("Revive Persistence");
        void OnGUI()
        {
            EditorGUILayout.LabelField("Explicit local demonstration; no real ads or accounts.",EditorStyles.wordWrappedLabel);
            if(GUILayout.Button("Install / Open Revive Bootstrap")&&!EditorApplication.isPlaying)ReviveSampleInstaller.Open();
            var boot=UnityEngine.Object.FindObjectOfType<ReviveSampleBootstrap>();
            if(!EditorApplication.isPlaying||!boot){EditorGUILayout.HelpBox("Open the dedicated bootstrap, then Play. Default: Mock ads + Volatile storage.",MessageType.Info);return;}
            var root=Path.GetFullPath("Library/MiniFrameworkReviveSave");EditorGUILayout.LabelField("Editor save root",root);
            if(GUILayout.Button("Explicitly reopen with Editor disk backend"))Start(boot.OpenAsync(new EditorStorageProvider(root)));
            if(GUILayout.Button("Close / reopen same backend"))Start(boot.OpenAsync());
            if(GUILayout.Button("Explicitly retry failed cleanup, then reopen"))Start(boot.OpenAsync(retryCleanup:true));
            if(running!=null)EditorGUILayout.LabelField(running.Message);
            EditorGUILayout.HelpBox("No data deletion. Disk saves survive normal Context/Editor reopen in this Library root; deleting Library removes them. Native/device/AOT not certified.",MessageType.Info);
        }
        void Start(Cysharp.Threading.Tasks.UniTask<Result> task){running=new UniTaskHolder(task);}
        sealed class UniTaskHolder
        {
            readonly Cysharp.Threading.Tasks.UniTask driver;internal string Message="Running...";
            internal UniTaskHolder(Cysharp.Threading.Tasks.UniTask<Result> task){driver=Observe(task).Preserve();}
            async Cysharp.Threading.Tasks.UniTask Observe(Cysharp.Threading.Tasks.UniTask<Result> task){try{var r=await task;Message=r.IsSuccess?"Completed":"Retained / failed: "+r.Error.Code;}catch(Exception e){Message=e.GetType().Name;}}
        }
    }
}
