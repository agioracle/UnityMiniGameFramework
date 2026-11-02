using System;
using System.Linq;
using MiniFramework.Contracts;
using MiniFramework.Ownership;
using MiniFramework.Accounts;
using MiniFramework.Providers.IdentityFake;
using NUnit.Framework;
public sealed class OwnershipContractTests
{
    static BusinessIdentity A=>new BusinessIdentity("fake-identity","account-A",IdentityMode.Simulated);
    static OwnershipPolicy Local=>OwnershipPolicy.LocalOnly("install-A","local.a");
    [Test] public void LocalModeIsExplicit(){Assert.AreEqual(ClaimOwnerMode.LocalOnly,Local.Mode);Assert.IsFalse(Local.AllowSimulatedIdentity);}
    [Test] public void LocalModeRequiresStableOwner(){Assert.Throws<ArgumentException>(()=>OwnershipPolicy.LocalOnly("","local.a"));}
    [Test] public void LocalNamespaceRejectsTraversal(){Assert.Throws<ArgumentException>(()=>OwnershipPolicy.LocalOnly("install-A","local../a"));}
    [Test] public void LocalNamespaceCannotUseAccountPrefix(){Assert.Throws<ArgumentException>(()=>OwnershipPolicy.LocalOnly("install-A","account.a"));}
    [Test] public void LocalNamespaceRejectsUppercase(){Assert.Throws<ArgumentException>(()=>OwnershipPolicy.LocalOnly("install-A","Local.a"));}
    [Test] public void LocalNamespaceRejectsOversize(){Assert.Throws<ArgumentException>(()=>OwnershipPolicy.LocalOnly("install-A","local."+new string('a',60)));}
    [Test] public void LocalCompositionDisablesAccountModule(){Assert.IsTrue(Local.ValidateComposition(false,true).IsSuccess);Assert.IsFalse(Local.ValidateComposition(true,true).IsSuccess);}
    [Test] public void LocalCompositionRequiresLocalAuthority(){Assert.IsFalse(Local.ValidateComposition(false,false).IsSuccess);}
    [Test] public void AccountCompositionDisablesLocalModule(){var p=OwnershipPolicy.AccountScoped();Assert.IsTrue(p.ValidateComposition(true,false).IsSuccess);Assert.IsFalse(p.ValidateComposition(true,true).IsSuccess);}
    [Test] public void AccountCompositionRequiresAccountModule(){Assert.IsFalse(OwnershipPolicy.AccountScoped().ValidateComposition(false,false).IsSuccess);}
    [Test] public void ProductionPolicyRejectsSimulatedIdentity(){Assert.AreEqual(ErrorKind.PermissionDenied,OwnershipPolicy.AccountScoped().NamespaceFor(A).Error.Kind);}
    [Test] public void TestPolicyAllowsExplicitSimulation(){Assert.IsTrue(OwnershipPolicy.AccountScoped(true).NamespaceFor(A).IsSuccess);}
    [Test] public void NamespaceIsStableAndPortable(){var p=OwnershipPolicy.AccountScoped(true);var n=p.NamespaceFor(A).Value;Assert.AreEqual(n,p.NamespaceFor(A).Value);Assert.IsTrue(StorageIdentifier.IsValid(n));Assert.AreEqual(60,n.Length);}
    [Test] public void NamespaceKeepsDifferentOwnersSeparate(){var p=OwnershipPolicy.AccountScoped(true);Assert.AreNotEqual(p.NamespaceFor(A).Value,p.NamespaceFor(new BusinessIdentity(A.Issuer,"account-B",A.Mode)).Value);}
    [Test] public void NamespaceKeepsIssuersSeparate(){var p=OwnershipPolicy.AccountScoped(true);Assert.AreNotEqual(p.NamespaceFor(A).Value,p.NamespaceFor(new BusinessIdentity("another-issuer",A.OwnerId,A.Mode)).Value);}
    [Test] public void OwnerIdsAreCaseSensitive(){var p=OwnershipPolicy.AccountScoped(true);Assert.AreNotEqual(p.NamespaceFor(A).Value,p.NamespaceFor(new BusinessIdentity(A.Issuer,"account-a",A.Mode)).Value);}
    [Test] public void LocalPolicyCannotDeriveAccountNamespace(){Assert.AreEqual(ErrorKind.InvalidArgument,Local.NamespaceFor(A).Error.Kind);}
    [Test] public void NullIdentityDoesNotCreateNamespace(){Assert.AreEqual(ErrorKind.InvalidArgument,OwnershipPolicy.AccountScoped().NamespaceFor(null).Error.Kind);}
    [Test] public void IdentityRejectsInvalidIssuer(){Assert.Throws<ArgumentException>(()=>new BusinessIdentity("../issuer","A",IdentityMode.Simulated));}
    [Test] public void IdentityRejectsInvalidSubject(){Assert.Throws<ArgumentException>(()=>new BusinessIdentity("issuer","private subject",IdentityMode.Simulated));}
    [Test] public void IdentityRejectsUnknownMode(){Assert.Throws<ArgumentException>(()=>new BusinessIdentity("issuer","A",(IdentityMode)44));}
    [Test] public void IdentityStringRedactsOwner(){Assert.IsFalse(A.ToString().Contains("account-A"));}
    [Test] public void IdentityAndPolicyFieldsAreImmutable(){Assert.IsTrue(typeof(BusinessIdentity).GetProperties().All(p=>!p.CanWrite));Assert.IsTrue(typeof(OwnershipPolicy).GetProperties().All(p=>!p.CanWrite));}
    [Test] public void GenerationSourceIsMonotonic(){var g=new OwnerGenerationSequence();Assert.AreEqual(1,g.Reserve().Value);Assert.AreEqual(2,g.Reserve().Value);}
    [Test] public void GenerationCanResumeFromExplicitSeed(){Assert.AreEqual(800,new OwnerGenerationSequence(799).Reserve().Value);}
    [Test] public void GenerationExhaustionFailsInsteadOfWrapping(){Assert.AreEqual(ErrorKind.Integrity,new OwnerGenerationSequence(long.MaxValue).Reserve().Error.Kind);}
    [Test] public void GenerationRejectsNegativeSeed(){Assert.Throws<ArgumentOutOfRangeException>(()=>new OwnerGenerationSequence(-1));}
    [Test] public void FakeIdentityIsExplicitAndCold(){var p=new FakeIdentityProvider();Assert.AreEqual(IdentityMode.Simulated,p.Mode);Assert.AreEqual(0,p.Attempts.Count);Assert.AreEqual("fake-identity",p.Id);}
    [Test] public void LimitsRejectUnboundedSessions(){Assert.Throws<ArgumentOutOfRangeException>(()=>new AccountLimits(maxSessions:0));Assert.Throws<ArgumentOutOfRangeException>(()=>new AccountLimits(maxOperations:129));}
    [Test] public void NamespaceMatchesFrozenSha256Base32Vector(){Assert.AreEqual("account.n2cvko2gwuh5wlwxkuxsrphlf3ktlgziwtmt2owcjjyrxuveprmq",OwnershipPolicy.AccountScoped(true).NamespaceFor(A).Value);}
}
