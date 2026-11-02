using System;
using System.Linq;
using MiniFramework.Contracts;
using MiniFramework.Ads;
using MiniFramework.Ads.Editor;
using MiniFramework.Providers.AdsMock;
using NUnit.Framework;
public sealed class AdsContractTests
{
    static ClaimOwnerSnapshot Owner=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"installation-A",1,"local.a");
    static RewardedAdRequest Request=>new RewardedAdRequest("revive","claim-A",Owner,true);
    static AdPresentationReceipt Receipt(string attempt="attempt-A",AdWatchOutcome outcome=AdWatchOutcome.Completed)=>new AdPresentationReceipt(attempt,Request,PlatformExecutionMode.Mock,outcome,true);
    [Test] public void OwnerRequiresExplicitIdentity(){Assert.Throws<ArgumentException>(()=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"",1,"local"));}
    [Test] public void OwnerRequiresPositiveGeneration(){Assert.Throws<ArgumentException>(()=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"owner",0,"local"));}
    [Test] public void OwnerRequiresStorageNamespace(){Assert.Throws<ArgumentException>(()=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"owner",1,"../escape"));}
    [Test] public void OwnerRejectsUnknownMode(){Assert.Throws<ArgumentException>(()=>new ClaimOwnerSnapshot((ClaimOwnerMode)99,"owner",1,"local"));}
    [Test] public void DefaultOwnerIsInvalid(){Assert.IsFalse(default(ClaimOwnerSnapshot).IsValid);Assert.Throws<ArgumentException>(()=>new RewardedAdRequest("revive","claim",default,true));}
    [Test] public void RequestRejectsInvalidPlacement(){Assert.Throws<ArgumentException>(()=>new RewardedAdRequest("../ad","claim",Owner,true));}
    [Test] public void RequestRejectsInvalidClaim(){Assert.Throws<ArgumentException>(()=>new RewardedAdRequest("revive","private claim",Owner,true));}
    [Test] public void RequestFreezesAllFields(){var r=Request;Assert.AreEqual("claim-A",r.ClaimId);Assert.AreEqual("revive",r.PlacementId);Assert.IsTrue(r.Owner.SameAs(Owner));Assert.IsTrue(r.UserInitiated);}
    [Test] public void OwnerEqualityIncludesNamespaceAndGeneration(){Assert.IsFalse(Owner.SameAs(new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,Owner.OwnerId,2,Owner.StorageNamespace)));Assert.IsFalse(Owner.SameAs(new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,Owner.OwnerId,1,"local.b")));}
    [Test] public void DebugStringsRedactBusinessIdentity(){foreach(var text in new[]{Owner.ToString(),Request.ToString(),Receipt().ToString()}){Assert.IsFalse(text.Contains("installation-A"));Assert.IsFalse(text.Contains("claim-A"));}}
    [Test] public void CompletionCannotExistWithoutShow(){Assert.Throws<ArgumentException>(()=>new AdPresentationReceipt("a",Request,PlatformExecutionMode.Mock,AdWatchOutcome.Completed,false));}
    [Test] public void SkippedCannotExistWithoutShow(){Assert.Throws<ArgumentException>(()=>new AdPresentationReceipt("a",Request,PlatformExecutionMode.Mock,AdWatchOutcome.Skipped,false));}
    [Test] public void ReceiptPreservesFixedOwner(){var r=Receipt();Assert.IsTrue(r.Owner.SameAs(Owner));Assert.AreEqual("claim-A",r.ClaimId);Assert.AreEqual(PlatformExecutionMode.Mock,r.Mode);}
    [Test] public void SkippedIsNotCompletionObserved(){Assert.IsFalse(Receipt(outcome:AdWatchOutcome.Skipped).IsCompletionObserved);Assert.IsTrue(Receipt().IsCompletionObserved);}
    [Test] public void CallbackRequiresAttemptGenerationAndSequence(){Assert.Throws<ArgumentException>(()=>new AdCallback("","a",1,AdCallbackKind.Started));Assert.Throws<ArgumentException>(()=>new AdCallback("g","",1,AdCallbackKind.Started));Assert.Throws<ArgumentException>(()=>new AdCallback("g","a",0,AdCallbackKind.Started));}
    [Test] public void FailedCallbackRequiresError(){Assert.Throws<ArgumentException>(()=>new AdCallback("g","a",1,AdCallbackKind.Failed));}
    [Test] public void CallbackRejectsUnknownKind(){Assert.Throws<ArgumentException>(()=>new AdCallback("g","a",1,(AdCallbackKind)90));}
    [Test] public void CallbackStringRedactsCorrelation(){Assert.IsFalse(new AdCallback("secret-g","secret-a",1,AdCallbackKind.Completed).ToString().Contains("secret"));}
    [Test] public void JournalRecordIsAttemptIdempotent(){var j=new AdReceiptJournal();Assert.IsTrue(j.Record(Receipt()).IsSuccess);Assert.IsTrue(j.Record(Receipt()).IsSuccess);Assert.AreEqual(1,j.PendingCount);}
    [Test] public void JournalRejectsConflictingAttempt(){var j=new AdReceiptJournal();j.Record(Receipt());Assert.AreEqual(ErrorKind.Integrity,j.Record(Receipt(outcome:AdWatchOutcome.Skipped)).Error.Kind);}
    [Test] public void JournalCapacityBackpressures(){var j=new AdReceiptJournal(1,2);j.Record(Receipt());Assert.AreEqual(ErrorKind.Unavailable,j.Record(Receipt("second")).Error.Kind);}
    [Test] public void AcknowledgementFreesPendingButPreservesDedup(){var j=new AdReceiptJournal(1,2);j.Record(Receipt());Assert.IsTrue(j.Acknowledge("attempt-A").IsSuccess);j.Record(Receipt());Assert.AreEqual(0,j.PendingCount);Assert.IsTrue(j.Record(Receipt("second")).IsSuccess);}
    [Test] public void JournalTrackerIsBoundedAfterAcknowledge(){var j=new AdReceiptJournal(1,1);j.Record(Receipt());j.Acknowledge("attempt-A");Assert.AreEqual(ErrorKind.Unavailable,j.Record(Receipt("second")).Error.Kind);}
    [Test] public void JournalRejectsUnknownAcknowledgement(){Assert.AreEqual(ErrorKind.InvalidArgument,new AdReceiptJournal().Acknowledge("unknown").Error.Kind);}
    [Test] public void JournalRejectsNullReceipt(){Assert.AreEqual(ErrorKind.InvalidArgument,new AdReceiptJournal().Record(null).Error.Kind);}
    [Test] public void LimitsRejectUnboundedOrZeroValues(){Assert.Throws<ArgumentOutOfRangeException>(()=>new AdsLimits(0));Assert.Throws<ArgumentOutOfRangeException>(()=>new AdsLimits(queueCapacity:2,callbacksPerFrame:3));Assert.Throws<ArgumentOutOfRangeException>(()=>new AdReceiptJournal(2,1));}
    [Test] public void MockFactoryIsExplicitAndCold(){var b=EditorAdsFactory.CreateMock();Assert.AreEqual(0,b.Attempts.Count);Assert.AreEqual(PlatformExecutionMode.Mock,b.Descriptor.Mode);}
    [Test] public void BackendReceivesNoOwnerOrClaim(){var create=typeof(IRewardedAdBackend).GetMethod("Create");Assert.IsFalse(create.GetParameters().Any(p=>p.ParameterType==typeof(RewardedAdRequest)||p.ParameterType==typeof(ClaimOwnerSnapshot)));Assert.IsFalse(typeof(IAdsService).GetMethods().Any(m=>m.Name.Contains("Grant")));}
    [Test] public void OwnerFieldsHaveNoSetters(){foreach(var p in typeof(ClaimOwnerSnapshot).GetProperties())Assert.IsFalse(p.CanWrite,p.Name);foreach(var p in typeof(RewardedAdRequest).GetProperties())Assert.IsFalse(p.CanWrite,p.Name);}
    [Test] public void AccountOwnerContractIsExplicitReservedShape(){var o=new ClaimOwnerSnapshot(ClaimOwnerMode.AccountScoped,"account-A",4,"account.a");Assert.IsTrue(o.IsValid);Assert.IsFalse(o.SameAs(Owner));}
}
