using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
namespace MiniFramework.UI.Editor
{
    [CustomEditor(typeof(UiBindingManifest))]
    public sealed class UiBindingInspector:UnityEditor.Editor
    {
        string message;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var manifest=(UiBindingManifest)target;
            if(GUILayout.Button("1. Validate / preview generated source"))Run(()=>message=UiBindingGenerator.Render(manifest));
            using(new EditorGUI.DisabledScope(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if(GUILayout.Button("2. Generate fields (then wait for compilation)"))Run(()=>message=UiBindingGenerator.Generate(manifest)?"Generated; compile before binding.":"Already current.");
                if(GUILayout.Button("3. Bind prefab and save"))Run(()=>{UiBindingGenerator.ValidateCompiled(manifest);message="Assigned fields: "+UiPrefabBindings.BindPrefab(manifest.Prefab);});
                if(GUILayout.Button("Validate prefab references"))Run(()=>{UiPrefabBindings.ValidateRoot(manifest.Prefab);message="References valid.";});
            }
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        void Run(Action action){try{action();}catch(Exception error){message=error.Message;}}
    }
    public sealed class UiBindingBuildCheck:IPreprocessBuildWithReport,IProcessSceneWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report){ValidateAllPrefabs();}
        public static void ValidateAllPrefabs()
        {
            foreach(var manifestId in AssetDatabase.FindAssets("t:UiBindingManifest",new[]{"Assets"}))
            {var manifestPath=AssetDatabase.GUIDToAssetPath(manifestId);try{UiBindingGenerator.ValidateCompiled(AssetDatabase.LoadAssetAtPath<UiBindingManifest>(manifestPath));}catch(Exception e){throw new BuildFailedException(manifestPath+": "+e.Message);}}
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
            {var path=AssetDatabase.GUIDToAssetPath(guid);try{UiPrefabBindings.ValidateRoot(AssetDatabase.LoadAssetAtPath<GameObject>(path));}catch(Exception e){throw new BuildFailedException(path+": "+e.Message);}}
        }
        public void OnProcessScene(Scene scene,BuildReport report)
        {if(report==null)return;foreach(var root in scene.GetRootGameObjects())try{UiPrefabBindings.ValidateRoot(root);}catch(Exception e){throw new BuildFailedException(scene.path+": "+e.Message);}}
    }
}
