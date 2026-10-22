using System;
using System.IO;
using MiniGame.Framework.Editor;
using UnityEditor;
using UnityEngine;

namespace MiniGame.ReleaseValidation.Editor
{
    public static class ReleaseValidationBuilder
    {
        public static void Build()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Application.isBatchMode || !File.Exists(Path.Combine(project, "release-validation.json")))
                throw new InvalidOperationException("Explicit release consumer workspace required.");
            string output = "Builds/Player/ReleaseConsumer.app";
            var result = PlayerBuilder.Build(new[] { ReleaseConsumer.SampleScene() }, BuildTarget.StandaloneOSX,
                output, BuildOptions.Development, new[] { "MINIGAME_RELEASE_SMOKE" });
            File.WriteAllText("Builds/player-build.json", JsonUtility.ToJson(new Receipt
            {
                output = Path.GetFullPath(output), result = result.summary.result.ToString(),
                bytes = result.summary.totalSize, backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString()
            }, true));
        }
        [Serializable]
        private sealed class Receipt { public string output; public string result; public ulong bytes; public string backend; }
    }
}
