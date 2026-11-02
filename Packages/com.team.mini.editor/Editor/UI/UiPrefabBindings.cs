using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MiniFramework.UI.Editor
{
    public static class UiPrefabBindings
    {
        public static string PathTo(Transform root,Transform target)
        {
            var names=new List<string>();while(target!=root){if(target==null)throw new InvalidOperationException("ui.binding.outside_root");if(string.IsNullOrEmpty(target.name)||target.name.Contains("/"))throw new InvalidOperationException("ui.binding.path_name: "+target.name);names.Add(target.name);target=target.parent;}names.Reverse();return string.Join("/",names);
        }
        public static Component Resolve(Transform root,string path,Type type)
        {
            if(root==null||path==null||!typeof(Component).IsAssignableFrom(type))throw new InvalidOperationException("ui.binding.argument");
            var current=root;if(path.Length>0)foreach(var part in path.Split('/')){var matches=new List<Transform>();foreach(Transform child in current)if(child.name==part)matches.Add(child);if(part.Length==0||matches.Count!=1)throw new InvalidOperationException("ui.binding.path: "+path+" segment="+part);current=matches[0];}
            var components=current.GetComponents(type);if(components.Length!=1)throw new InvalidOperationException("ui.binding.component: "+path+" type="+type.FullName);return components[0];
        }
        sealed class Assignment {internal UIPanel Panel;internal FieldInfo Field;internal Component Value;}
        static List<Assignment> Plan(GameObject root)
        {
            if(root==null)throw new InvalidOperationException("ui.binding.root");
            var assignments=new List<Assignment>();
            foreach(var panel in root.GetComponentsInChildren<UIPanel>(true))
            {
                for(var type=panel.GetType();type!=typeof(UIPanel)&&type!=null;type=type.BaseType)
                foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                {var attr=field.GetCustomAttribute<UiBindingPathAttribute>();if(attr==null)continue;
                    var serialized=new SerializedObject(panel).FindProperty(field.Name);
                    if(serialized==null||serialized.propertyType!=SerializedPropertyType.ObjectReference)throw new InvalidOperationException("ui.binding.field: "+type.FullName+"."+field.Name);
                    assignments.Add(new Assignment{Panel=panel,Field=field,Value=Resolve(panel.transform,attr.Path,field.FieldType)});
                }
            }
            return assignments;
        }
        public static int BindRoot(GameObject root)
        {
            if(EditorUtility.IsPersistent(root))throw new InvalidOperationException("ui.binding.asset_use_bind_prefab");
            var plan=Plan(root);var changed=0;foreach(var item in plan){var so=new SerializedObject(item.Panel);var property=so.FindProperty(item.Field.Name);if(property.objectReferenceValue==item.Value)continue;Undo.RecordObject(item.Panel,"Bind UI fields");property.objectReferenceValue=item.Value;so.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(item.Panel);changed++;}return changed;
        }
        public static void ValidateRoot(GameObject root)
        {
            foreach(var item in Plan(root))if(new SerializedObject(item.Panel).FindProperty(item.Field.Name).objectReferenceValue!=item.Value)throw new InvalidOperationException("ui.binding.reference: "+item.Panel.GetType().FullName+"."+item.Field.Name);
        }
        public static int BindPrefab(GameObject prefab)
        {
            var path=AssetDatabase.GetAssetPath(prefab);if(string.IsNullOrEmpty(path)||PrefabUtility.GetPrefabAssetType(prefab)!=PrefabAssetType.Regular)throw new InvalidOperationException("ui.binding.prefab: regular prefab required");
            var root=PrefabUtility.LoadPrefabContents(path);
            try{if(root.GetComponentsInChildren<Transform>(true).Any(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))throw new InvalidOperationException("ui.binding.nested_prefab: edit its source explicitly");var changed=BindRoot(root);ValidateRoot(root);if(changed>0){PrefabUtility.SaveAsPrefabAsset(root,path,out var success);if(!success)throw new InvalidOperationException("ui.binding.save: "+path);}return changed;}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
