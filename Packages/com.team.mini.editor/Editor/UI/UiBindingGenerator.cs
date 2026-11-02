using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MiniFramework.EditorFoundation;
using UnityEditor;
using UnityEngine;
namespace MiniFramework.UI.Editor
{
    public static class UiBindingGenerator
    {
        public static string Marker(Type type)=>"// <mini-ui-generated schema=1 type="+type.FullName+">";
        public static Type PanelType(UiBindingManifest manifest)
        {
            if(manifest==null||manifest.Prefab==null||manifest.PanelScript==null)throw new InvalidOperationException("ui.generate.manifest: prefab/script required");
            var type=manifest.PanelScript.GetClass();
            if(type==null||!typeof(UIPanel).IsAssignableFrom(type)||!type.IsPublic||type.IsNested||type.IsGenericType||type.IsAbstract)throw new InvalidOperationException("ui.generate.panel_type");
            if(!GeneratedSource.Identifier(type.Name)||!GeneratedSource.Namespace(type.Namespace))throw new InvalidOperationException("ui.generate.namespace: "+type.FullName);
            var path=AssetDatabase.GetAssetPath(manifest.PanelScript);
            if(!path.StartsWith("Assets/",StringComparison.Ordinal)||Path.GetFileNameWithoutExtension(path)!=type.Name||!Regex.IsMatch(File.ReadAllText(path),@"\bpartial\s+class\s+"+Regex.Escape(type.Name)+@"\b"))throw new InvalidOperationException("ui.generate.partial: public top-level partial class in Assets required");
            if(!EditorUtility.IsPersistent(manifest.Prefab)||PrefabUtility.GetPrefabAssetType(manifest.Prefab)!=PrefabAssetType.Regular||manifest.Prefab.transform.parent!=null)throw new InvalidOperationException("ui.generate.prefab: regular root prefab required");
            if(manifest.Prefab.GetComponents(type).Length!=1)throw new InvalidOperationException("ui.generate.panel_component");
            return type;
        }
        public static string OutputPath(UiBindingManifest manifest)=>Path.ChangeExtension(AssetDatabase.GetAssetPath(manifest.PanelScript),"Bindings.g.cs");
        public static string Render(UiBindingManifest manifest)
        {
            var type=PanelType(manifest);var names=new System.Collections.Generic.HashSet<string>();var lines=new StringBuilder();
            lines.Append(Marker(type)).Append("\nnamespace ").Append(type.Namespace).Append("\n{\n    public partial class ").Append(type.Name).Append(" : global::MiniFramework.UI.IUiGeneratedBindings\n    {\n");
            if(manifest.Fields==null)throw new InvalidOperationException("ui.generate.fields");
            foreach(var entry in manifest.Fields.OrderBy(e=>e?.Name,StringComparer.Ordinal))
            {
                if(entry==null||!GeneratedSource.Identifier(entry.Name)||!names.Add(entry.Name)||entry.Name.StartsWith("__ui_",StringComparison.Ordinal)||entry.Name=="ValidateGeneratedBindings"||entry.Name==type.Name)throw new InvalidOperationException("ui.generate.field: "+entry?.Name);
                var member=type.GetMember(entry.Name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).FirstOrDefault();
                var field=type.GetField("__ui_"+entry.Name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if((member!=null||field!=null)&&(field==null||field.GetCustomAttribute<UiBindingPathAttribute>()==null))throw new InvalidOperationException("ui.generate.member_conflict: "+entry.Name);
                if(entry.Target==null)throw new InvalidOperationException("ui.generate.missing_reference: "+entry.Name);
                var targetType=entry.Target.GetType();if(!targetType.IsPublic||targetType.IsNested||targetType.IsGenericType||targetType.FullName==null)throw new InvalidOperationException("ui.generate.component_type: "+entry.Name);
                if(targetType.Assembly!=type.Assembly&&!targetType.Assembly.GetName().Name.StartsWith("UnityEngine",StringComparison.Ordinal))throw new InvalidOperationException("ui.generate.component_assembly: "+entry.Name);
                var consumer=UnityEditor.Compilation.CompilationPipeline.GetAssemblies().FirstOrDefault(a=>a.name==type.Assembly.GetName().Name);
                if(targetType.Assembly!=type.Assembly&&(consumer==null||!consumer.compiledAssemblyReferences.Any(r=>Path.GetFileNameWithoutExtension(r)==targetType.Assembly.GetName().Name)&&!consumer.assemblyReferences.Any(r=>r.name==targetType.Assembly.GetName().Name)))throw new InvalidOperationException("ui.generate.missing_assembly_reference: "+targetType.Assembly.GetName().Name);
                var relative=UiPrefabBindings.PathTo(manifest.Prefab.transform,entry.Target.transform);
                var resolved=UiPrefabBindings.Resolve(manifest.Prefab.transform,relative,targetType);if(resolved!=entry.Target)throw new InvalidOperationException("ui.generate.ambiguous_component: "+entry.Name);
                lines.Append("        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath(\"").Append(Escape(relative)).Append("\")]\n        private global::").Append(targetType.FullName).Append(" __ui_").Append(entry.Name).Append(";\n        public global::").Append(targetType.FullName).Append(' ').Append(entry.Name).Append(" => __ui_").Append(entry.Name).Append(";\n");
            }
            lines.Append("        public global::MiniFramework.Contracts.Result ValidateGeneratedBindings()\n        {\n");
            foreach(var entry in manifest.Fields.OrderBy(e=>e.Name,StringComparer.Ordinal))lines.Append("            if (__ui_").Append(entry.Name).Append(" == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, \"ui.binding.missing\", \"").Append(type.FullName).Append('.').Append(entry.Name).Append("\"));\n");
            lines.Append("            return global::MiniFramework.Contracts.Result.Success();\n        }\n    }\n}\n");return lines.ToString();
        }
        public static void ValidateCompiled(UiBindingManifest manifest)
        {
            var content=Render(manifest);var path=OutputPath(manifest);
            if(!File.Exists(path)||File.ReadAllText(path)!=content)throw new InvalidOperationException("ui.binding.stale_generation: "+path);
            var type=manifest.PanelScript.GetClass();
            var fields=type.GetFields(BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(f=>f.GetCustomAttribute<UiBindingPathAttribute>()!=null).ToArray();
            if(fields.Length!=manifest.Fields.Length)throw new InvalidOperationException("ui.binding.compile_required: "+type.FullName);
            foreach(var entry in manifest.Fields)
            {
                var field=fields.FirstOrDefault(f=>f.Name=="__ui_"+entry.Name);
                if(field==null||field.FieldType!=entry.Target.GetType()||field.GetCustomAttribute<UiBindingPathAttribute>().Path!=UiPrefabBindings.PathTo(manifest.Prefab.transform,entry.Target.transform))throw new InvalidOperationException("ui.binding.compile_required: "+entry.Name);
            }
        }
        public static bool Generate(UiBindingManifest manifest)
        {var content=Render(manifest);var changed=GeneratedSource.Write(OutputPath(manifest),content,Marker(manifest.PanelScript.GetClass()));if(changed)AssetDatabase.ImportAsset(OutputPath(manifest));return changed;}
        static string Escape(string value)=>value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");
    }
}
