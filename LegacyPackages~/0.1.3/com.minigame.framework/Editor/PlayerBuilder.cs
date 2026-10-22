using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class PlayerBuilder
    {
        public static BuildReport Build(string[] scenes, BuildTarget target, string outputPath, BuildOptions options = BuildOptions.None,
            string[] extraScriptingDefines = null)
        {
            if (scenes == null || scenes.Length == 0) throw new ArgumentException("Explicit build scenes are required.", nameof(scenes));
            if (target == BuildTarget.NoTarget) throw new ArgumentException("An explicit target is required.", nameof(target));
            if (!UnityEditor.BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("The requested build target module is not installed.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Player build requires an idle Editor outside Play Mode.");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path required.", nameof(outputPath));
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string builds = Path.Combine(project, "Builds") + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(outputPath);
            BuildPathGuard.RequireChild(builds, destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Player output exists; use a new run directory.");
            string[] copy = ValidateScenes(scenes);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Player build cancelled while saving scenes.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = copy, target = target, locationPathName = destination, options = options,
                extraScriptingDefines = extraScriptingDefines == null ? Array.Empty<string>() : (string[])extraScriptingDefines.Clone()
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Player build failed: " + report.summary.result + "; errors: " + report.summary.totalErrors);
            return report;
        }

        public static string[] ValidateScenes(string[] scenes)
        {
            if (scenes == null || scenes.Length == 0) throw new ArgumentException("Explicit build scenes are required.", nameof(scenes));
            var unique = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            string[] copy = (string[])scenes.Clone();
            foreach (string scene in copy)
            {
                if (string.IsNullOrWhiteSpace(scene) || !scene.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !scene.EndsWith(".unity", StringComparison.Ordinal) || scene.Contains("\\") ||
                    Array.Exists(scene.Split('/'), segment => segment == ".." || segment == "." || segment.Length == 0))
                    throw new ArgumentException("Scenes must be explicit normalized Assets paths.", nameof(scenes));
                BuildPathGuard.RequireChild(Application.dataPath, Path.GetFullPath(scene));
                if (!File.Exists(scene) || !AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) || !unique.Add(scene))
                    throw new ArgumentException("Each build scene must be a unique imported SceneAsset.", nameof(scenes));
            }
            return copy;
        }
    }
}
