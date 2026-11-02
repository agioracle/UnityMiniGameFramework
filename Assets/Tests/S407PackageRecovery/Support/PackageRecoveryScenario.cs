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

namespace MiniFramework.S407PackageRecovery
{
    [Serializable, Preserve] public sealed class PackageRecoveryCase
    {
        public string name, error = "", firstError = "", terminalError = "", selectedVersion = "";
        public string[] trace = Array.Empty<string>(), candidateLocations = Array.Empty<string>();
        public bool passed, loadedActualBundle, cleanupBeforeFallback, allFailedDrained, newContextRecovered;
        public bool injectedPrepareAcknowledgementHold, closeWaited, readyDeniedDuringHold, consumerBlockedDuringHold;
        public int attempts, cleanups, consumers, leases, modules, sessions, handles, entries, packages, bundlesBefore, bundlesAfter;
    }

    // Validation only: production backend results and ownership are observed, never synthesized.
    public sealed class PackageRecoveryScenario
    {
        public const string Package = "S407RealBundle", Version = "bundle-v1", Address = "real-bundle";
        public const string Text = "S4-07 real AssetBundle / frozen content v1";
        public static readonly string[] Names = { "initialization_failure_fallback", "manifest_failure_fallback", "all_failed_new_context_recovery", "close_during_preparation" };
        public static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        public readonly List<PackageRecoveryCase> Cases = new List<PackageRecoveryCase>();
        readonly string root;
        readonly Func<string, InitializePackageOptions> options;
        public PackageRecoveryScenario(string root, Func<string, InitializePackageOptions> options) { this.root = root; this.options = options; }
        public static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        public static string MissingRoot(string validRoot)
        {
            var uri = new Uri(validRoot);
            // Keep the deliberate missing HTTPS path outside the official SDK's configured bundle-cache prefix.
            return uri.Scheme == "https" || uri.Scheme == "http" ? uri.GetLeftPart(UriPartial.Authority) + "/__s407_package_recovery_missing_v2__" : validRoot + "/__s407_missing_candidate__";
        }
        sealed class ObservedBackend : IPackageBackend
        {
            public readonly YooAssetPackageBackend Actual;
            public readonly List<string> Trace = new List<string>();
            public readonly List<string> Locations = new List<string>();
            public readonly UniTaskCompletionSource Prepared = new UniTaskCompletionSource();
            public readonly UniTaskCompletionSource ReleaseAcknowledgement = new UniTaskCompletionSource();
            readonly PackageRecoveryCase row;
            bool releasedPrevious;
            int creates;
            public ObservedBackend(YooAssetPackageBackend actual, PackageRecoveryCase row) { Actual = actual; this.row = row; }
            public IPackageSession Create(PackageProfile profile, PackageCandidate candidate)
            {
                if (creates > 0)
                {
                    var clean = releasedPrevious && Actual.RetainedSessionCount == 0 && !YooAssets.TryGetPackage(Package, out _)
                        && AssetBundle.GetAllLoadedAssetBundles().Count() == row.bundlesBefore;
                    Require(clean, "fallback.before_cleanup"); row.cleanupBeforeFallback = true;
                }
                row.attempts++; creates++; releasedPrevious = false; Trace.Add("create:" + candidate.Version); Locations.Add(candidate.Location);
                return new Session(this, Actual.Create(profile, candidate));
            }
            sealed class Session : IPackageSession
            {
                readonly ObservedBackend owner; readonly IPackageSession actual;
                public Session(ObservedBackend owner, IPackageSession actual) { this.owner = owner; this.actual = actual; }
                public async UniTask<Result<PackageManifest>> PrepareAsync()
                {
                    var result = await actual.PrepareAsync();
                    var code = result.IsSuccess ? "ready" : result.Error.Code;
                    owner.Trace.Add("prepared:" + code);
                    if (!result.IsSuccess && owner.row.firstError == "") owner.row.firstError = code;
                    if (owner.row.injectedPrepareAcknowledgementHold)
                    {
                        Require(result.IsSuccess, "hold.requires_actual_prepared");
                        owner.Prepared.TrySetResult();
                        await owner.ReleaseAcknowledgement.Task;
                    }
                    return result;
                }
                public async UniTask<Result> CleanupAsync()
                {
                    owner.Trace.Add("cleanup.begin");
                    var result = await actual.CleanupAsync();
                    Require(result.IsSuccess, "actual.cleanup.failed");
                    owner.row.cleanups++; owner.releasedPrevious = true; owner.Trace.Add("cleanup.confirmed");
                    return result;
                }
            }
        }
        sealed class Consumer : IModule
        {
            readonly AssetsService assets; readonly PackageStartup startup; readonly PackageRecoveryCase row;
            public Consumer(AssetsService assets, PackageStartup startup, PackageRecoveryCase row) { this.assets = assets; this.startup = startup; this.row = row; }
            public string Id => "consumer";
            public IReadOnlyList<string> Dependencies => new[] { "assets", "package" };
            public async UniTask<Result> InitializeAsync(IModuleContext context, OperationContext wait)
            {
                Require(startup.CheckReady(Package, Version).IsSuccess, "consumer.ready"); row.consumers++;
                var scope = new Scope("package-recovery-consumer");
                try
                {
                    var key = new ResourceKey("yooasset", Package, Version, Address, typeof(TextAsset));
                    var load = assets.Load<TextAsset>(key, scope); Require(load.IsSuccess, "consumer.load");
                    var lease = await load.Value.WaitAsync(wait);
                    Require(lease.IsSuccess && lease.Value.Value.text.Trim() == Text, "consumer.payload"); row.leases++;
                }
                finally { Require((await scope.CloseAsync(Cleanup)).IsFullyDrained, "consumer.cleanup"); }
                return Result.Success();
            }
            public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        }
        sealed class Composition
        {
            public GameContext Context; public PackageStartup Startup; public ObservedBackend Backend;
            public YooAssetPackageProvider Provider; public AssetsService Assets;
        }
        static ModuleRegistration Reg(IModule module, params Type[] services) => new ModuleRegistration(module.Id, module.Dependencies, () => module, services);
        Composition Compose(PackageRecoveryCase row, params PackageCandidate[] candidates)
        {
            var definitions = candidates.Select(candidate => new YooAssetPackageDefinition(Package, candidate, candidate.Version, "bundle-schema-v1",
                new[] { "fixture-app-v1" }, new[] { new YooAssetFirstPlayAsset(Address, typeof(TextAsset), asset => {
                    row.loadedActualBundle |= AssetBundle.GetAllLoadedAssetBundles().Count() > row.bundlesBefore;
                    return ((TextAsset)asset).text.Trim() == Text ? Result.Success() : Result.Failure(new FrameworkError(ErrorKind.Integrity, "fixture.payload"));
                }) }, () => {
                    var configured = options(candidate.Location);
                    Require(configured is WebPlayModeOptions web && web.WebServerFileSystemParameters.PackageRoot == candidate.Location, "fixture.options.root_mismatch");
                    return configured;
                })).ToArray();
            var actual = new YooAssetPackageBackend(definitions); var observed = new ObservedBackend(actual, row);
            var startup = new PackageStartup(new PackageProfile(Package, "fixture-app-v1", "bundle-schema-v1", new[] { Address }, candidates), observed);
            var provider = new YooAssetPackageProvider(actual, startup);
            var assets = new AssetsService(provider, Package, Version, Cleanup, providerModuleId: provider.Id, packageReadiness: startup, packageModuleId: startup.Id);
            var context = new GameContext(new[] { Reg(startup, typeof(IPackageReadiness)), Reg(provider, typeof(IAssetProvider), typeof(IAssetDownloadProvider)), Reg(assets, typeof(IAssetsService)), Reg(new Consumer(assets, startup, row)) }, Cleanup);
            return new Composition { Context = context, Startup = startup, Provider = provider, Assets = assets, Backend = observed };
        }
        static void CheckDrained(Composition c, PackageRecoveryCase row)
        {
            row.modules += c.Context.RetainedModuleCount; row.sessions += c.Backend.Actual.RetainedSessionCount;
            row.handles += c.Provider.RetainedHandleCount; row.entries += c.Assets.RetainedEntryCount;
            Require(row.modules + row.sessions + row.handles + row.entries == 0 && !YooAssets.TryGetPackage(Package, out _), "composition.retained");
        }
        public async UniTask<PackageRecoveryCase> Run(string name)
        {
            Require(Names.Contains(name) && !YooAssets.TryGetPackage(Package, out _), "case.precondition");
            var row = new PackageRecoveryCase { name = name, bundlesBefore = AssetBundle.GetAllLoadedAssetBundles().Count(), injectedPrepareAcknowledgementHold = name == Names[3] }; Cases.Add(row);
            var good = new PackageCandidate(PackageSource.Remote, Version, root);
            var missingRoot = new PackageCandidate(PackageSource.Remote, Version, MissingRoot(root));
            var missingManifest = new PackageCandidate(PackageSource.Remote, "missing-version", root);
            var candidates = name == Names[0] ? new[] { missingRoot, good } : name == Names[1] ? new[] { missingManifest, good } : name == Names[2] ? new[] { missingRoot, missingManifest } : new[] { good };
            var compositions = new List<Composition>(); var c = Compose(row, candidates); compositions.Add(c);
            try
            {
                var started = c.Context.StartAsync(new OperationContext(default, TimeSpan.FromSeconds(90))).Preserve();
                if (row.injectedPrepareAcknowledgementHold)
                {
                    await c.Backend.Prepared.Task.Timeout(TimeSpan.FromSeconds(75));
                    row.readyDeniedDuringHold = !c.Startup.CheckReady(Package, Version).IsSuccess;
                    row.consumerBlockedDuringHold = row.consumers == 0;
                    var closing = c.Context.CloseAsync(OperationContext.Unlimited()).Preserve();
                    for (int i = 0; i < 3; i++) await UniTask.Yield();
                    row.closeWaited = closing.Status == UniTaskStatus.Pending && c.Backend.Actual.RetainedSessionCount == 1 && YooAssets.TryGetPackage(Package, out _);
                    Require(row.closeWaited && row.readyDeniedDuringHold && row.consumerBlockedDuringHold, "close.barrier");
                    c.Backend.ReleaseAcknowledgement.TrySetResult();
                    var result = await started; row.terminalError = result.IsSuccess ? "" : result.Error.Code;
                    Require(row.terminalError == "context.closed.during.start" && (await closing).IsSuccess && row.consumers == 0, "close.result");
                }
                else
                {
                    var result = await started; row.terminalError = result.IsSuccess ? "" : result.Error.Code;
                    Require(row.firstError == (name == Names[1] ? "yoo.package.manifest" : "yoo.package.initialize"), "first.failure:" + row.firstError);
                    if (name == Names[2])
                    {
                        Require(!result.IsSuccess && row.terminalError == "yoo.package.manifest" && row.consumers == 0, "all.failed");
                        Require((await c.Context.CloseAsync(OperationContext.Unlimited())).IsSuccess, "failed.close"); CheckDrained(c, row); row.allFailedDrained = true;
                        var fresh = Compose(row, good); compositions.Add(fresh); Require(!ReferenceEquals(fresh.Context, c.Context), "context.not_new"); c = fresh;
                        Require((await c.Context.StartAsync(new OperationContext(default, TimeSpan.FromSeconds(60)))).IsSuccess, "fresh.start"); row.newContextRecovered = true;
                    }
                    else Require(result.IsSuccess && row.cleanupBeforeFallback, "fallback.result:" + row.terminalError);
                    row.selectedVersion = c.Startup.ContentVersion;
                    Require(row.consumers == 1 && row.leases == 1 && row.loadedActualBundle && row.selectedVersion == Version, "recovered.business");
                }
            }
            catch (Exception e) { row.error = e.Message; }
            finally
            {
                foreach (var item in compositions)
                {
                    item.Backend.ReleaseAcknowledgement.TrySetResult(); // Always release only the fixture's acknowledgment hold.
                    try { Require((await item.Context.CloseAsync(OperationContext.Unlimited())).IsSuccess, "final.close"); }
                    catch (Exception e) { row.error += ";" + e.Message; }
                }
                foreach (var item in compositions)
                    try { CheckDrained(item, row); } catch (Exception e) { row.error += ";" + e.Message; }
                row.trace = compositions.SelectMany(item => item.Backend.Trace).ToArray();
                row.candidateLocations = compositions.SelectMany(item => item.Backend.Locations).ToArray();
                row.packages = YooAssets.TryGetPackage(Package, out _) ? 1 : 0; row.bundlesAfter = AssetBundle.GetAllLoadedAssetBundles().Count();
                row.passed = row.error == "" && row.attempts == (name == Names[2] ? 3 : name == Names[3] ? 1 : 2) && row.cleanups == row.attempts
                    && row.modules + row.sessions + row.handles + row.entries + row.packages == 0 && row.bundlesBefore == row.bundlesAfter;
            }
            return row;
        }
        public async UniTask Execute() { foreach (var name in Names) Require((await Run(name)).passed, "case.failed:" + name); }
    }
}
