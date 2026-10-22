using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGame.Framework.Editor
{
    public static class SceneConfigurationAudit
    {
        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string generatedUtc;
            public string scenePath;
            public List<FrameworkAudit.Issue> issues;
            public string scope = "Read-only loaded scene configuration and references; not a build, gameplay, performance or target-device validation.";
        }

        public static List<FrameworkAudit.Issue> Inspect(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded scene is required.", nameof(scene));
            var issues = new List<FrameworkAudit.Issue>();
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots.Length == 0) Add(issues, scene.name, "roots", "Scene has no root objects; verify the intended entry point.");
            int listeners = 0, eventSystems = 0;
            var interactiveCanvases = new List<Canvas>();
            foreach (GameObject root in roots)
            {
                issues.AddRange(FrameworkAudit.InspectBindings(root));
                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                    if (listener.isActiveAndEnabled) listeners++;
                foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                    if (eventSystem.isActiveAndEnabled) eventSystems++;
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (!canvas.enabled || !canvas.gameObject.activeInHierarchy) continue;
                    using (var serialized = new SerializedObject(canvas))
                    {
                        SerializedProperty mode = serialized.FindProperty("m_RenderMode");
                        SerializedProperty camera = serialized.FindProperty("m_Camera");
                        if (mode == null || camera == null)
                            Add(issues, ObjectPath(canvas.transform), "canvasSchema", "Canvas serialized configuration is unavailable for this Unity version.");
                        else if (mode.intValue == (int)RenderMode.ScreenSpaceCamera && !camera.objectReferenceValue)
                            Add(issues, ObjectPath(canvas.transform), "worldCamera", "Screen-space camera canvas has no assigned camera.");
                    }
                    var raycaster = canvas.GetComponent<GraphicRaycaster>();
                    if (raycaster && raycaster.isActiveAndEnabled) interactiveCanvases.Add(canvas);
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler && scaler.isActiveAndEnabled && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                        (!PositiveFinite(scaler.referenceResolution.x) || !PositiveFinite(scaler.referenceResolution.y)))
                        Add(issues, ObjectPath(canvas.transform), "referenceResolution", "Scaled canvas requires a finite positive reference resolution.");
                }
            }
            if (listeners > 1) Add(issues, scene.name, "audioListeners", "Multiple enabled AudioListeners in this scene.");
            if (eventSystems > 1) Add(issues, scene.name, "eventSystems", "Multiple enabled EventSystems in this scene.");
            if (eventSystems == 0)
                foreach (Canvas canvas in interactiveCanvases)
                    Add(issues, ObjectPath(canvas.transform), "eventSystem", "Interactive canvas has no enabled EventSystem in this scene; verify an explicitly shared additive-scene input owner if intentional.");
            if (EditorSceneManager.DetectCrossSceneReferences(scene))
                Add(issues, scene.name, "crossSceneReferences", "Scene contains cross-scene object references; verify serialization and explicit lifetime ownership.");
            return issues;
        }

        [MenuItem("MiniGame/Framework/Audit Active Scene Configuration")]
        public static void AuditActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            var report = new Report
            {
                unityVersion = Application.unityVersion, generatedUtc = DateTime.UtcNow.ToString("O"),
                scenePath = scene.path, issues = Inspect(scene)
            };
            string output = "Builds/Reports/scene-configuration-" + Guid.NewGuid().ToString("N") + ".json";
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[MiniGame] Scene configuration report written: " + output + "; findings: " + report.issues.Count);
        }

        private static bool PositiveFinite(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Add(List<FrameworkAudit.Issue> issues, string path, string property, string reason)
            => issues.Add(new FrameworkAudit.Issue { path = path, property = property, reason = reason });
        private static string ObjectPath(Transform node)
        {
            string path = node.name;
            while (node.parent) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }
    }
}
