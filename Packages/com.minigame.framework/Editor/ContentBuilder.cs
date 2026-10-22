using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace MiniGame.Framework.Editor
{
    public static class ContentBuilder
    {
        public static string Build(string packageName, string version, BuildTarget target, string outputRoot)
        {
            ValidateSegment(packageName, nameof(packageName));
            ValidateSegment(version, nameof(version));
            if (target == BuildTarget.NoTarget) throw new ArgumentException("An explicit build target is required.", nameof(target));
            if (string.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("Output directory required.", nameof(outputRoot));
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Content build requires an idle Editor outside Play Mode.");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string allowedRoot = Path.Combine(project, "Builds") + Path.DirectorySeparatorChar;
            string output = Path.GetFullPath(outputRoot);
            BuildPathGuard.RequireChild(allowedRoot, output);
            if (Directory.Exists(Path.Combine(output, target.ToString(), packageName, version)))
                throw new IOException("Version output already exists. Use a new immutable version or run directory.");
            var settings = AssetBundleCollectorSettingData.Setting;
            settings.CheckPackageConfigError(packageName);
            var parameters = new BuiltinBuildParameters
            {
                BuildOutputRoot = output,
                BuildinFileRoot = Path.Combine(output, "Builtin"),
                BuildPipeline = nameof(BuiltinBuildPipeline),
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = target,
                PackageName = packageName,
                PackageVersion = version,
                BuildinFileCopyOption = EBuildinFileCopyOption.None,
                ClearBuildCacheFiles = false
            };
            var result = new BuiltinBuildPipeline().Run(parameters, true);
            if (!result.Success) throw new InvalidOperationException(result.ErrorInfo);
            return result.OutputPackageDirectory;
        }

        private static void ValidateSegment(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 96 || value == "." || value == "..")
                throw new ArgumentException("A bounded package/version identifier is required.", name);
            foreach (char c in value)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
                    throw new ArgumentException("Package/version identifiers cannot contain path separators or special characters.", name);
        }
    }
}
