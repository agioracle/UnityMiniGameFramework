#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Providers.YooAsset;
using MiniFramework.Providers.Local;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MiniFramework.Providers.Tests
{
    public sealed class ProviderAdapterTests
    {
        internal const string Fixture = "Assets/Tests/S2Providers/Fixtures/probe.txt";
        private static CleanupContext Cleanup => new CleanupContext(CancellationToken.None, Timeout.InfiniteTimeSpan);
        private TextAsset value;
        [SetUp] public void SetUp() { value = new TextAsset("adapter payload"); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(value); }
        private static ResourceKey Key(string id="yooasset", string address="probe", Type type=null, string version="v1") => new ResourceKey(id,"main",version,address,type??typeof(TextAsset));
        private sealed class Handle : IYooHandle
        {
            public bool Done=true, Ok=true, ThrowRelease, ThrowLoadRead;
            public int Releases;
            public Object Payload;
            public bool IsDone => ThrowLoadRead ? throw new Exception("read failure") : Done;
            public bool Succeeded => Ok;
            public string Error => "controlled SDK error";
            public Object Value => Payload;
            public void Release() { Releases++; if(ThrowRelease)throw new Exception("release failure"); }
        }
        private sealed class Package : IYooPackage
        {
            internal Handle Handle;
            internal bool Ready=true, WrongVersion, ThrowStart, NullHandle;
            internal int Starts;
            public string Name => "main";
            public Result ValidateVersion(string version) => Ready ? (WrongVersion ? Result.Failure(new FrameworkError(ErrorKind.Integrity,"version.changed")) : Result.Success()) : Result.Failure(new FrameworkError(ErrorKind.NotReady,"not_ready"));
            public Result<IYooHandle> Load(string address, Type type) { Starts++; if(ThrowStart)throw new Exception("start");return Result<IYooHandle>.Success(NullHandle?null:Handle); }
        }
        private Package Backend() => new Package{Handle=new Handle{Payload=value}};
        private static async UniTask<IProviderAsset<TextAsset>> Load(YooAssetProvider p) => (await p.BeginLoad<TextAsset>(Key()).Value.Completion).Value;
        private static EditorLocalAssetProvider Local(IReadOnlyDictionary<string,string> map=null) => new EditorLocalAssetProvider("main","v1",map??new Dictionary<string,string>{{"probe",Fixture}});
        [Test] public void RejectsInvalidKeyWithoutStartingBackend() { var b=Backend();var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.InvalidArgument,p.BeginLoad<TextAsset>(default).Error.Kind);Assert.AreEqual(ErrorKind.InvalidArgument,p.BeginLoad<TextAsset>(Key(version:"v2")).Error.Kind);Assert.AreEqual(0,b.Starts); }
        [Test] public void NonUnityTypeIsExplicitlyUnsupported() { var p=new YooAssetProvider(Backend(),"v1");Assert.AreEqual(ErrorKind.NotSupported,p.BeginLoad<string>(Key(type:typeof(string))).Error.Kind); }
        [Test] public void NotReadyDoesNotStartSdk() { var b=Backend();b.Ready=false;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.NotReady,p.BeginLoad<TextAsset>(Key()).Error.Kind);Assert.AreEqual(0,b.Starts); }
        [Test] public void VersionMutationIsIntegrityFailure() { var b=Backend();b.WrongVersion=true;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Integrity,p.BeginLoad<TextAsset>(Key()).Error.Kind);Assert.AreEqual(0,b.Starts); }
        [Test] public void StartExceptionMapsProviderError() { var b=Backend();b.ThrowStart=true;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Provider,p.BeginLoad<TextAsset>(Key()).Error.Kind);Assert.AreEqual(0,p.RetainedHandleCount); }
        [Test] public void NullSdkHandleIsIntegrityFailure() { var b=Backend();b.NullHandle=true;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Integrity,p.BeginLoad<TextAsset>(Key()).Error.Kind); }
        [UnityTest] public IEnumerator ActualCompletionHasNoBusinessCancellation()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.Done=false;var p=new YooAssetProvider(b,"v1");var op=p.BeginLoad<TextAsset>(Key()).Value;await UniTask.Yield();Assert.AreEqual(BackendState.Running,op.State);Assert.AreEqual(1,p.RetainedHandleCount);b.Handle.Done=true;var asset=(await op.Completion).Value;Assert.AreSame(asset,(await op.Completion).Value);await asset.BeginRelease(Cleanup).Value.Completion;Assert.AreEqual(0,p.RetainedHandleCount);});
        [UnityTest] public IEnumerator ReleaseOnceAndGuardValue()=>UniTask.ToCoroutine(async()=> {var b=Backend();var p=new YooAssetProvider(b,"v1");var asset=await Load(p);Assert.AreSame(value,asset.Value);Assert.IsTrue((await asset.BeginRelease(Cleanup).Value.Completion).IsSuccess);Assert.AreEqual(1,b.Handle.Releases);Assert.Throws<InvalidOperationException>(()=>{var x=asset.Value;});Assert.AreEqual(ErrorKind.InvalidState,asset.BeginRelease(Cleanup).Error.Kind);Assert.AreEqual(ErrorKind.InvalidState,asset.RetryRelease(Cleanup).Error.Kind);});
        [UnityTest] public IEnumerator ReleaseFailureNeedsExplicitRetryAndKeepsOldResult()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.ThrowRelease=true;var p=new YooAssetProvider(b,"v1");var asset=await Load(p);var first=asset.BeginRelease(Cleanup).Value;Assert.AreEqual(ErrorKind.CleanupFailed,(await first.Completion).Error.Kind);Assert.AreSame(first,asset.BeginRelease(Cleanup).Value);Assert.AreEqual(1,b.Handle.Releases);b.Handle.ThrowRelease=false;Assert.IsTrue((await asset.RetryRelease(Cleanup).Value.Completion).IsSuccess);Assert.IsFalse((await first.Completion).IsSuccess);Assert.AreEqual(2,b.Handle.Releases);Assert.AreEqual(0,p.RetainedHandleCount);});
        [UnityTest] public IEnumerator FailedLoadReleasesSdkHandleBeforeReportingFailure()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.Ok=false;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Provider,(await p.BeginLoad<TextAsset>(Key()).Value.Completion).Error.Kind);Assert.AreEqual(1,b.Handle.Releases);Assert.AreEqual(0,p.RetainedHandleCount);});
        [UnityTest] public IEnumerator InvalidPayloadIsReclaimed()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.Payload=null;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Integrity,(await p.BeginLoad<TextAsset>(Key()).Value.Completion).Error.Kind);Assert.AreEqual(1,b.Handle.Releases);});
        [UnityTest] public IEnumerator CompletionReadExceptionStillReclaimsHandle()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.ThrowLoadRead=true;var p=new YooAssetProvider(b,"v1");Assert.AreEqual(ErrorKind.Provider,(await p.BeginLoad<TextAsset>(Key()).Value.Completion).Error.Kind);Assert.AreEqual(0,p.RetainedHandleCount);});
        [UnityTest] public IEnumerator FailedLoadCleanupIsRetainedForContextRetry()=>UniTask.ToCoroutine(async()=> {
            var b=Backend();b.Handle.Ok=false;b.Handle.ThrowRelease=true;var p=new YooAssetProvider(b,"v1");var context=new GameContext(new[]{new ModuleRegistration(p.Id,p.Dependencies,()=>p,typeof(IAssetProvider))},Cleanup);await context.StartAsync(OperationContext.Unlimited());
            Assert.AreEqual(ErrorKind.CleanupFailed,(await p.BeginLoad<TextAsset>(Key()).Value.Completion).Error.Kind);Assert.AreEqual(1,p.FailedLoadCleanupCount);Assert.IsFalse((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(1,b.Handle.Releases);
            b.Handle.ThrowRelease=false;Assert.IsTrue((await context.RetryCleanupAsync(Cleanup,OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(0,p.FailedLoadCleanupCount);Assert.AreEqual(0,p.RetainedHandleCount);
        });
        [UnityTest] public IEnumerator ProviderCloseWaitsActualLoadAndDoesNotForceLiveLease()=>UniTask.ToCoroutine(async()=> {var b=Backend();b.Handle.Done=false;var p=new YooAssetProvider(b,"v1");var op=p.BeginLoad<TextAsset>(Key()).Value;var close=p.CleanupAsync(Cleanup);Assert.AreEqual(UniTaskStatus.Pending,close.Status);Assert.AreEqual(ErrorKind.NotReady,p.BeginLoad<TextAsset>(Key()).Error.Kind);b.Handle.Done=true;var asset=(await op.Completion).Value;Assert.IsFalse((await close).IsSuccess);await asset.BeginRelease(Cleanup).Value.Completion;Assert.IsTrue((await p.CleanupAsync(Cleanup)).IsSuccess);});
        [Test] public void SceneCapabilitiesAreExplicitlyUnsupported() {var p=new YooAssetProvider(Backend(),"v1");Assert.IsFalse(p.SupportsDeferredActivation);Assert.AreEqual(ErrorKind.NotSupported,p.BeginLoadScene(ResourceKey.Scene("yooasset","main","v1","scene"),new SceneLoadOptions(SceneLoadMode.Additive,true)).Error.Kind);var l=Local();Assert.IsFalse(l.SupportsDeferredActivation);Assert.AreEqual(ErrorKind.NotSupported,l.BeginLoadScene(default,default).Error.Kind);}
        [UnityTest] public IEnumerator LocalMappingRejectsTraversalAndCopiesInput()=>UniTask.ToCoroutine(async()=> {Assert.Throws<ArgumentException>(()=>Local(new Dictionary<string,string>{{"probe","Assets/../secret"}}));var map=new Dictionary<string,string>{{"probe",Fixture}};var l=Local(map);map.Clear();var asset=(await l.BeginLoad<TextAsset>(Key("editor-local")).Value.Completion).Value;Assert.IsTrue((await asset.BeginRelease(Cleanup).Value.Completion).IsSuccess);});
        [Test] public void LocalNoFuzzyOrWrongVersionFallback() {var p=Local();Assert.AreEqual(ErrorKind.Unavailable,p.BeginLoad<TextAsset>(Key("editor-local","Probe")).Error.Kind);Assert.AreEqual(ErrorKind.InvalidArgument,p.BeginLoad<TextAsset>(Key("editor-local",version:"v2")).Error.Kind);Assert.AreEqual(0,p.LoadStartCount);}
        [UnityTest] public IEnumerator LocalReadsImportedAssetAndOnlyDropsReference()=>UniTask.ToCoroutine(async()=> {var p=Local();var op=p.BeginLoad<TextAsset>(Key("editor-local")).Value;Assert.AreEqual(BackendState.Running,op.State);var asset=(await op.Completion).Value;Assert.AreEqual("S2-03 local provider fixture",asset.Value.text.Trim());var imported=asset.Value;Assert.IsTrue((await asset.BeginRelease(Cleanup).Value.Completion).IsSuccess);Assert.IsTrue(imported!=null);Assert.AreSame(imported,AssetDatabase.LoadAssetAtPath<TextAsset>(Fixture));Assert.AreEqual(0,p.RetainedHandleCount);Assert.IsFalse(asset.BeginRelease(Cleanup).IsSuccess);});
        [UnityTest] public IEnumerator LocalMissingPathAndWrongTypeFailWithoutHandles()=>UniTask.ToCoroutine(async()=> {var p=Local(new Dictionary<string,string>{{"probe","Assets/Tests/S2Providers/Fixtures/missing.txt"}});Assert.AreEqual(ErrorKind.Unavailable,(await p.BeginLoad<TextAsset>(Key("editor-local")).Value.Completion).Error.Kind);p=Local();Assert.AreEqual(ErrorKind.Integrity,(await p.BeginLoad<Texture2D>(Key("editor-local",type:typeof(Texture2D))).Value.Completion).Error.Kind);Assert.AreEqual(0,p.RetainedHandleCount);});
        [UnityTest] public IEnumerator LocalServiceAllCanceledReclaimsLatePayload()=>UniTask.ToCoroutine(async()=> {var p=Local();var s=new AssetsService(p,"main","v1",Cleanup);var c=new GameContext(new[]{new ModuleRegistration(s.Id,s.Dependencies,()=>s)},Cleanup);await c.StartAsync(OperationContext.Unlimited());var owner=new Scope("local-late");var wait=s.Load<TextAsset>(Key("editor-local"),owner).Value.WaitAsync(OperationContext.Unlimited());var close=owner.CloseAsync(Cleanup);Assert.AreEqual(ErrorKind.Canceled,(await wait).Error.Kind);Assert.IsTrue((await close).IsFullyDrained);Assert.AreEqual(0,p.RetainedHandleCount);Assert.AreEqual(0,s.RetainedEntryCount);await c.CloseAsync(OperationContext.Unlimited());});
    }
}
#endif
