using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public sealed class ResourceAuditWindow : EditorWindow
    {
        [SerializeField] private ResourceThresholdAudit.Limits limits = new ResourceThresholdAudit.Limits();
        private ResourceThresholdAudit.Report report;
        private string message;
        private Vector2 scroll;

        [MenuItem("MiniGame/Framework/Audit Resource Thresholds")]
        public static void Open() => GetWindow<ResourceAuditWindow>("Resource Thresholds");
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Thresholds are editable project choices, not certified performance budgets. Select imported asset files. This reads their dependency closure without modifying importers.", MessageType.Info);
            limits.maxSourceBytes = EditorGUILayout.LongField("Source file bytes", limits.maxSourceBytes);
            limits.maxTextureDimension = EditorGUILayout.IntField("Texture/atlas dimension", limits.maxTextureDimension);
            limits.maxAudioSeconds = EditorGUILayout.DoubleField("Audio duration (seconds)", limits.maxAudioSeconds);
            limits.maxShaderCount = EditorGUILayout.IntField("Distinct shader count", limits.maxShaderCount);
            if (GUILayout.Button("Inspect selected asset files"))
            {
                try
                {
                    string[] paths = new string[Selection.objects.Length];
                    for (int i = 0; i < paths.Length; i++) paths[i] = AssetDatabase.GetAssetPath(Selection.objects[i]);
                    report = ResourceThresholdAudit.Inspect(paths, limits);
                    message = "Dependencies: " + report.assets.Count + "; findings: " + report.issues.Count;
                }
                catch (Exception error) { report = null; message = error.Message; }
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            if (report == null) return;
            if (GUILayout.Button("Export inspected report to Builds"))
            {
                try
                {
                    string output = "Builds/Reports/resource-thresholds-" + Guid.NewGuid().ToString("N") + ".json";
                    BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    File.WriteAllText(output, JsonUtility.ToJson(report, true));
                    message = "Report written: " + output;
                }
                catch (Exception error) { message = error.Message; }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (FrameworkAudit.Issue issue in report.issues) EditorGUILayout.LabelField(issue.path + " / " + issue.property + " / " + issue.reason, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
