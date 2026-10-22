using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class ResourceThresholdAudit
    {
        [Serializable]
        public sealed class Limits
        {
            public long maxSourceBytes = 8 * 1024 * 1024;
            public int maxTextureDimension = 2048;
            public double maxAudioSeconds = 30;
            public int maxShaderCount = 32;
        }
        [Serializable]
        public sealed class Entry
        {
            public string path;
            public long sourceBytes = -1;
            public bool sourceBytesKnown;
            public int textureWidth;
            public int textureHeight;
            public double audioSeconds;
            public bool shader;
        }
        [Serializable]
        public sealed class Report
        {
            public string metrics = "Source file bytes, imported texture dimensions, imported audio duration and distinct shader dependencies; not Bundle/Player bytes or runtime memory.";
            public Limits limits;
            public List<Entry> assets = new List<Entry>();
            public List<FrameworkAudit.Issue> issues = new List<FrameworkAudit.Issue>();
            public int shaderCount;
        }

        public static Report Inspect(string[] assetPaths, Limits limits)
        {
            if (assetPaths == null || assetPaths.Length == 0) throw new ArgumentException("Select at least one asset.", nameof(assetPaths));
            if (limits == null) throw new ArgumentNullException(nameof(limits));
            if (limits.maxSourceBytes < 1 || limits.maxTextureDimension < 1 || limits.maxAudioSeconds <= 0 ||
                double.IsNaN(limits.maxAudioSeconds) || double.IsInfinity(limits.maxAudioSeconds) || limits.maxShaderCount < 0)
                throw new ArgumentException("Resource thresholds must be finite and non-negative; byte/dimension/duration budgets must be positive.", nameof(limits));
            var copy = new Limits
            {
                maxSourceBytes = limits.maxSourceBytes, maxTextureDimension = limits.maxTextureDimension,
                maxAudioSeconds = limits.maxAudioSeconds, maxShaderCount = limits.maxShaderCount
            };
            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in assetPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) ||
                    path.Contains("..") || path.Contains("\\") || AssetDatabase.IsValidFolder(path) || !AssetDatabase.LoadMainAssetAtPath(path))
                    throw new ArgumentException("Each input must be an imported Assets/Packages file, not a folder or traversal path.", nameof(assetPaths));
                requested.Add(path);
            }
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(new List<string>(requested).ToArray(), true), StringComparer.Ordinal);
            var sorted = new List<string>(dependencies);
            sorted.Sort(StringComparer.Ordinal);
            var report = new Report { limits = copy };
            foreach (string path in sorted)
            {
                var entry = new Entry { path = path };
                string physicalPath = path;
                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                    if (package != null) physicalPath = Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
                }
                if (File.Exists(physicalPath))
                {
                    entry.sourceBytes = new FileInfo(physicalPath).Length;
                    entry.sourceBytesKnown = true;
                    if (entry.sourceBytes > copy.maxSourceBytes) Add(report, path, "sourceBytes", "Source file exceeds byte threshold.");
                }
                else Add(report, path, "sourceBytes", "Source byte metric unavailable for this dependency; not assumed zero.");
                UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (UnityEngine.Object asset in objects)
                {
                    if (asset is Texture texture)
                    {
                        entry.textureWidth = Math.Max(entry.textureWidth, texture.width);
                        entry.textureHeight = Math.Max(entry.textureHeight, texture.height);
                    }
                    if (asset is AudioClip audio) entry.audioSeconds = Math.Max(entry.audioSeconds, audio.length);
                    if (asset is Shader) entry.shader = true;
                    if (asset is TMP_FontAsset font && (font.characterTable == null || font.characterTable.Count == 0))
                        Add(report, path, "fontCharacters", "TMP font has no characters in its current atlas; dynamic fallback is not proof of shipped glyph coverage.");
                }
                if (entry.textureWidth > copy.maxTextureDimension || entry.textureHeight > copy.maxTextureDimension)
                    Add(report, path, "textureDimensions", "Imported texture/atlas exceeds dimension threshold.");
                if (entry.audioSeconds > copy.maxAudioSeconds) Add(report, path, "audioSeconds", "Imported audio exceeds duration threshold.");
                if (entry.shader) report.shaderCount++;
                report.assets.Add(entry);
            }
            if (report.shaderCount > copy.maxShaderCount) Add(report, "<dependency-closure>", "shaderCount", "Distinct shader dependencies exceed count threshold; shader variant cost is not measured.");
            return report;
        }

        private static void Add(Report report, string path, string property, string reason)
        {
            report.issues.Add(new FrameworkAudit.Issue { path = path, property = property, reason = reason });
        }
    }
}
