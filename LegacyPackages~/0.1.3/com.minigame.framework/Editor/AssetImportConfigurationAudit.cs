using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class AssetImportConfigurationAudit
    {
        [Serializable]
        public sealed class Entry
        {
            public string path;
            public string importerKind;
            public string textureType;
            public int sourceWidth = -1;
            public int sourceHeight = -1;
            public bool textureReadable;
            public bool mipmapsEnabled;
            public string defaultTextureCompression;
            public string defaultTextureFormat;
            public int defaultMaxTextureSize;
            public string defaultAudioLoadType;
            public string defaultAudioCompression;
            public bool preloadAudioData;
            public bool loadAudioInBackground;
            public bool forceAudioToMono;
        }
        [Serializable]
        public sealed class Report
        {
            public string unityVersion;
            public List<Entry> assets = new List<Entry>();
            public List<FrameworkAudit.Issue> findings = new List<FrameworkAudit.Issue>();
            public string scope = "Read-only default importer configuration over the selected dependency closure. Findings are review prompts, not automatic errors or memory/performance measurements. Target-specific overrides, packed atlases and runtime behavior require separate verification.";
        }

        public static Report Inspect(string[] assetPaths)
        {
            if (assetPaths == null || assetPaths.Length == 0) throw new ArgumentException("At least one imported asset file is required.", nameof(assetPaths));
            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in assetPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) ||
                    path.Contains("..") || path.Contains("\\") || AssetDatabase.IsValidFolder(path) || !AssetDatabase.LoadMainAssetAtPath(path))
                    throw new ArgumentException("Inputs must be imported Assets/Packages files.", nameof(assetPaths));
                requested.Add(path);
            }
            var dependencies = new SortedSet<string>(AssetDatabase.GetDependencies(new List<string>(requested).ToArray(), true), StringComparer.Ordinal);
            var report = new Report { unityVersion = Application.unityVersion };
            foreach (string path in dependencies)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter texture)
                {
                    TextureImporterPlatformSettings defaults = texture.GetDefaultPlatformTextureSettings();
                    texture.GetSourceTextureWidthAndHeight(out int width, out int height);
                    report.assets.Add(new Entry
                    {
                        path = path, importerKind = "TextureImporter", textureType = texture.textureType.ToString(),
                        sourceWidth = width, sourceHeight = height, textureReadable = texture.isReadable,
                        mipmapsEnabled = texture.mipmapEnabled, defaultTextureCompression = defaults.textureCompression.ToString(),
                        defaultTextureFormat = defaults.format.ToString(), defaultMaxTextureSize = defaults.maxTextureSize
                    });
                    if (texture.isReadable) Add(report, path, "textureReadable", "CPU-readable texture may retain a CPU-side copy; verify a real readback/processing requirement before disabling it.");
                    if (texture.textureType == TextureImporterType.Sprite && texture.mipmapEnabled)
                        Add(report, path, "spriteMipmaps", "Sprite mipmaps are enabled; verify scaling/quality requirements for the intended UI or world-space usage.");
                    if (defaults.textureCompression == TextureImporterCompression.Uncompressed)
                        Add(report, path, "textureCompression", "Default texture import is uncompressed; target format and visual quality must be reviewed separately.");
                }
                else if (importer is AudioImporter audio)
                {
                    AudioImporterSampleSettings defaults = audio.defaultSampleSettings;
                    report.assets.Add(new Entry
                    {
                        path = path, importerKind = "AudioImporter", defaultAudioLoadType = defaults.loadType.ToString(),
                        defaultAudioCompression = defaults.compressionFormat.ToString(), preloadAudioData = defaults.preloadAudioData,
                        loadAudioInBackground = audio.loadInBackground, forceAudioToMono = audio.forceToMono
                    });
                    if (defaults.loadType == AudioClipLoadType.DecompressOnLoad && defaults.preloadAudioData)
                        Add(report, path, "audioPreload", "Audio is preloaded and decompressed on load; verify residency and clip duration. This can be intentional for short low-latency effects.");
                    if (defaults.compressionFormat == AudioCompressionFormat.PCM)
                        Add(report, path, "audioCompression", "Default audio format is PCM; review size/latency/quality tradeoffs on the actual target.");
                }
            }
            return report;
        }

        [MenuItem("MiniGame/Framework/Audit Default Import Configuration")]
        public static void AuditSelection()
        {
            var paths = new string[Selection.objects.Length];
            for (int i = 0; i < paths.Length; i++) paths[i] = AssetDatabase.GetAssetPath(Selection.objects[i]);
            Report report = Inspect(paths);
            string output = "Builds/Reports/import-configuration-" + Guid.NewGuid().ToString("N") + ".json";
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
            Debug.Log("[MiniGame] Default importer configuration report written: " + output);
        }

        private static void Add(Report report, string path, string property, string reason)
            => report.findings.Add(new FrameworkAudit.Issue { path = path, property = property, reason = reason });
    }
}
