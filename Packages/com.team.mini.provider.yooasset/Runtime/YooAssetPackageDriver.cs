using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using YooAsset;

namespace MiniFramework.Providers.YooAsset
{
    // Internal fault seam. Public construction always uses the installed YooAsset SDK.
    internal interface IYooPackageDriver
    {
        IYooPackage Assets { get; }
        string Version { get; }
        UniTask<Result> InitializeAsync(InitializePackageOptions options);
        UniTask<Result> LoadManifestAsync(string version, int timeoutSeconds);
        UniTask<Result> DestroyAsync();
    }
    internal sealed class YooAssetPackageDriver : IYooPackageDriver
    {
        readonly ResourcePackage package;
        bool destroyed;
        public IYooPackage Assets { get; }
        public string Version => package.GetPackageVersion();
        YooAssetPackageDriver(ResourcePackage package) { this.package = package; Assets = new YooPackage(package); }
        internal static Result<IYooPackageDriver> Create(string name)
        {
            if (!YooAssets.IsInitialized) return YooAssetProvider.Reject<IYooPackageDriver>("yoo.package.sdk.not_ready", ErrorKind.NotReady);
            if (YooAssets.TryGetPackage(name, out _)) return YooAssetProvider.Reject<IYooPackageDriver>("yoo.package.foreign", ErrorKind.InvalidState);
            return Result<IYooPackageDriver>.Success(new YooAssetPackageDriver(YooAssets.CreatePackage(name)));
        }
        public async UniTask<Result> InitializeAsync(InitializePackageOptions options)
        {
            var op = package.InitializePackageAsync(options);
            while (!op.IsDone) await UniTask.Yield();
            return op.Status == EOperationStatus.Succeeded ? Result.Success() : YooAssetProvider.Fail("yoo.package.initialize", ErrorKind.Provider, op.Error);
        }
        public async UniTask<Result> LoadManifestAsync(string version, int timeoutSeconds)
        {
            var op = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, timeoutSeconds));
            while (!op.IsDone) await UniTask.Yield();
            return op.Status == EOperationStatus.Succeeded ? Result.Success() : YooAssetProvider.Fail("yoo.package.manifest", ErrorKind.Provider, op.Error);
        }
        public async UniTask<Result> DestroyAsync()
        {
            if (!destroyed)
            {
                var op = package.DestroyPackageAsync();
                while (!op.IsDone) await UniTask.Yield();
                if (op.Status != EOperationStatus.Succeeded) return YooAssetProvider.Fail("yoo.package.destroy", ErrorKind.CleanupFailed, op.Error);
                destroyed = true;
            }
            if (YooAssets.TryGetPackage(package.PackageName, out var current))
            {
                if (!ReferenceEquals(current, package)) return YooAssetProvider.Fail("yoo.package.owner.changed", ErrorKind.InvalidState);
                YooAssets.RemovePackage(package.PackageName);
            }
            return Result.Success();
        }
    }
}
