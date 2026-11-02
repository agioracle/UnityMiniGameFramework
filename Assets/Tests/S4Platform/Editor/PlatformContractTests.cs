using System;
using MiniFramework.Contracts;
using MiniFramework.Platform;
using MiniFramework.Platform.Editor;
using MiniFramework.Providers.PlatformMock;
using NUnit.Framework;
public sealed class PlatformContractTests
{
    static PlatformLoginCredential Credential(DateTimeOffset? expires=null)=>new PlatformLoginCredential("editor-mock","attempt",PlatformExecutionMode.Mock,"private-code",expires);
    [Test] public void EditorFactoryIsExplicitMock(){var backend=EditorPlatformFactory.CreateMock();Assert.AreEqual(PlatformExecutionMode.Mock,backend.Descriptor.Mode);Assert.AreEqual("editor-mock",backend.Descriptor.Id);}
    [Test] public void EditorFactoryCreatesIndependentInstances(){Assert.AreNotSame(EditorPlatformFactory.CreateMock(),EditorPlatformFactory.CreateMock());}
    [Test] public void MockFactoryAndBackendConstructionAreCold(){var b=(MockPlatformBackend)EditorPlatformFactory.CreateMock();Assert.AreEqual(0,b.Sessions.Count);}
    [Test] public void DescriptorRejectsUnstableId(){Assert.Throws<ArgumentException>(()=>new PlatformDescriptor("WeChat",PlatformExecutionMode.Native,"v1"));}
    [Test] public void UnknownFeatureDoesNotBecomeSupported(){var p=new PlatformCapabilities(Array.Empty<PlatformCapability>()).Get(PlatformFeature.Storage);Assert.AreEqual(CapabilitySupport.Unknown,p.Support);Assert.AreEqual(CapabilityAvailability.Unknown,p.Availability);Assert.IsFalse(p.IsUsable);}
    [Test] public void SupportAndAvailabilityStaySeparate(){var p=new PlatformCapability(PlatformFeature.LoginCredential,CapabilitySupport.Supported,CapabilityAvailability.Unavailable,"platform.offline");Assert.AreEqual(CapabilitySupport.Supported,p.Support);Assert.IsFalse(p.IsUsable);}
    [Test] public void UnsupportedAvailableIsInvalid(){Assert.Throws<ArgumentException>(()=>new PlatformCapability(PlatformFeature.Share,CapabilitySupport.Unsupported,CapabilityAvailability.Available,"platform.invalid"));}
    [Test] public void UnknownAvailableIsInvalid(){Assert.Throws<ArgumentException>(()=>new PlatformCapability(PlatformFeature.Share,CapabilitySupport.Unknown,CapabilityAvailability.Available,"platform.invalid"));}
    [Test] public void PermissionDeniedIsExplicit(){var p=MockPlatformBackend.DefaultCapabilities(loginAvailability:CapabilityAvailability.PermissionDenied).Get(PlatformFeature.LoginCredential);Assert.AreEqual(CapabilityAvailability.PermissionDenied,p.Availability);Assert.AreEqual(CapabilitySupport.Supported,p.Support);}
    [Test] public void DuplicateCapabilitiesReject(){var p=MockPlatformBackend.DefaultCapabilities().Get(PlatformFeature.Lifecycle);Assert.Throws<ArgumentException>(()=>new PlatformCapabilities(new[]{p,p}));}
    [Test] public void CapabilitiesCopyCallerArray(){var a=new[]{MockPlatformBackend.DefaultCapabilities().Get(PlatformFeature.LoginCredential)};var p=new PlatformCapabilities(a);a[0]=new PlatformCapability(PlatformFeature.LoginCredential,CapabilitySupport.Unsupported,CapabilityAvailability.Unavailable,"platform.changed");Assert.IsTrue(p.Get(PlatformFeature.LoginCredential).IsUsable);}
    [Test] public void DefaultMockDoesNotClaimAdsStorageOrAccounts(){var p=MockPlatformBackend.DefaultCapabilities();Assert.AreEqual(CapabilitySupport.Unsupported,p.Get(PlatformFeature.RewardedAds).Support);Assert.AreEqual(CapabilitySupport.Unsupported,p.Get(PlatformFeature.Storage).Support);Assert.IsFalse(typeof(PlatformLoginCredential).GetProperty("OwnerId")!=null);}
    [Test] public void CapabilityRetainsConstraintsAndReason(){var c=MockPlatformBackend.DefaultCapabilities().Get(PlatformFeature.LoginCredential);Assert.AreEqual(1,c.MaxConcurrent);Assert.AreEqual("platform.mock.login",c.ReasonCode);Assert.AreNotEqual(CapabilityConstraints.None,c.Constraints);}
    [Test] public void DefaultCapabilityCannotEnterSnapshot(){Assert.Throws<ArgumentException>(()=>new PlatformCapabilities(new[]{default(PlatformCapability)}));}
    [Test] public void InvalidFeatureLookupRejects(){Assert.Throws<ArgumentOutOfRangeException>(()=>MockPlatformBackend.DefaultCapabilities().Get((PlatformFeature)99));}
    [Test] public void CredentialContainsCodeButNotBusinessAccount(){using(var c=Credential()){Assert.AreEqual("private-code",c.ReadAuthorizationCode().Value);Assert.AreEqual(PlatformExecutionMode.Mock,c.Mode);Assert.IsFalse(c.ToString().Contains("private-code"));}}
    [Test] public void CredentialDisposeIsIdempotentAndRevokesRead(){var c=Credential();c.Dispose();c.Dispose();Assert.IsFalse(c.ReadAuthorizationCode().IsSuccess);Assert.IsTrue(c.IsDisposed);}
    [Test] public void ExpiredCredentialCannotBeRead(){using(var c=Credential(DateTimeOffset.UtcNow.AddMinutes(-1)))Assert.AreEqual("platform.credential.expired",c.ReadAuthorizationCode().Error.Code);}
    [Test] public void UnknownExpiryIsNotInvented(){using(var c=Credential())Assert.IsNull(c.ExpiresAtUtc);}
    [Test] public void ClaimIsExactlyOnce(){using(var c=Credential()){Assert.IsTrue(c.TryClaim());Assert.IsFalse(c.TryClaim());}}
    [Test] public void DiscardCannotRevokeAlreadyClaimedCredential(){using(var c=Credential()){c.TryClaim();c.DiscardUnclaimed();Assert.IsTrue(c.ReadAuthorizationCode().IsSuccess);}}
    [Test] public void UnclaimedDiscardRevokesPayload(){var c=Credential();c.DiscardUnclaimed();Assert.IsTrue(c.IsDisposed);Assert.IsFalse(c.TryClaim());}
    [Test] public void CredentialRequiresUtcExpiry(){Assert.Throws<ArgumentException>(()=>Credential(new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.FromHours(8))));}
    [Test] public void CredentialRejectsNullOrExcessiveCode(){Assert.Throws<ArgumentException>(()=>new PlatformLoginCredential("editor-mock","attempt",PlatformExecutionMode.Mock,null));Assert.Throws<ArgumentException>(()=>new PlatformLoginCredential("editor-mock","attempt",PlatformExecutionMode.Mock,new string('x',16385)));}
    [Test] public void LoginCallbackToStringIsRedacted(){using(var c=Credential()){var callback=PlatformCallback.Login("generation","attempt",Result<PlatformLoginCredential>.Success(c));Assert.IsFalse(callback.ToString().Contains("private-code"));callback.Discard();Assert.IsTrue(c.IsDisposed);}}
    [Test] public void CallbackRejectsNullSuccessfulPayload(){Assert.Throws<ArgumentException>(()=>PlatformCallback.Login("generation","attempt",Result<PlatformLoginCredential>.Success(null)));}
    [Test] public void LifecycleSequenceMustBePositive(){Assert.Throws<ArgumentException>(()=>PlatformCallback.LifecycleChanged("g",0,PlatformLifecycleKind.Shown));}
    [Test] public void StartupRejectsNegativeSequence(){Assert.Throws<ArgumentException>(()=>new PlatformStartup(MockPlatformBackend.DefaultCapabilities(),PlatformVisibility.Foreground,-1));}
    [Test] public void PlatformLimitsAreBounded(){var l=new PlatformLimits();Assert.AreEqual(256,l.QueueCapacity);Assert.AreEqual(32,l.CallbacksPerFrame);Assert.AreEqual(16,l.MaxOperations);Assert.AreEqual(32,l.MaxCredentials);}
    [Test] public void InvalidLimitsReject(){Assert.Throws<ArgumentOutOfRangeException>(()=>new PlatformLimits(callbacksPerFrame:257));Assert.Throws<ArgumentOutOfRangeException>(()=>new PlatformLimits(maxCredentials:0));}
}
