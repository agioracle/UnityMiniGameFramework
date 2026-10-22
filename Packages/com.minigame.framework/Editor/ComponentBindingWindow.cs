using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public sealed class ComponentBindingWindow : EditorWindow
    {
        [SerializeField] private string namespaceName = "Game.Presentation";
        [SerializeField] private string className = "GeneratedViewBindings";
        [SerializeField] private Component target;
        [SerializeField] private List<ComponentBindingGenerator.Binding> bindings = new List<ComponentBindingGenerator.Binding>();
        private string preview;
        private string message;
        private Vector2 scroll;

        [MenuItem("MiniGame/Framework/Generate Explicit Component Bindings")]
        public static void Open() => GetWindow<ComponentBindingWindow>("Component Bindings");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Declare each field and component explicitly. Source generation creates a new script only; apply after compilation to a component on the same hierarchy. No runtime searching, automatic attachment or asset saving.", MessageType.Info);
            namespaceName = EditorGUILayout.TextField("Namespace", namespaceName);
            className = EditorGUILayout.TextField("New class", className);
            target = (Component)EditorGUILayout.ObjectField("Apply to component", target, typeof(Component), true);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < bindings.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                bindings[i].fieldName = EditorGUILayout.TextField(bindings[i].fieldName);
                bindings[i].component = (Component)EditorGUILayout.ObjectField(bindings[i].component, typeof(Component), true);
                if (GUILayout.Button("Remove", GUILayout.Width(65))) { bindings.RemoveAt(i); i--; }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Add explicit field")) bindings.Add(new ComponentBindingGenerator.Binding { fieldName = "component" + bindings.Count });
            if (GUILayout.Button("Preview source")) Run(() => preview = ComponentBindingGenerator.GenerateSource(namespaceName, className, bindings.ToArray()));
            if (GUILayout.Button("Save new script")) Run(() =>
            {
                string path = EditorUtility.SaveFilePanelInProject("Save new bindings script", className, "cs", "Existing files are never overwritten.");
                if (string.IsNullOrEmpty(path)) return;
                ComponentBindingGenerator.SaveNew(path, namespaceName, className, bindings.ToArray());
                message = "Created " + path + ". After compilation attach the script explicitly, then apply bindings. Ensure its assembly references the declared component types.";
            });
            if (GUILayout.Button("Apply declared bindings (Undo supported)")) Run(() =>
            {
                ComponentBindingGenerator.Apply(target, bindings.ToArray());
                message = "Bindings applied. Save the scene or Prefab explicitly when ready.";
            });
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            if (!string.IsNullOrEmpty(preview)) EditorGUILayout.TextArea(preview, GUILayout.Height(180));
        }
        private void Run(Action action)
        {
            try { action(); }
            catch (Exception error) { message = error.Message; }
        }
    }
}
