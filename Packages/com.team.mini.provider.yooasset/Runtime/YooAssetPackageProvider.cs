using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;

namespace MiniFramework.Providers.YooAsset
{
    // Lazy binding is necessary: GameContext creates every module before initializing dependencies.
    public sealed class YooAssetPackageProvider : IModule, IAssetProvider, IAssetDownloadProvider
    {
        readonly YooAssetPackageBackend backend;
        readonly IPackageReadiness readiness;
        YooAssetProvider provider;
        bool stopped;
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public int RetainedHandleCount => provider?.RetainedHandleCount ?? 0;
        public int LoadStartCount => provider?.LoadStartCount ?? 0;
        public YooAssetPackageProvider(YooAssetPackageBackend backend, IPackageReadiness readiness, string packageModuleId = "package", string id = "yooasset")
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            if (string.IsNullOrWhiteSpace(packageModuleId) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ids are required.");
            Id = id; Dependencies = Array.AsReadOnly(new[] { packageModuleId });
        }
        public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation)
        {
            if (stopped || provider != null) return UniTask.FromResult(YooAssetProvider.Fail("yoo.package.provider.state", ErrorKind.InvalidState));
            var bound = backend.CreateAssetProvider(readiness, Id);
            if (!bound.IsSuccess) return UniTask.FromResult(Result.Failure(bound.Error));
            provider = bound.Value;
            return provider.InitializeAsync(context, operation);
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup) { stopped = true; return provider == null ? UniTask.FromResult(Result.Success()) : provider.StopAsync(cleanup); }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup) => provider == null ? UniTask.FromResult(Result.Success()) : provider.DisposeAsync(cleanup);
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T : class => provider == null || stopped ? NotReady<IProviderOperation<IProviderAsset<T>>>() : provider.BeginLoad<T>(key);
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoadPrepared<T>(ResourceKey key) where T : class => provider == null || stopped ? NotReady<IProviderOperation<IProviderAsset<T>>>() : provider.BeginLoadPrepared<T>(key);
        public Result<IProviderOperation<Unit>> BeginDownload(ResourceKey key) => provider == null || stopped ? NotReady<IProviderOperation<Unit>>() : provider.BeginDownload(key);
        static Result<T> NotReady<T>() => YooAssetProvider.Reject<T>("yoo.package.provider.not_ready", ErrorKind.NotReady);
    }
}
