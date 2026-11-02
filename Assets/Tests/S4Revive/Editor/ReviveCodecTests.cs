using System;
using System.Linq;
using Game.Access;
using Game.Revive;
using MiniFramework.Contracts;
using NUnit.Framework;
public sealed class ReviveCodecTests
{
    static ClaimOwnerSnapshot Owner=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"installation-A",1,"local.a");
    static ReviveClaim Claim(string id="c",string attempt="a",string run="r",ReviveClaimState state=ReviveClaimState.Pending)=>new ReviveClaim(id,attempt,run,Owner,1,5,PlatformExecutionMode.Mock,state,state==ReviveClaimState.Granted?5:0);
    static ReviveSnapshot Pending=>new ReviveSnapshot(Owner.Mode,Owner.OwnerId,Owner.StorageNamespace,1,runId:"r",level:1,phase:ReviveRunPhase.Failed,claims:new[]{Claim()});
    [Test] public void ImmutableSnapshotRoundTripsAllClaimFields(){var x=ReviveCodec.Decode(ReviveCodec.Encode(Pending).Value).Value;Assert.AreEqual(Pending.OwnerId,x.OwnerId);Assert.AreEqual("a",x.Pending.AttemptId);Assert.IsTrue(x.Pending.Owner.SameAs(Owner));Assert.AreEqual(ReviveClaimState.Pending,x.Pending.State);}
    [Test] public void PayloadHasFrozenMagicAndMode(){var bytes=ReviveCodec.Encode(Pending).Value;CollectionAssert.AreEqual(new byte[]{77,82,86,49,0},bytes.Take(5));}
    [Test] public void EveryTruncationFailsClosed(){var bytes=ReviveCodec.Encode(Pending).Value;for(var i=0;i<bytes.Length;i++)Assert.IsFalse(ReviveCodec.Decode(bytes.Take(i).ToArray()).IsSuccess,"offset "+i);}
    [Test] public void TrailingBytesAreRejected(){Assert.IsFalse(ReviveCodec.Decode(ReviveCodec.Encode(Pending).Value.Concat(new byte[]{0}).ToArray()).IsSuccess);}
    [Test] public void UnknownOwnerModeIsRejected(){var b=ReviveCodec.Encode(Pending).Value;b[4]=88;Assert.IsFalse(ReviveCodec.Decode(b).IsSuccess);}
    [Test] public void InvalidUtf8IsRejected(){var b=ReviveCodec.Encode(Pending).Value;b[7]=255;Assert.IsFalse(ReviveCodec.Decode(b).IsSuccess);}
    [Test] public void OversizedPayloadIsRejected(){Assert.IsFalse(ReviveCodec.Decode(new byte[65537]).IsSuccess);}
    [Test] public void DuplicateClaimIdIsRejected(){Assert.Throws<ArgumentException>(()=>Pending.Copy(claims:new[]{Claim(),Claim(attempt:"other")}));}
    [Test] public void DuplicateAttemptIdIsRejected(){Assert.Throws<ArgumentException>(()=>Pending.Copy(claims:new[]{Claim(),Claim(id:"other")}));}
    [Test] public void SameRunCannotContainTwoGrantedClaims(){Assert.Throws<ArgumentException>(()=>Pending.Copy(coins:10,phase:ReviveRunPhase.Playing,used:true,claims:new[]{Claim(state:ReviveClaimState.Granted),Claim("other","other",state:ReviveClaimState.Granted)}));}
    [Test] public void GrantMarkerWithoutRewardIsRejected(){Assert.Throws<ArgumentException>(()=>Pending.Copy(phase:ReviveRunPhase.Playing,used:true,claims:new[]{Claim(state:ReviveClaimState.Granted)}));}
    [Test] public void WrongOwnerClaimIsRejected(){var c=new ReviveClaim("c","a","r",new ClaimOwnerSnapshot(Owner.Mode,"B",1,Owner.StorageNamespace),1,5,PlatformExecutionMode.Mock,ReviveClaimState.Pending);Assert.Throws<ArgumentException>(()=>Pending.Copy(claims:new[]{c}));}
    [Test] public void FutureGenerationClaimIsRejected(){Assert.Throws<ArgumentException>(()=>Pending.Copy(generation:0));}
    [Test] public void PendingClaimRequiresFailedRun(){Assert.Throws<ArgumentException>(()=>Pending.Copy(phase:ReviveRunPhase.Playing));}
    [Test] public void CollectedProgressCannotLagCurrentLevel(){Assert.Throws<ArgumentException>(()=>Pending.Copy(collected:true));}
    [Test] public void ClaimCapacityIsBounded(){var claims=Enumerable.Range(0,65).Select(i=>Claim("c"+i,"a"+i,"r"+i,ReviveClaimState.Aborted));Assert.Throws<ArgumentException>(()=>Pending.Copy(claims:claims));}
    [Test] public void CoinsCannotOverflowBudget(){Assert.Throws<ArgumentException>(()=>Pending.Copy(coins:ReviveSnapshot.MaxCoins+1));Assert.Throws<ArgumentException>(()=>Pending.Copy(coins:-1));}
    [Test] public void DebugStringsRedactIdentity(){Assert.IsFalse(Pending.ToString().Contains(Owner.OwnerId));Assert.IsFalse(Claim().ToString().Contains(Owner.OwnerId));}
    [Test] public void CopyDoesNotMutatePublishedSnapshot(){var old=Pending;var copy=old.Copy(generation:2);Assert.AreEqual(1,old.LastGeneration);Assert.AreEqual(2,copy.LastGeneration);Assert.AreEqual(1,old.Claims.Count);}
    [Test] public void TerminalSequenceCannotBeFabricatedInPending(){Assert.Throws<ArgumentException>(()=>new ReviveClaim("c","a","r",Owner,1,5,PlatformExecutionMode.Mock,ReviveClaimState.Pending,4));}
}
