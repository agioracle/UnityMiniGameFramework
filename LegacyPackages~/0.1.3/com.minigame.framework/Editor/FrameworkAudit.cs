using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MiniGame.Framework.Reactive;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class FrameworkAudit
    {
        [Serializable]
        public sealed class Issue
        {
            public string path;
            public string property;
            public string reason;
        }
        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string generatedUtc;
            public List<Issue> issues;
            public string[] dependencies;
            public string[] publicApi;
        }

        public static List<Issue> InspectBindings(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var issues = new List<Issue>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in node.GetComponents<Component>())
                {
                    if (!component)
                    {
                        issues.Add(new Issue { path = ObjectPath(node), property = "script", reason = "Missing script" });
                        continue;
                    }
                    using (var serialized = new SerializedObject(component))
                    {
                        SerializedProperty property = serialized.GetIterator();
                        while (property.Next(true))
                        {
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                issues.Add(new Issue { path = ObjectPath(node), property = property.propertyPath, reason = "Broken object reference" });
                        }
                    }
                }
            }
            return issues;
        }

        public static string[] PublicApi()
        {
            var entries = new List<string>();
            Assembly[] assemblies = { typeof(TimerService).Assembly, typeof(ResourceService).Assembly, typeof(DisplayLifetime).Assembly };
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Assembly assembly in assemblies)
            foreach (Type type in assembly.GetExportedTypes())
            {
                entries.Add("type " + type.FullName + " : " + type.BaseType?.FullName);
                foreach (ConstructorInfo constructor in type.GetConstructors(flags)) entries.Add(type.FullName + " " + constructor);
                foreach (MethodInfo method in type.GetMethods(flags)) entries.Add(type.FullName + " " + method);
                foreach (PropertyInfo property in type.GetProperties(flags)) entries.Add(type.FullName + " property " + property);
                foreach (FieldInfo field in type.GetFields(flags)) entries.Add(type.FullName + " field " + field);
                foreach (EventInfo eventInfo in type.GetEvents(flags)) entries.Add(type.FullName + " event " + eventInfo);
            }
            entries.Sort(StringComparer.Ordinal);
            return entries.ToArray();
        }

        [MenuItem("MiniGame/Framework/Audit Selected Assets")]
        public static void AuditSelection()
        {
            if (Selection.objects.Length == 0) throw new InvalidOperationException("Select assets or hierarchy roots to inspect.");
            var issues = new List<Issue>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path)) paths.Add(path);
                if (selected is GameObject root) issues.AddRange(InspectBindings(root));
            }
            string[] dependencies = paths.Count == 0 ? Array.Empty<string>() : AssetDatabase.GetDependencies(new List<string>(paths).ToArray(), true);
            Array.Sort(dependencies, StringComparer.Ordinal);
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                issues = issues,
                dependencies = dependencies,
                publicApi = PublicApi()
            };
            const string directory = "Builds/Reports";
            string output = Path.Combine(directory, "framework-audit-" + Guid.NewGuid().ToString("N") + ".json");
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
            Directory.CreateDirectory(directory);
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[MiniGame] Audit written: " + output + "; issues: " + issues.Count);
        }

        [MenuItem("MiniGame/Framework/Export Public API Snapshot")]
        public static void ExportPublicApi()
        {
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                generatedUtc = DateTime.UtcNow.ToString("O"),
                issues = new List<Issue>(),
                dependencies = Array.Empty<string>(),
                publicApi = PublicApi()
            };
            string output = "Builds/Reports/framework-api-" + Guid.NewGuid().ToString("N") + ".json";
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[MiniGame] Public API snapshot written: " + output);
        }

        private static string ObjectPath(Transform node)
        {
            string path = node.name;
            while (node.parent) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }
    }
}
