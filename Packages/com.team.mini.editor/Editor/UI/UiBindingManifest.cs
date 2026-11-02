using System;
using UnityEngine;
using UnityEditor;
namespace MiniFramework.UI.Editor
{
    [CreateAssetMenu(menuName="MiniFramework/UI Binding Manifest")]
    public sealed class UiBindingManifest:ScriptableObject
    {
        public GameObject Prefab;
        public MonoScript PanelScript;
        public Entry[] Fields=Array.Empty<Entry>();
        [Serializable] public sealed class Entry { public string Name; public Component Target; }
    }
}
