using System;using System.Linq;using System.Collections.Generic;using WeChatWASM;using MiniFramework.Contracts;using MiniFramework.Providers.WeChat;using MiniFramework.Providers.WeChat.WxSdk;using NUnit.Framework;
public sealed class WeChatSdkBindingTests
{
    [Test] public void NativeApiCompositionFactoryRejectsEditorSimulation()
    {var r=WxSdkApi.CreateNativeApi();Assert.IsFalse(r.IsSuccess);Assert.AreEqual(ErrorKind.NotSupported,r.Error.Kind);Assert.AreEqual("wechat.native_host.required",r.Error.Code);}
    [Test] public void NativeSdk200MapsToPortableSuccess()
    {Assert.AreEqual(0,WxSdkApi.NormalizeInitializationResult(200));}
    [Test] public void NativeZeroIsNotSdkSuccess()
    {Assert.AreNotEqual(0,WxSdkApi.NormalizeInitializationResult(0));}
    [Test] public void NativeFailureRemainsFailure()
    {Assert.AreEqual(500,WxSdkApi.NormalizeInitializationResult(500));}
    [Test] public void RealSdkFactoryRejectsEditorSimulationAsNative()
    {var result=WxSdkApi.CreateNativeBackend();Assert.IsFalse(result.IsSuccess);Assert.AreEqual(ErrorKind.NotSupported,result.Error.Kind);Assert.AreEqual("wechat.native_host.required",result.Error.Code);}
    [Test] public void CommonOwnershipBridgeHasNoWxAssemblyDependency()
    {Assert.IsFalse(typeof(WeChatBackend).Assembly.GetReferencedAssemblies().Any(a=>a.Name=="Wx"||a.Name.StartsWith("wx-runtime")));}
    sealed class Env
    {
        internal LoginOption Copy;internal int Starts,Successes,Terminals;internal readonly List<Action> Pending=new List<Action>();
        internal readonly WxSdkApi.LoginRequest Request;
        internal Env(){Request=new WxSdkApi.LoginRequest(_=>Successes++,()=>{},()=>Terminals++,o=>{Starts++;Copy=new LoginOption{success=o.success,fail=o.fail,complete=o.complete};},a=>Pending.Add(a));}
        internal void Drain(){foreach(var a in Pending.ToArray())a();Pending.Clear();}
    }
    [Test] public void TypedRequestConstructionAndColdReleaseDoNotDispatchSdk()
    {var e=new Env();e.Request.Release();Assert.Throws<InvalidOperationException>(()=>e.Request.Start());Assert.AreEqual(0,e.Starts);}
    [Test] public void SuccessCannotReleaseBeforeSdkComplete()
    {var e=new Env();e.Request.Start();e.Copy.success(new LoginSuccessCallbackResult{code="synthetic"});Assert.AreEqual(1,e.Successes);Assert.Throws<InvalidOperationException>(()=>e.Request.Release());}
    [Test] public void TerminalDeliveryWaitsForSdkDispatchReturnBarrier()
    {var e=new Env();e.Request.Start();e.Copy.success(new LoginSuccessCallbackResult{code="synthetic"});e.Copy.complete(new GeneralCallbackResult());Assert.AreEqual(0,e.Terminals);e.Drain();Assert.AreEqual(1,e.Terminals);e.Request.Release();}
    [Test] public void CompleteOnlyRetainsLeaseUntilLateOutcomeAndExplicitRetry()
    {var e=new Env();e.Request.Start();e.Copy.complete(new GeneralCallbackResult());e.Drain();Assert.Throws<InvalidOperationException>(()=>e.Request.Release());e.Copy.success(new LoginSuccessCallbackResult{code="late-synthetic"});Assert.AreEqual(0,e.Successes);Assert.DoesNotThrow(()=>e.Request.Release());Assert.AreEqual(1,e.Starts);}
    [Test] public void ReleasedClonedCallbacksCannotRetainConsumerBehavior()
    {var e=new Env();e.Request.Start();e.Copy.success(new LoginSuccessCallbackResult{code="synthetic"});e.Copy.complete(new GeneralCallbackResult());e.Drain();e.Request.Release();e.Copy.success(new LoginSuccessCallbackResult{code="late"});e.Copy.complete(new GeneralCallbackResult());e.Drain();Assert.AreEqual(1,e.Successes);Assert.AreEqual(1,e.Terminals);}
    [Test] public void DuplicateCompleteSchedulesOnlyOneTerminal()
    {var e=new Env();e.Request.Start();e.Copy.fail(new LoginFailCallbackErr());e.Copy.complete(new GeneralCallbackResult());e.Copy.complete(new GeneralCallbackResult());Assert.AreEqual(1,e.Pending.Count);e.Drain();e.Request.Release();Assert.AreEqual(1,e.Terminals);}
}
