using System;
using MiniFramework.Contracts;
using Cysharp.Threading.Tasks;
using YooAsset;
using Object = UnityEngine.Object;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MiniFramework.Providers.Tests.PlayMode")]
namespace MiniFramework.Providers.YooAsset
{
    // Internal fault-injection seam; production always wraps the pinned SDK directly.
    internal interface IYooPackage
    {
        string Name { get; }
        Result ValidateVersion(string version);
        Result<IYooHandle> Load(string address, Type type);
    }
    internal interface IYooHandle
    {
        bool IsDone { get; }
        bool Succeeded { get; }
        string Error { get; }
        Object Value { get; }
        void Release();
    }
    internal interface IYooDownloadPackage
    {
        Result<IProviderOperation<Unit>> Download(string address,Type type);
        Result<IYooHandle> LoadPrepared(string address,Type type);
    }
    internal sealed class YooPackage : IYooPackage,IYooDownloadPackage
    {
        private readonly ResourcePackage package;
        internal YooPackage(ResourcePackage package) { this.package = package ?? throw new ArgumentNullException(nameof(package)); }
        public string Name => package.PackageName;
        public Result ValidateVersion(string version)
        {
            if (package.InitializeStatus != EOperationStatus.Succeeded)
                return YooAssetProvider.Fail("yoo.package.not_ready", ErrorKind.NotReady);
            string actual;
            try { actual = package.GetPackageVersion(); }
            catch (Exception) { return YooAssetProvider.Fail("yoo.manifest.not_ready", ErrorKind.NotReady); }
            return actual == version ? Result.Success() : YooAssetProvider.Fail("yoo.version.changed", ErrorKind.Integrity);
        }
        public Result<IProviderOperation<Unit>> Download(string address,Type type)
        {
            var info=package.GetAssetInfo(address,type);
            if(!info.IsValid)return YooAssetProvider.Reject<IProviderOperation<Unit>>("yoo.asset.missing",ErrorKind.Unavailable,info.Error);
            // One file worker per admitted job; scheduler controls job concurrency, SDK owns file details.
            var downloader=package.CreateResourceDownloader(new BundleDownloaderOptions(info,true,1,0));
            var operation=new ProviderOperation<Unit>();downloader.StartDownload();operation.Retain(ObserveDownload(downloader,operation));
            return Result<IProviderOperation<Unit>>.Success(operation);
        }
        private async UniTask ObserveDownload(ResourceDownloaderOperation downloader,ProviderOperation<Unit> operation)
        {
            while(!downloader.IsDone)await UniTask.Yield();
            operation.Complete(downloader.Status==EOperationStatus.Succeeded?Result<Unit>.Success(default):
                YooAssetProvider.Reject<Unit>("yoo.download.failed",ErrorKind.Provider,downloader.Error));
        }
        public Result<IYooHandle> LoadPrepared(string address,Type type)
        {
            var info=package.GetAssetInfo(address,type);
            if(!info.IsValid)return YooAssetProvider.Reject<IYooHandle>("yoo.asset.missing",ErrorKind.Unavailable,info.Error);
            var check=package.CreateResourceDownloader(new BundleDownloaderOptions(info,true,1,0));
            if(check.TotalDownloadCount!=0)return YooAssetProvider.Reject<IYooHandle>("yoo.prepared.missing",ErrorKind.Unavailable);
            return Result<IYooHandle>.Success(new YooHandle(package.LoadAssetAsync(info)));
        }
        public Result<IYooHandle> Load(string address, Type type)
        {
            var info = package.GetAssetInfo(address, type);
            if (!info.IsValid) return YooAssetProvider.Reject<IYooHandle>("yoo.asset.missing", ErrorKind.Unavailable, info.Error);
            return Result<IYooHandle>.Success(new YooHandle(package.LoadAssetAsync(info)));
        }
    }
    internal sealed class YooHandle : IYooHandle
    {
        private readonly AssetHandle handle;
        internal YooHandle(AssetHandle handle) { this.handle = handle; }
        public bool IsDone => handle.IsDone;
        public bool Succeeded => handle.Status == EOperationStatus.Succeeded;
        public string Error => handle.Error;
        public Object Value => handle.AssetObject;
        public void Release()
        {
            // A valid handle is released once. Invalid also covers a previous Release that
            // committed before an exception; never replay a committed SDK reference decrement.
            if (handle.IsValid) handle.Release();
            if (handle.IsValid) throw new InvalidOperationException("SDK retained the released handle.");
        }
    }
}
