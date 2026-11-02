using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Providers.YooAsset;
using UnityEngine;
using UnityEngine.Scripting;
using YooAsset;
using Manifest = MiniFramework.Contracts.PackageManifest;

namespace MiniFramework.S407RealBundle
{
    [Serializable, Preserve] public sealed class BundleCase
    {
        public string name, error = "", state = "", text = "", version = "";
        public bool passed, readyBeforePrepare, versionFrozen, bundleObserved, packageRemoved;
        public int consumers, leases, loadStarts, preparedAssets, providerHandles, assetEntries, modules, sessions, packages, bundlesBefore, bundlesAfter;
        public List<string> order = new List<string>();
    }
    // Validation-only pinned local package, not a production remote manifest or filesystem adapter.
    public sealed class RealBundleScenario
    {
        public const string Build = "s407-real-bundle-csharp-v1-20261031", Package = "S407RealBundle", Version = "bundle-v1", Address = "real-bundle";
        public const string Text = "S4-07 real AssetBundle / frozen content v1";
        public static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        static OperationContext Wait => new OperationContext(default, TimeSpan.FromSeconds(60));
        public static void Require(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
        static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Integrity) => new FrameworkError(kind, "fixture.bundle." + code);
        static ResourceKey Key(string version = Version) => new ResourceKey("yooasset", Package, version, Address, typeof(TextAsset));
        readonly Func<InitializePackageOptions> options;
        public RealBundleScenario(Func<InitializePackageOptions> options) { this.options = options; }
        public readonly List<BundleCase> Cases = new List<BundleCase>();
        sealed class Backend : IPackageBackend
        {
            readonly RealBundleScenario owner; readonly BundleCase row; readonly string config, required, expected;
            public ResourcePackage Package; public YooAssetProvider Provider; public int Sessions;
            public Backend(RealBundleScenario owner, BundleCase row, string config, string required, string expected)
            { this.owner = owner; this.row = row; this.config = config; this.required = required; this.expected = expected; }
            public IPackageSession Create(PackageProfile p, PackageCandidate c) { Sessions++; return new Session(this); }
            sealed class Session : IPackageSession
            {
                readonly Backend b; bool closed;
                public Session(Backend b) { this.b = b; }
                public async UniTask<Result<Manifest>> PrepareAsync()
                {
                    b.row.order.Add("prepare");
                    // Resource creation only begins after cold Create has transferred ownership.
                    b.Package = YooAssets.CreatePackage(RealBundleScenario.Package);
                    var init = b.Package.InitializePackageAsync(b.owner.options());
                    while (!init.IsDone) await UniTask.Yield();
                    if (init.Status != EOperationStatus.Succeeded) return Result<Manifest>.Failure(Error("initialize", ErrorKind.Provider));
                    var manifest = b.Package.LoadPackageManifestAsync(new LoadPackageManifestOptions(Version, 30));
                    while (!manifest.IsDone) await UniTask.Yield();
                    if (manifest.Status != EOperationStatus.Succeeded) return Result<Manifest>.Failure(Error("manifest", ErrorKind.Provider));
                    b.Provider = new YooAssetProvider(b.Package, Version);
                    // WebServer FS reports no downloads required. Actual async load is the first-play proof.
                    if (!b.Package.GetAssetInfo(b.required, typeof(TextAsset)).IsValid)
                        return Result<Manifest>.Failure(Error("first_play.missing", ErrorKind.Unavailable));
                    var started = b.Provider.BeginLoad<TextAsset>(Key());
                    if (!started.IsSuccess) return Result<Manifest>.Failure(started.Error);
                    var loaded = await started.Value.Completion;
                    if (!loaded.IsSuccess) return Result<Manifest>.Failure(loaded.Error);
                    var asset = loaded.Value; bool valid;
                    try
                    {
                        valid = asset.Value != null && asset.Value.text.Trim() == b.expected;
                        b.row.text = asset.Value == null ? "" : asset.Value.text.Trim();
                        b.row.bundleObserved = AssetBundle.GetAllLoadedAssetBundles().Count() > b.row.bundlesBefore;
                        b.row.preparedAssets++;
                    }
                    finally
                    {
                        var released = await asset.BeginRelease(Cleanup).Value.Completion;
                        Require(released.IsSuccess, "prepare.release");
                    }
                    if (!valid) return Result<Manifest>.Failure(Error("payload"));
                    b.row.order.Add("first_play_loaded"); b.row.version = b.Package.GetPackageVersion();
                    return Result<Manifest>.Success(new Manifest(RealBundleScenario.Package, b.row.version, b.config, "bundle-schema-v1", true, new[] { "fixture-app-v1" }, new[] { b.required }));
                }
                public async UniTask<Result> CleanupAsync()
                {
                    if (closed) return Result.Success();
                    b.row.order.Add("package_cleanup");
                    if (b.Provider != null && b.Provider.RetainedHandleCount != 0) return Result.Failure(Error("handles.retained", ErrorKind.CleanupFailed));
                    if (b.Package != null)
                    {
                        var destroy = b.Package.DestroyPackageAsync(); while (!destroy.IsDone) await UniTask.Yield();
                        if (destroy.Status != EOperationStatus.Succeeded) return Result.Failure(Error("destroy", ErrorKind.CleanupFailed));
                        YooAssets.RemovePackage(b.Package.PackageName); b.Package = null;
                    }
                    closed = true; b.Sessions--; b.row.packageRemoved = !YooAssets.TryGetPackage(RealBundleScenario.Package, out _);
                    return Result.Success();
                }
            }
        }
        // Defers existing production provider binding until package readiness. No second resource cache.
        sealed class ProviderModule : IModule, IAssetProvider
        {
            readonly Backend b; public ProviderModule(Backend b) { this.b = b; }
            public string Id => "yooasset"; public IReadOnlyList<string> Dependencies => new[] { "package" };
            public UniTask<Result> InitializeAsync(IModuleContext c, OperationContext w) => b.Provider.InitializeAsync(c, w);
            public UniTask<Result> StopAsync(CleanupContext c) => b.Provider.StopAsync(c);
            public UniTask<Result> DisposeAsync(CleanupContext c) => b.Provider.DisposeAsync(c);
            public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey k) where T : class => b.Provider.BeginLoad<T>(k);
        }
        sealed class Consumer : IModule
        {
            readonly AssetsService assets; readonly PackageStartup package; readonly BundleCase row;
            public Consumer(AssetsService assets, PackageStartup package, BundleCase row) { this.assets = assets; this.package = package; this.row = row; }
            public string Id => "bundle-consumer"; public IReadOnlyList<string> Dependencies => new[] { "assets", "package" };
            public async UniTask<Result> InitializeAsync(IModuleContext c, OperationContext w)
            {
                Require(package.CheckReady(Package, Version).IsSuccess, "consumer.not_ready"); row.order.Add("consumer"); row.consumers++;
                row.versionFrozen = !package.CheckReady(Package, "bundle-v2").IsSuccess;
                var scope = new Scope("bundle-consumer");
                try
                {
                    var waits = new List<UniTask<Result<IAssetLease<TextAsset>>>>();
                    for (int i = 0; i < 20; i++) waits.Add(assets.Load<TextAsset>(Key(), scope).Value.WaitAsync(w));
                    var ids = new HashSet<string>();
                    foreach (var wait in waits)
                    {
                        var r = await wait; Require(r.IsSuccess && r.Value.Value.text.Trim() == Text, "consumer.text"); ids.Add(r.Value.Id); row.leases++;
                    }
                    Require(ids.Count == 20 && row.versionFrozen, "consumer.leases");
                }
                finally { Require((await scope.CloseAsync(Cleanup)).IsFullyDrained, "consumer.scope"); }
                row.order.Add("consumer_released"); return Result.Success();
            }
            public UniTask<Result> StopAsync(CleanupContext c) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext c) => UniTask.FromResult(Result.Success());
        }
        static ModuleRegistration Reg(IModule m, params Type[] exposed) => new ModuleRegistration(m.Id, m.Dependencies, () => m, exposed);
        public async UniTask<BundleCase> RunCase(string name)
        {
            Require(!YooAssets.TryGetPackage(Package, out _), "package.preexisting");
            var row = new BundleCase { name = name, bundlesBefore = AssetBundle.GetAllLoadedAssetBundles().Count() }; Cases.Add(row);
            var config = name == "config_mismatch" ? "bundle-v2" : Version;
            var required = name == "missing_first_play" ? "not-in-bundle" : Address;
            var expected = name == "payload_mismatch" ? "wrong-pinned-payload" : Text;
            var backend = new Backend(this, row, config, required, expected);
            var package = new PackageStartup(new PackageProfile(Package, "fixture-app-v1", "bundle-schema-v1", new[] { required }, new[] { new PackageCandidate(PackageSource.BuiltIn, Version, "pinned-local-bundle") }), backend);
            var provider = new ProviderModule(backend);
            var assets = new AssetsService(provider, Package, Version, Cleanup, providerModuleId: provider.Id, packageReadiness: package, packageModuleId: package.Id);
            var consumer = new Consumer(assets, package, row);
            var context = new GameContext(new[] { Reg(package, typeof(IPackageReadiness)), Reg(provider, typeof(IAssetProvider)), Reg(assets, typeof(IAssetsService)), Reg(consumer) }, Cleanup);
            row.readyBeforePrepare = package.CheckReady(Package, Version).IsSuccess;
            try
            {
                var result = await context.StartAsync(Wait); row.error = result.IsSuccess ? "" : result.Error.Code; row.state = package.State.ToString();
                var expectedError = name == "missing_first_play" ? "fixture.bundle.first_play.missing" : name == "config_mismatch" ? "package.manifest.compatibility" : name == "payload_mismatch" ? "fixture.bundle.payload" : "";
                Require(row.error == expectedError, "case.result:" + row.error); Require(!row.readyBeforePrepare, "ready.early");
                Require(row.consumers == (expectedError == "" ? 1 : 0), "consumer.gate");
                if (expectedError == "") Require(row.bundleObserved && row.preparedAssets == 1 && row.leases == 20 && backend.Provider.LoadStartCount == 2, "actual.bundle");
            }
            finally
            {
                Require((await context.CloseAsync(Wait)).IsSuccess, "context.close");
                row.modules = context.RetainedModuleCount; row.sessions = backend.Sessions; row.providerHandles = backend.Provider?.RetainedHandleCount ?? 0;
                row.loadStarts = backend.Provider?.LoadStartCount ?? 0; row.assetEntries = assets.RetainedEntryCount;
                row.packages = YooAssets.TryGetPackage(Package, out _) ? 1 : 0; row.bundlesAfter = AssetBundle.GetAllLoadedAssetBundles().Count();
            }
            Require(row.modules + row.sessions + row.providerHandles + row.assetEntries + row.packages == 0 && row.packageRemoved && row.bundlesAfter == row.bundlesBefore, "cleanup.retained");
            row.passed = true; return row;
        }
        public async UniTask Execute() { foreach (var n in new[] { "ready_shared_leases", "missing_first_play", "config_mismatch", "payload_mismatch" }) await RunCase(n); }
    }
}
