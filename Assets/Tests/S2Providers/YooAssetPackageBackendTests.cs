using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.YooAsset;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace MiniFramework.Providers.Tests
{
    public sealed class YooAssetPackageBackendTests
    {
        static readonly PackageCandidate Candidate = new PackageCandidate(PackageSource.Remote, "v1", "https://fixture.invalid/pinned");
        static PackageProfile Profile(params string[] assets) => new PackageProfile("pkg", "app", "schema", assets.Length == 0 ? new[] { "text" } : assets, new[] { Candidate });
        static FrameworkError Failure(string code) => new FrameworkError(ErrorKind.Provider, code);
        sealed class Handle : IYooHandle
        {
            public bool Done = true, Success = true, FailRelease; public int Releases; public Object Payload = new TextAsset("payload");
            public bool IsDone => Done; public bool Succeeded => Success; public string Error => "injected.load"; public Object Value => Payload;
            public void Release() { Releases++; if (FailRelease) throw new InvalidOperationException("injected.release"); }
        }
        sealed class Driver : IYooPackageDriver, IYooPackage
        {
            public Handle Handle = new Handle(); public int Inits, Manifests, Loads, Destroys; public bool InitFail, ManifestFail, DestroyFail, Missing;
            public string ActualVersion = "v1"; public UniTaskCompletionSource<Result> DestroyGate;
            public IYooPackage Assets => this; public string Name => "pkg"; public string Version => ActualVersion;
            public Result ValidateVersion(string v) => Result.Success();
            public Result<IYooHandle> Load(string a, Type t) { Loads++; return Missing ? Result<IYooHandle>.Failure(Failure("yoo.asset.missing")) : Result<IYooHandle>.Success(Handle); }
            public UniTask<Result> InitializeAsync(InitializePackageOptions o) { Inits++; return UniTask.FromResult(InitFail ? Result.Failure(Failure("injected.initialize")) : Result.Success()); }
            public UniTask<Result> LoadManifestAsync(string v, int t) { Manifests++; return UniTask.FromResult(ManifestFail ? Result.Failure(Failure("injected.manifest")) : Result.Success()); }
            public async UniTask<Result> DestroyAsync() { Destroys++; if (DestroyGate != null) return await DestroyGate.Task; return DestroyFail ? Result.Failure(Failure("injected.destroy")) : Result.Success(); }
        }
        sealed class Ready : IPackageReadiness
        {
            public bool Allowed = true; public string Package => "pkg"; public string ContentVersion => "v1"; public PackageState State => Allowed ? PackageState.Ready : PackageState.Failed;
            public Result CheckReady(string p, string v) => Allowed && p == Package && v == ContentVersion ? Result.Success() : Result.Failure(new FrameworkError(ErrorKind.NotReady,"injected.not_ready"));
        }
        static YooAssetPackageDefinition Definition(Func<Object, Result> validate = null, Func<InitializePackageOptions> options = null) => new YooAssetPackageDefinition("pkg", Candidate, "v1", "schema", new[] { "app" }, new[] { new YooAssetFirstPlayAsset("text", typeof(TextAsset), validate) }, options ?? (() => new WebPlayModeOptions()));
        static YooAssetPackageBackend Backend(Driver driver, YooAssetPackageDefinition definition = null) => new YooAssetPackageBackend(new[] { definition ?? Definition() }, _ => Result<IYooPackageDriver>.Success(driver));
        IEnumerator Run(Func<UniTask> body) => UniTask.ToCoroutine(body);

        [Test] public void CreateIsColdAndCopiesConfiguration()
        {
            var d = new Driver(); int options = 0; var b = Backend(d, Definition(options: () => { options++; return new WebPlayModeOptions(); }));
            b.Create(Profile(), Candidate); Assert.AreEqual(0, options); Assert.AreEqual(0, d.Inits); Assert.AreEqual(0, b.RetainedSessionCount);
        }
        [Test] public void RejectsDuplicateDefinitionAndCacheClaim()
        {
            var def = Definition(); Assert.Throws<ArgumentException>(() => new YooAssetPackageBackend(new[] { def, def }));
            Assert.Throws<ArgumentException>(() => new YooAssetPackageDefinition("pkg", new PackageCandidate(PackageSource.CompatibleCache,"v1","local"), "v1","schema",new[]{"app"},def.FirstPlay,def.CreateOptions));
        }
        [UnityTest] public IEnumerator ExactCandidateAndTypedFirstPlayRequired() => Run(async () =>
        {
            var d = new Driver(); var b = Backend(d); var wrong = b.Create(Profile(),new PackageCandidate(PackageSource.Remote,"v1",Candidate.Location+"/other"));
            Assert.AreEqual("yoo.package.candidate",(await wrong.PrepareAsync()).Error.Code); Assert.IsTrue((await wrong.CleanupAsync()).IsSuccess);
            var missing = b.Create(Profile("unknown"),Candidate); Assert.AreEqual("yoo.package.first_play.undefined",(await missing.PrepareAsync()).Error.Code);
            Assert.IsTrue((await missing.CleanupAsync()).IsSuccess); Assert.AreEqual(0,d.Inits); Assert.AreEqual(0,b.RetainedSessionCount);
        });
        [UnityTest] public IEnumerator ActualCompletionAndCleanupFence() => Run(async () =>
        {
            var d = new Driver(); d.Handle.Done = false; var b = Backend(d); var s = b.Create(Profile(),Candidate); var task = s.PrepareAsync().Preserve();
            Assert.AreEqual(UniTaskStatus.Pending,task.Status); Assert.IsFalse(b.CreateAssetProvider(new Ready()).IsSuccess);
            var close=s.CleanupAsync().Preserve(); Assert.AreEqual(UniTaskStatus.Pending,close.Status); Assert.AreEqual(0,d.Destroys);
            d.Handle.Done=true; Assert.IsTrue((await task).IsSuccess); Assert.IsTrue((await close).IsSuccess);
            Assert.AreEqual(1,d.Handle.Releases); Assert.AreEqual(1,d.Destroys); Assert.IsFalse(b.CreateAssetProvider(new Ready()).IsSuccess);
        });
        [UnityTest] public IEnumerator DuplicatePrepareAndOwnerIsolation() => Run(async () =>
        {
            var d=new Driver();d.Handle.Done=false;var b=Backend(d);var s=b.Create(Profile(),Candidate);var t=s.PrepareAsync().Preserve();var duplicate=s.PrepareAsync().Preserve();
            var other=b.Create(Profile(),Candidate);Assert.AreEqual("yoo.package.owned",(await other.PrepareAsync()).Error.Code);Assert.IsTrue((await other.CleanupAsync()).IsSuccess);Assert.AreEqual(1,b.RetainedSessionCount);
            d.Handle.Done=true;Assert.IsTrue((await t).IsSuccess);Assert.IsTrue((await duplicate).IsSuccess);Assert.AreEqual(1,d.Inits);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);
        });
        [UnityTest] public IEnumerator InitFailureRetainsForCleanup() => Run(async () =>
        {var d=new Driver{InitFail=true};var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.AreEqual("injected.initialize",(await s.PrepareAsync()).Error.Code);Assert.AreEqual(1,b.RetainedSessionCount);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(1,d.Destroys);});
        [UnityTest] public IEnumerator ManifestFailureRetainsForCleanup() => Run(async () =>
        {var d=new Driver{ManifestFail=true};var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.AreEqual("injected.manifest",(await s.PrepareAsync()).Error.Code);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(0,d.Loads);});
        [UnityTest] public IEnumerator VersionMismatchPreventsAssetLoad() => Run(async () =>
        {var d=new Driver{ActualVersion="v2"};var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.AreEqual("yoo.package.version",(await s.PrepareAsync()).Error.Code);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(0,d.Loads);});
        [UnityTest] public IEnumerator MissingAssetPreventsReady() => Run(async () =>
        {var d=new Driver{Missing=true};var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.AreEqual("yoo.asset.missing",(await s.PrepareAsync()).Error.Code);Assert.IsFalse(b.CreateAssetProvider(new Ready()).IsSuccess);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);});
        [UnityTest] public IEnumerator LoadFailureReleasesActualHandle() => Run(async () =>
        {var d=new Driver();d.Handle.Success=false;var s=Backend(d).Create(Profile(),Candidate);Assert.AreEqual("yoo.package.first_play.load",(await s.PrepareAsync()).Error.Code);Assert.AreEqual(1,d.Handle.Releases);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);});
        [UnityTest] public IEnumerator WrongTypeReleasesActualHandle() => Run(async () =>
        {var d=new Driver();var go=new GameObject("wrong-type");d.Handle.Payload=go;try{var s=Backend(d).Create(Profile(),Candidate);Assert.AreEqual("yoo.package.first_play.payload",(await s.PrepareAsync()).Error.Code);Assert.AreEqual(1,d.Handle.Releases);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);}finally{Object.Destroy(go);}});
        [UnityTest] public IEnumerator ValidatorFailureAndThrowReleaseHandle() => Run(async () =>
        {
            foreach(bool throws in new[]{false,true}){var d=new Driver();var b=Backend(d,Definition(_=>throws?throw new InvalidOperationException("validator"):Result.Failure(Failure("payload.rejected"))));var s=b.Create(Profile(),Candidate);Assert.IsFalse((await s.PrepareAsync()).IsSuccess);Assert.AreEqual(1,d.Handle.Releases);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(0,b.RetainedSessionCount);}
        });
        [UnityTest] public IEnumerator ReleaseFailureRequiresExplicitCleanupRetry() => Run(async () =>
        {var d=new Driver();d.Handle.FailRelease=true;var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.AreEqual("yoo.package.first_play.release",(await s.PrepareAsync()).Error.Code);Assert.IsFalse((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(1,d.Handle.Releases);Assert.AreEqual(0,d.Destroys);d.Handle.FailRelease=false;Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(2,d.Handle.Releases);Assert.AreEqual(0,b.RetainedSessionCount);});
        [UnityTest] public IEnumerator DestroyFailureAndConcurrentCleanupRetry() => Run(async () =>
        {
            var d=new Driver{DestroyFail=true};var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.IsTrue((await s.PrepareAsync()).IsSuccess);Assert.IsFalse((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(1,b.RetainedSessionCount);
            d.DestroyGate=new UniTaskCompletionSource<Result>();var a=s.CleanupAsync().Preserve();var c=s.CleanupAsync().Preserve();Assert.AreEqual(2,d.Destroys);d.DestroyGate.TrySetResult(Result.Success());Assert.IsTrue((await a).IsSuccess);Assert.IsTrue((await c).IsSuccess);Assert.AreEqual(0,b.RetainedSessionCount);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(2,d.Destroys);
        });
        [UnityTest] public IEnumerator ProviderBindingUsesReadinessAndRetainedHandlesBlockDestroy() => Run(async () =>
        {
            var d=new Driver();var b=Backend(d);var s=b.Create(Profile(),Candidate);Assert.IsTrue((await s.PrepareAsync()).IsSuccess);
            Assert.IsFalse(b.CreateAssetProvider(new Ready{Allowed=false}).IsSuccess);var provider=b.CreateAssetProvider(new Ready()).Value;Assert.IsFalse(b.CreateAssetProvider(new Ready()).IsSuccess);
            var key=new ResourceKey("yooasset","pkg","v1","text",typeof(TextAsset));var value=await provider.BeginLoad<TextAsset>(key).Value.Completion;Assert.IsTrue(value.IsSuccess);
            Assert.AreEqual("yoo.package.provider.retained",(await s.CleanupAsync()).Error.Code);Assert.AreEqual(0,d.Destroys);
            Assert.AreEqual(ErrorKind.NotReady,provider.BeginLoad<TextAsset>(key).Error.Kind);
            Assert.IsTrue((await value.Value.BeginRelease(new CleanupContext(default, System.Threading.Timeout.InfiniteTimeSpan)).Value.Completion).IsSuccess);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);
        });
        [UnityTest] public IEnumerator OptionsExceptionIsCleanableAndNoSdkCreated() => Run(async () =>
        {var d=new Driver();var b=Backend(d,Definition(options:()=>throw new InvalidOperationException("options")));var s=b.Create(Profile(),Candidate);Assert.AreEqual("yoo.package.prepare.exception",(await s.PrepareAsync()).Error.Code);Assert.IsTrue((await s.CleanupAsync()).IsSuccess);Assert.AreEqual(0,d.Inits);Assert.AreEqual(0,d.Destroys);});
    }
}
