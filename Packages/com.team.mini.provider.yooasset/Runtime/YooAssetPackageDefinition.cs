using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using YooAsset;
using Object = UnityEngine.Object;

namespace MiniFramework.Providers.YooAsset
{
    // Trusted build configuration, not metadata inferred from an untrusted CDN response.
    public sealed class YooAssetFirstPlayAsset
    {
        public string Address { get; }
        public Type AssetType { get; }
        public Func<Object, Result> Validate { get; }
        public YooAssetFirstPlayAsset(string address, Type assetType, Func<Object, Result> validate = null)
        {
            if (string.IsNullOrWhiteSpace(address) || assetType == null || !typeof(Object).IsAssignableFrom(assetType))
                throw new ArgumentException("An address and Unity asset type are required.");
            Address = address; AssetType = assetType; Validate = validate;
        }
    }

    public sealed class YooAssetPackageDefinition
    {
        public string Package { get; }
        public PackageCandidate Candidate { get; }
        public string ConfigVersion { get; }
        public string Schema { get; }
        public IReadOnlyList<string> CompatiblePrograms { get; }
        public IReadOnlyList<YooAssetFirstPlayAsset> FirstPlay { get; }
        public Func<InitializePackageOptions> CreateOptions { get; }
        public int ManifestTimeoutSeconds { get; }
        public YooAssetPackageDefinition(string package, PackageCandidate candidate, string configVersion, string schema,
            IEnumerable<string> compatiblePrograms, IEnumerable<YooAssetFirstPlayAsset> firstPlay,
            Func<InitializePackageOptions> createOptions, int manifestTimeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(package) || candidate == null || string.IsNullOrWhiteSpace(configVersion) ||
                string.IsNullOrWhiteSpace(schema) || compatiblePrograms == null || firstPlay == null || createOptions == null || manifestTimeoutSeconds <= 0)
                throw new ArgumentException("Pinned package identity, compatibility and options are required.");
            // Cache fallback requires an independently verified local cache adapter. Never label a network FS as offline.
            if (candidate.Source == PackageSource.CompatibleCache) throw new ArgumentException("CompatibleCache is not supported by this backend.");
            var programs = new List<string>(compatiblePrograms); var assets = new List<YooAssetFirstPlayAsset>(firstPlay);
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            if (programs.Count == 0 || programs.Exists(string.IsNullOrWhiteSpace) || new HashSet<string>(programs, StringComparer.Ordinal).Count != programs.Count ||
                assets.Count == 0 || assets.Exists(a => a == null || !addresses.Add(a.Address)))
                throw new ArgumentException("Unique compatibility values and first-play assets are required.");
            Package = package; Candidate = candidate; ConfigVersion = configVersion; Schema = schema;
            CompatiblePrograms = programs.AsReadOnly(); FirstPlay = assets.AsReadOnly();
            CreateOptions = createOptions; ManifestTimeoutSeconds = manifestTimeoutSeconds;
        }
        internal bool Matches(PackageCandidate c) => c.Source == Candidate.Source && c.Version == Candidate.Version && c.Location == Candidate.Location;
    }
}
