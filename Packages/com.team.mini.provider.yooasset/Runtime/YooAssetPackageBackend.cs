using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using YooAsset;

namespace MiniFramework.Providers.YooAsset
{
    // Owns one package session. SDK initialization and application lifetime remain the caller's responsibility.
    public sealed class YooAssetPackageBackend : IPackageBackend
    {
        readonly IReadOnlyList<YooAssetPackageDefinition> definitions;
        readonly Func<string, Result<IYooPackageDriver>> create;
        Session active;
        public int RetainedSessionCount => active == null ? 0 : 1;
        public int PreparedAssetCount => active?.PreparedCount ?? 0;
        public YooAssetPackageBackend(IEnumerable<YooAssetPackageDefinition> definitions) : this(definitions, YooAssetPackageDriver.Create) { }
        internal YooAssetPackageBackend(IEnumerable<YooAssetPackageDefinition> definitions, Func<string, Result<IYooPackageDriver>> create)
        {
            if (definitions == null || create == null) throw new ArgumentNullException();
            var items = new List<YooAssetPackageDefinition>(definitions);
            if (items.Count == 0 || items.Exists(d => d == null)) throw new ArgumentException("At least one definition is required.");
            for (int i = 0; i < items.Count; i++) for (int j = 0; j < i; j++)
                if (items[i].Package == items[j].Package && items[i].Matches(items[j].Candidate)) throw new ArgumentException("Duplicate candidate.");
            this.definitions = items.AsReadOnly(); this.create = create;
        }
        static void MainThread() { if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("YooAsset package operations require the Unity main thread."); }
        public IPackageSession Create(PackageProfile profile, PackageCandidate candidate)
        {
            MainThread(); if (profile == null || candidate == null) throw new ArgumentNullException();
            // Cold: no package registration, options callback, download or owner reservation here.
            YooAssetPackageDefinition found = null;
            foreach (var d in definitions) if (d.Package == profile.Package && d.Matches(candidate)) { found = d; break; }
            return new Session(this, profile, found);
        }
        public Result<YooAssetProvider> CreateAssetProvider(IPackageReadiness readiness, string id = "yooasset")
        {
            MainThread();
            if (active == null || !active.Prepared || active.Closing) return YooAssetProvider.Reject<YooAssetProvider>("yoo.package.not_ready", ErrorKind.NotReady);
            if (readiness == null) return YooAssetProvider.Reject<YooAssetProvider>("yoo.package.readiness", ErrorKind.InvalidArgument);
            var ready = readiness.CheckReady(active.Definition.Package, active.Definition.Candidate.Version);
            if (!ready.IsSuccess) return Result<YooAssetProvider>.Failure(ready.Error);
            if (active.Provider != null) return YooAssetProvider.Reject<YooAssetProvider>("yoo.package.provider.bound", ErrorKind.InvalidState);
            if (string.IsNullOrWhiteSpace(id)) return YooAssetProvider.Reject<YooAssetProvider>("yoo.package.provider.id", ErrorKind.InvalidArgument);
            active.Provider = new YooAssetProvider(active.Driver.Assets, active.Definition.Candidate.Version, id);
            return Result<YooAssetProvider>.Success(active.Provider);
        }
        sealed class Session : IPackageSession
        {
            readonly YooAssetPackageBackend owner; readonly PackageProfile profile;
            public readonly YooAssetPackageDefinition Definition;
            public IYooPackageDriver Driver; public YooAssetProvider Provider;
            public bool Prepared, Closing; public int PreparedCount;
            bool closed, skipFailedHandleRetry; IYooHandle handle;
            UniTaskCompletionSource<Result<PackageManifest>> preparing; UniTaskCompletionSource<Result> cleaning;
            public Session(YooAssetPackageBackend owner, PackageProfile profile, YooAssetPackageDefinition definition) { this.owner = owner; this.profile = profile; Definition = definition; }
            public UniTask<Result<PackageManifest>> PrepareAsync()
            {
                MainThread();
                if (Closing || closed) return UniTask.FromResult(YooAssetProvider.Reject<PackageManifest>("yoo.package.closed", ErrorKind.InvalidState));
                if (preparing != null) return preparing.Task;
                preparing = new UniTaskCompletionSource<Result<PackageManifest>>(); Prepare().Forget(); return preparing.Task;
            }
            async UniTaskVoid Prepare()
            {
                Result<PackageManifest> result;
                try
                {
                    if (Definition == null) result = YooAssetProvider.Reject<PackageManifest>("yoo.package.candidate", ErrorKind.NotSupported);
                    else if (owner.active != null && owner.active != this) result = YooAssetProvider.Reject<PackageManifest>("yoo.package.owned", ErrorKind.InvalidState);
                    else { owner.active = this; result = await PrepareOwned(); }
                }
                catch (Exception e) { result = YooAssetProvider.Reject<PackageManifest>("yoo.package.prepare.exception", ErrorKind.Provider, e.Message); }
                preparing.TrySetResult(result);
            }
            async UniTask<Result<PackageManifest>> PrepareOwned()
            {
                foreach (var required in profile.FirstPlay)
                {
                    bool found = false; foreach (var a in Definition.FirstPlay) if (a.Address == required) found = true;
                    if (!found) return YooAssetProvider.Reject<PackageManifest>("yoo.package.first_play.undefined", ErrorKind.Unavailable);
                }
                var options = Definition.CreateOptions();
                if (options == null) return YooAssetProvider.Reject<PackageManifest>("yoo.package.options", ErrorKind.InvalidArgument);
                var start = owner.create(Definition.Package);
                if (!start.IsSuccess) return Result<PackageManifest>.Failure(start.Error);
                Driver = start.Value;
                if (Driver == null) return YooAssetProvider.Reject<PackageManifest>("yoo.package.driver.null", ErrorKind.Integrity);
                var initialized = await Driver.InitializeAsync(options); if (!initialized.IsSuccess) return Result<PackageManifest>.Failure(initialized.Error);
                var manifest = await Driver.LoadManifestAsync(Definition.Candidate.Version, Definition.ManifestTimeoutSeconds); if (!manifest.IsSuccess) return Result<PackageManifest>.Failure(manifest.Error);
                if (Driver.Version != Definition.Candidate.Version) return YooAssetProvider.Reject<PackageManifest>("yoo.package.version", ErrorKind.Integrity);
                var available = new List<string>();
                foreach (var asset in Definition.FirstPlay)
                {
                    var load = Driver.Assets.Load(asset.Address, asset.AssetType);
                    if (!load.IsSuccess) return Result<PackageManifest>.Failure(load.Error);
                    handle = load.Value;
                    if (handle == null) return YooAssetProvider.Reject<PackageManifest>("yoo.package.handle.null", ErrorKind.Integrity);
                    while (!handle.IsDone) await UniTask.Yield();
                    Result checkedAsset;
                    try
                    {
                        checkedAsset = !handle.Succeeded ? YooAssetProvider.Fail("yoo.package.first_play.load", ErrorKind.Provider, handle.Error) :
                            handle.Value == null || !asset.AssetType.IsInstanceOfType(handle.Value) ? YooAssetProvider.Fail("yoo.package.first_play.payload", ErrorKind.Integrity) :
                            asset.Validate == null ? Result.Success() : asset.Validate(handle.Value);
                    }
                    finally { try { handle.Release(); handle = null; } catch { skipFailedHandleRetry = true; } }
                    if (handle != null) return YooAssetProvider.Reject<PackageManifest>("yoo.package.first_play.release", ErrorKind.CleanupFailed);
                    if (!checkedAsset.IsSuccess) return Result<PackageManifest>.Failure(checkedAsset.Error);
                    available.Add(asset.Address); PreparedCount++;
                }
                Prepared = true;
                return Result<PackageManifest>.Success(new PackageManifest(Definition.Package, Driver.Version, Definition.ConfigVersion, Definition.Schema, true, Definition.CompatiblePrograms, available));
            }
            public UniTask<Result> CleanupAsync()
            {
                MainThread(); Closing = true; Prepared = false;
                if (closed) return UniTask.FromResult(Result.Success());
                if (cleaning != null) return cleaning.Task;
                var pending = new UniTaskCompletionSource<Result>(); cleaning = pending; Clean(pending).Forget(); return pending.Task;
            }
            async UniTaskVoid Clean(UniTaskCompletionSource<Result> pending)
            {
                Result result;
                try
                {
                    if (preparing != null) await preparing.Task;
                    // Also fence callers that bound the provider directly, outside GameContext's Stop phase.
                    if (Provider != null) await Provider.StopAsync(new CleanupContext(default, System.Threading.Timeout.InfiniteTimeSpan));
                    if (skipFailedHandleRetry) { skipFailedHandleRetry = false; result = YooAssetProvider.Fail("yoo.package.first_play.release", ErrorKind.CleanupFailed); }
                    else if (Provider != null && (Provider.RetainedHandleCount != 0 || Provider.ActualDownloadCount != 0)) result = YooAssetProvider.Fail("yoo.package.provider.retained", ErrorKind.CleanupFailed);
                    else
                    {
                        if (handle != null) { handle.Release(); handle = null; }
                        result = Driver == null ? Result.Success() : await Driver.DestroyAsync();
                        if (result.IsSuccess) { closed = true; Driver = null; Provider = null; if (owner.active == this) owner.active = null; }
                    }
                }
                catch (Exception e) { result = YooAssetProvider.Fail("yoo.package.cleanup.exception", ErrorKind.CleanupFailed, e.Message); }
                cleaning = null; pending.TrySetResult(result);
            }
        }
    }
}
