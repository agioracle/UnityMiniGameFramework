using System;
using System.IO;
using System.Linq;
using Game.Config;
using Game.Config.Generated;
using Game.Features;
using MiniFramework.Config;
using MiniFramework.Host;
using MiniFramework.UI;
using MiniFramework.UI.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace Game.Boot.Editor
{
    public static class LocalSampleInstaller
    {
        public const string Root=SampleContent.AssetRoot;
        const string ResourcesRoot=Root+"/Resources/"+SampleContent.Root;
        [MenuItem("Tools/MiniFramework/Local Sample/Install or Validate")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before installing.");
            if(AssetDatabase.IsValidFolder(Root)&&!File.Exists(Root+"/template-owner.txt"))throw new InvalidOperationException("Unowned sample root; refusing overwrite.");
            Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Root+"/Editor");Directory.CreateDirectory(ResourcesRoot+"config");Directory.CreateDirectory(ResourcesRoot+"ui");Directory.CreateDirectory(ResourcesRoot+"audio");
            File.WriteAllText(Root+"/template-owner.txt","MiniFramework LocalCore template v1\n");AssetDatabase.Refresh();
            foreach(var file in new[]{"catalog.json","tblevel.bytes","tbreward.bytes"})CopyConfig(file);
            if(!File.Exists(ResourcesRoot+"audio/click.wav"))WriteWave(ResourcesRoot+"audio/click.wav");AssetDatabase.Refresh();
            Panel<SampleHomePanel>("home",new[]{"Start","Retry"},new[]{"Start tutorial","Retry cleanup"});
            Panel<SampleLevelPanel>("level",new[]{"Collect","Back","Mute"},new[]{"Collect reward","Back to home","Mute / unmute"});
            for(var i=0;i<SceneManager.sceneCount;i++){var open=SceneManager.GetSceneAt(i);if(open.isDirty||string.IsNullOrEmpty(open.path)&&open.rootCount>0)throw new InvalidOperationException("Save authored scenes before installing sample scenes.");}
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if(!File.Exists(SampleContent.BootstrapScene))Bootstrap();
                foreach(var path in SampleContent.Scenes.Values)if(!File.Exists(path))Level(path);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            var scenes=EditorBuildSettings.scenes.ToList();foreach(var path in new[]{SampleContent.BootstrapScene}.Concat(SampleContent.Scenes.Values))
            {var found=scenes.FirstOrDefault(s=>s.path==path);if(found==null)scenes.Add(new EditorBuildSettingsScene(path,true));else if(!found.enabled)throw new InvalidOperationException("Sample scene disabled in Build Settings: "+path);}
            EditorBuildSettings.scenes=scenes.ToArray();
            var catalog=new SampleCatalog{format=1,package=SampleContent.Package,version=SampleContent.Version,configVersion=SampleContent.Version,schema=GeneratedConfigInfo.Schema,files=SampleContent.Types.Select(pair=>new SampleCatalogFile{address=pair.Key,path=SampleContent.Root+pair.Key,sha256=pair.Value==typeof(TextAsset)?ConfigModule<object>.Hash(Resources.Load<TextAsset>(SampleContent.Root+pair.Key).bytes):""}).ToArray()};
            File.WriteAllText(ResourcesRoot+"firstplay.json",JsonUtility.ToJson(catalog,true)+"\n");AssetDatabase.Refresh();AssetDatabase.SaveAssets();Validate();
        }
        static void CopyConfig(string name)
        {
            var source="Assets/Game/ConfigData/"+name;var target=ResourcesRoot+"config/"+name;
            if(File.Exists(target)){if(!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)))throw new InvalidOperationException("Sample config stale: "+target);return;}
            if(!AssetDatabase.CopyAsset(source,target))throw new IOException("Config copy failed: "+name);
        }
        static void Panel<T>(string name,string[] buttons,string[] labels) where T:UIPanel
        {
            var path=ResourcesRoot+"ui/"+name+".prefab";
            if(!File.Exists(path))
            {
                var root=new GameObject(name,typeof(RectTransform),typeof(CanvasGroup),typeof(Image),typeof(T));
                try
                {
                    Stretch((RectTransform)root.transform);root.GetComponent<Image>().color=new Color(.05f,.09f,.14f,.96f);
                    Text(root.transform,"Title","MINIFRAMEWORK",new Vector2(0,200),new Vector2(700,70),38);
                    Text(root.transform,"Status","Loading local data...",new Vector2(0,105),new Vector2(740,80),22);
                    for(var i=0;i<buttons.Length;i++)Button(root.transform,buttons[i],labels[i],new Vector2(0,-10-i*85));
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var manifestPath=Root+"/Editor/"+name+".asset";
            var manifest=AssetDatabase.LoadAssetAtPath<UiBindingManifest>(manifestPath);
            if(!manifest){manifest=ScriptableObject.CreateInstance<UiBindingManifest>();AssetDatabase.CreateAsset(manifest,manifestPath);}
            manifest.Prefab=prefab;manifest.PanelScript=MonoScript.FromMonoBehaviour(prefab.GetComponent<T>());
            manifest.Fields=buttons.Select(b=>new UiBindingManifest.Entry{Name=b=="Start"?"StartButton":b+"Button",Target=prefab.transform.Find(b).GetComponent<UnityEngine.UI.Button>()}).Concat(new[]{new UiBindingManifest.Entry{Name="Status",Target=prefab.transform.Find("Status").GetComponent<UnityEngine.UI.Text>()}}).ToArray();
            EditorUtility.SetDirty(manifest);UiBindingGenerator.Generate(manifest);UiBindingGenerator.ValidateCompiled(manifest);UiPrefabBindings.BindPrefab(prefab);
        }
        static void Bootstrap()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);SceneManager.SetActiveScene(scene);
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color(.035f,.06f,.09f);camera.transform.position=new Vector3(0,0,-10);
            var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,640);scaler.matchWidthOrHeight=.5f;
            var safe=new GameObject("SafeArea",typeof(RectTransform));safe.transform.SetParent(canvas.transform,false);Stretch((RectTransform)safe.transform);
            var layer=new GameObject("Panels",typeof(RectTransform));layer.transform.SetParent(safe.transform,false);Stretch((RectTransform)layer.transform);
            var status=Text(safe.transform,"RuntimeStatus","Starting local framework...",new Vector2(0,-285),new Vector2(900,40),16);
            new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var boot=new GameObject("LocalSample",typeof(RuntimeHost),typeof(LocalSampleBootstrap));var component=boot.GetComponent<LocalSampleBootstrap>();component.LayerRoot=(RectTransform)layer.transform;component.SafeRoot=(RectTransform)safe.transform;component.Status=status;
            EditorSceneManager.SaveScene(scene,SampleContent.BootstrapScene);
        }
        static void Level(string path)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);SceneManager.SetActiveScene(scene);
            new GameObject("SessionDrivenActor",typeof(SampleLevelActor));EditorSceneManager.SaveScene(scene,path);
        }
        static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static Text Text(Transform parent,string name,string value,Vector2 position,Vector2 size,int fontSize)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;rect.sizeDelta=size;rect.anchoredPosition=position;
            var text=go.GetComponent<Text>();text.text=value;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;return text;
        }
        static void Button(Transform parent,string name,string label,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(380,64);rect.anchoredPosition=position;go.GetComponent<Image>().color=new Color(.12f,.38f,.52f);
            go.GetComponent<Button>().targetGraphic=go.GetComponent<Image>();Text(go.transform,"Label",label,Vector2.zero,new Vector2(370,60),24);
        }
        static void WriteWave(string path)
        {
            const int rate=22050,count=2205;using(var writer=new BinaryWriter(File.Create(path))){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);for(var i=0;i<count;i++)writer.Write((short)(Math.Sin(2*Math.PI*880*i/rate)*(1-i/(double)count)*5000));}
        }
        public static void Validate()
        {
            foreach(var guid in AssetDatabase.FindAssets("t:UiBindingManifest",new[]{Root+"/Editor"}))UiBindingGenerator.ValidateCompiled(AssetDatabase.LoadAssetAtPath<UiBindingManifest>(AssetDatabase.GUIDToAssetPath(guid)));
            var dependencies=AssetDatabase.GetDependencies(ResourcesRoot+"ui/home.prefab",true);if(dependencies.Any(p=>p.StartsWith(Root+"/Scenes/",StringComparison.Ordinal)||p.EndsWith("/level.prefab",StringComparison.Ordinal)))throw new InvalidOperationException("Home has level strong reference.");
            foreach(var pair in SampleContent.Types)if(!Resources.Load(SampleContent.Root+pair.Key,pair.Value))throw new InvalidOperationException("Missing first-play: "+pair.Key);
        }
        [MenuItem("Tools/MiniFramework/Local Sample/Open Bootstrap")]
        public static void OpenBootstrap(){Install();if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(SampleContent.BootstrapScene);}
        // Invoked only in a disposable clean project, never changes the user's target/settings.
        public static void BuildCleanPlayer()
        {
            Install();var output=Environment.GetEnvironmentVariable("MINI_TEMPLATE_PLAYER");if(string.IsNullOrWhiteSpace(output))throw new InvalidOperationException("MINI_TEMPLATE_PLAYER required");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{SampleContent.BootstrapScene,SampleContent.TutorialScene,SampleContent.SecondScene},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            File.WriteAllText(output+".report.json",JsonUtility.ToJson(new PlayerReport{passed=report.summary.result==BuildResult.Succeeded,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=report.summary.totalSize},true));
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Player build failed");
        }
        [Serializable] sealed class PlayerReport{public bool passed;public int errors,warnings;public ulong bytes;}
    }
}
