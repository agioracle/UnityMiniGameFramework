using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class ComponentBindingGenerator
    {
        [Serializable]
        public sealed class Binding
        {
            public string fieldName;
            public Component component;
        }
        private static readonly HashSet<string> Keywords = new HashSet<string>(
            ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias ascending async await by descending dynamic equals from get global group init into join let managed nameof not notnull on or orderby partial record remove required scoped select set unmanaged value var when where with yield").Split(' '), StringComparer.Ordinal);

        public static string GenerateSource(string namespaceName, string className, Binding[] bindings)
        {
            if (string.IsNullOrWhiteSpace(namespaceName)) throw new ArgumentException("Namespace required.", nameof(namespaceName));
            foreach (string part in namespaceName.Split('.')) ValidateIdentifier(part);
            ValidateIdentifier(className);
            ValidateBindings(bindings);
            if (className == "ValidateBindings") throw new ArgumentException("Class name cannot equal the generated validation method.", nameof(className));
            foreach (Binding binding in bindings)
                if (binding.fieldName == className) throw new ArgumentException("A binding field cannot have the same name as its declaring class.", nameof(className));
            var source = new StringBuilder();
            source.Append("namespace ").Append(namespaceName).Append("\n{\n    public sealed class ").Append(className).Append(" : global::UnityEngine.MonoBehaviour\n    {\n");
            foreach (Binding binding in bindings)
                source.Append("        [global::UnityEngine.SerializeField] private global::").Append(TypeName(binding.component.GetType())).Append(' ').Append(binding.fieldName).Append(";\n");
            source.Append("\n        public void ValidateBindings()\n        {\n");
            foreach (Binding binding in bindings)
                source.Append("            if (!").Append(binding.fieldName).Append(") throw new global::System.InvalidOperationException(\"Missing binding: ").Append(binding.fieldName).Append("\");\n");
            source.Append("        }\n    }\n}\n");
            return source.ToString();
        }

        public static void SaveNew(string assetPath, string namespaceName, string className, Binding[] bindings)
        {
            string source = GenerateSource(namespaceName, className, bindings);
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetPath.Contains("\\") || assetPath.Contains("..") || !assetPath.EndsWith("/" + className + ".cs", StringComparison.Ordinal))
                throw new ArgumentException("Output must be an Assets C# file with the generated class name.", nameof(assetPath));
            BuildPathGuard.RequireChild(Application.dataPath, Path.GetFullPath(assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            using (var stream = new FileStream(assetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(source);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        public static void Apply(Component target, Binding[] bindings)
        {
            if (!target) throw new ArgumentNullException(nameof(target));
            if (EditorUtility.IsPersistent(target)) throw new ArgumentException("Open Prefab Mode or use a scene instance; persistent assets are not modified directly.", nameof(target));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Bindings can only be written outside Play Mode.");
            ValidateBindings(bindings);
            using (var serialized = new SerializedObject(target))
            {
                foreach (Binding binding in bindings)
                {
                    FieldInfo field = target.GetType().GetField(binding.fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    SerializedProperty property = serialized.FindProperty(binding.fieldName);
                    if (field == null || !field.FieldType.IsInstanceOfType(binding.component) || property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                        throw new ArgumentException("Target does not have the expected serialized component field: " + binding.fieldName);
                    if (binding.component.transform != target.transform && !binding.component.transform.IsChildOf(target.transform))
                        throw new ArgumentException("Bindings must belong to the target hierarchy: " + binding.fieldName);
                }
                Undo.RecordObject(target, "Apply explicit component bindings");
                foreach (Binding binding in bindings) serialized.FindProperty(binding.fieldName).objectReferenceValue = binding.component;
                serialized.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private static void ValidateBindings(Binding[] bindings)
        {
            if (bindings == null || bindings.Length == 0 || bindings.Length > 128) throw new ArgumentException("Declare between 1 and 128 named bindings.", nameof(bindings));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Binding binding in bindings)
            {
                if (binding == null || !binding.component) throw new ArgumentException("Every binding requires a component.", nameof(bindings));
                ValidateIdentifier(binding.fieldName);
                if (binding.fieldName == "ValidateBindings" || typeof(MonoBehaviour).GetMember(binding.fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length != 0 || !names.Add(binding.fieldName))
                    throw new ArgumentException("Field names must be unique and cannot hide generated or inherited members: " + binding.fieldName);
                Type type = binding.component.GetType();
                if (!type.IsVisible || type.IsGenericType) throw new ArgumentException("Component type must be a visible, non-generic type.", nameof(bindings));
                TypeName(type);
            }
        }
        private static string TypeName(Type type)
        {
            string name = type.FullName?.Replace('+', '.');
            if (name == null) throw new ArgumentException("Component type must have a qualified name.");
            foreach (string part in name.Split('.')) ValidateIdentifier(part);
            return name;
        }
        private static void ValidateIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96 || Keywords.Contains(value) || !Initial(value[0]))
                throw new ArgumentException("Use a bounded C# identifier that is not a keyword.");
            for (int i = 1; i < value.Length; i++) if (!Initial(value[i]) && (value[i] < '0' || value[i] > '9')) throw new ArgumentException("Invalid C# identifier.");
        }
        private static bool Initial(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
