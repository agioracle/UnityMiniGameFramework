using System;
using System.Collections.Generic;
using MiniFramework.Providers.WeChat.WxSdk;
using NUnit.Framework;
using WeChatWASM;
public sealed class CSharpMemoryWarningTests
{
    sealed class Env
    {
        public readonly List<Action<OnMemoryWarningListenerResult>> Live=new List<Action<OnMemoryWarningListenerResult>>();
        public readonly WxSdkApi.MemoryWarningBinding Binding;
        public int Ons,Offs;public bool ThrowOnAfter,ThrowOff;
        public Env(){Binding=new WxSdkApi.MemoryWarningBinding(c=>{Ons++;Live.Add(c);if(ThrowOnAfter)throw new InvalidOperationException();},c=>{Offs++;if(ThrowOff)throw new InvalidOperationException();Assert.Contains(c,Live);Live.Remove(c);});}
    }
    [Test] public void ConstructionIsColdAndDuplicateRegistrationDoesNotDispatchTwice(){var env=new Env();Action callback=()=>{};Assert.AreEqual(0,env.Ons);env.Binding.On(callback);Assert.Throws<InvalidOperationException>(()=>env.Binding.On(callback));Assert.AreEqual(1,env.Ons);env.Binding.Off(callback);Assert.AreEqual(0,env.Binding.Count);}
    [Test] public void TypedNotificationDoesNotRequireSeverityMapping(){var env=new Env();int count=0;Action callback=()=>count++;env.Binding.On(callback);env.Live[0](new OnMemoryWarningListenerResult());env.Live[0](null);Assert.AreEqual(2,count);env.Binding.Off(callback);}
    [Test] public void SuccessfulOffFencesSavedDelegateAndIsIdempotent(){var env=new Env();int count=0;Action callback=()=>count++;env.Binding.On(callback);var stale=env.Live[0];env.Binding.Off(callback);env.Binding.Off(callback);stale(null);Assert.AreEqual(0,count);Assert.AreEqual(1,env.Offs);}
    [Test] public void FailedOffKeepsExactTypedDelegateForRetry(){var env=new Env();Action callback=()=>{};env.Binding.On(callback);var bound=env.Live[0];env.ThrowOff=true;Assert.Throws<InvalidOperationException>(()=>env.Binding.Off(callback));Assert.AreEqual(1,env.Binding.Count);Assert.AreSame(bound,env.Live[0]);env.ThrowOff=false;env.Binding.Off(callback);Assert.IsEmpty(env.Live);Assert.AreEqual(2,env.Offs);}
    [Test] public void ThrowAfterRegistrationStillAllowsExplicitRollback(){var env=new Env{ThrowOnAfter=true};Action callback=()=>{};Assert.Throws<InvalidOperationException>(()=>env.Binding.On(callback));Assert.AreEqual(1,env.Binding.Count);env.Binding.Off(callback);Assert.AreEqual(0,env.Binding.Count);Assert.IsEmpty(env.Live);}
    [Test] public void RebindCannotReviveOldTypedDelegate(){var env=new Env();int count=0;Action callback=()=>count++;env.Binding.On(callback);var stale=env.Live[0];env.Binding.Off(callback);env.Binding.On(callback);stale(null);Assert.AreEqual(0,count);env.Live[0](null);Assert.AreEqual(1,count);env.Binding.Off(callback);}
    [Test] public void IndependentObserversReleaseOnlyTheirOwnTypedDelegate(){var env=new Env();int a=0,b=0;Action ca=()=>a++,cb=()=>b++;env.Binding.On(ca);env.Binding.On(cb);env.Binding.Off(ca);env.Live[0](null);Assert.AreEqual(0,a);Assert.AreEqual(1,b);env.Binding.Off(cb);Assert.IsEmpty(env.Live);}
}
