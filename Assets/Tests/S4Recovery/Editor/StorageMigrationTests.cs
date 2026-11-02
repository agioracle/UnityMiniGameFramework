using System;
using System.Linq;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using NUnit.Framework;
public sealed class StorageMigrationTests
{
    sealed class Data{}
    static readonly StorageAddress Address=new StorageAddress("local","main");
    static StorageModuleRegistration Module(int schema=3,string codec="bytes-v3",Func<byte[],Result> validate=null)=>new StorageModuleRegistration("progress",schema,codec,true,typeof(Data),()=>Result<byte[]>.Success(new byte[]{7,0,9}),validate??(_=>Result.Success()));
    static StorageContainer Source(int schema=1,string codec="bytes-v1",bool required=true)=>new StorageContainer(Address,4,new[]{new StorageRecord("progress",schema,codec,required,new byte[]{7})});
    static StorageMigrationStep Step(int v,Func<byte[],Result<byte[]>> transform=null)=>new StorageMigrationStep("progress",v,"bytes-v"+v,"bytes-v"+(v+1),transform??(b=>Result<byte[]>.Success(b.Concat(new byte[]{v==1?(byte)0:(byte)9}).ToArray())));
    static Result<StorageContainer> Prepare(StorageRecovery recovery,StorageContainer source=null,StorageModuleRegistration module=null)=>recovery.Prepare(source??Source(),new[]{module??Module()},new StorageLimits());
    [Test] public void FullChainAddsExplicitDefaultsOnCopy(){var r=new StorageMigrationRegistry();r.Register(Step(1));r.Register(Step(2));var source=Source();var c=Prepare(new StorageRecovery(r),source).Value;CollectionAssert.AreEqual(new byte[]{7,0,9},c.Records[0].CopyPayload());CollectionAssert.AreEqual(new byte[]{7},source.Records[0].CopyPayload());Assert.AreEqual(4,c.Sequence);}
    [Test] public void MissingStepRejects(){var r=new StorageMigrationRegistry();r.Register(Step(1));Assert.AreEqual("storage.migration.missing",Prepare(new StorageRecovery(r)).Error.Code);}
    [Test] public void ThrowAfterEarlierStepPreservesSource(){var r=new StorageMigrationRegistry();r.Register(Step(1));r.Register(Step(2,_=>throw new Exception()));var source=Source();Assert.AreEqual("storage.migration.exception",Prepare(new StorageRecovery(r),source).Error.Code);CollectionAssert.AreEqual(new byte[]{7},source.Records[0].CopyPayload());}
    [Test] public void MutatingFailedTransformCannotChangeSource(){var r=new StorageMigrationRegistry();r.Register(Step(1,b=>{b[0]=99;return Result<byte[]>.Failure(new FrameworkError(ErrorKind.Integrity,"test.fail"));}));var source=Source();Assert.IsFalse(Prepare(new StorageRecovery(r),source).IsSuccess);Assert.AreEqual(7,source.Records[0].CopyPayload()[0]);}
    [Test] public void NullTransformOutputRejected(){var r=new StorageMigrationRegistry();r.Register(Step(1,_=>Result<byte[]>.Success(null)));Assert.AreEqual("storage.migration.payload",Prepare(new StorageRecovery(r)).Error.Code);}
    [Test] public void OversizedTransformOutputRejected(){var r=new StorageMigrationRegistry();r.Register(Step(1,_=>Result<byte[]>.Success(new byte[65537])));Assert.AreEqual("storage.migration.payload",Prepare(new StorageRecovery(r)).Error.Code);}
    [Test] public void FinalValidationFailsWholePlan(){var r=new StorageMigrationRegistry();r.Register(Step(1));r.Register(Step(2));Assert.AreEqual("test.invalid",Prepare(new StorageRecovery(r),module:Module(validate:_=>Result.Failure(new FrameworkError(ErrorKind.Integrity,"test.invalid")))).Error.Code);}
    [Test] public void DuplicateMigrationStartRejected(){var r=new StorageMigrationRegistry();Assert.IsTrue(r.Register(Step(1)).IsSuccess);Assert.AreEqual("storage.migration.duplicate",r.Register(Step(1)).Error.Code);}
    [Test] public void FrozenRegistryRejectsFurtherSteps(){var r=new StorageMigrationRegistry();new StorageRecovery(r);Assert.AreEqual("storage.migration.frozen",r.Register(Step(1)).Error.Code);}
    [Test] public void VersionStepCannotOverflow(){Assert.Throws<ArgumentException>(()=>new StorageMigrationStep("progress",int.MaxValue,"bytes","bytes",b=>Result<byte[]>.Success(b)));}
    [Test] public void FutureSchemaCannotDowngrade(){Assert.AreEqual("storage.module.version",Prepare(new StorageRecovery(),Source(4)).Error.Code);}
    [Test] public void CodecChangeNeedsExplicitVersionStep(){Assert.AreEqual("storage.module.version",Prepare(new StorageRecovery(),Source(3,"other")).Error.Code);}
    [Test] public void RequiredMarkerCannotSilentlyChange(){Assert.IsFalse(Prepare(new StorageRecovery(),Source(3,"bytes-v3",false)).IsSuccess);}
    [Test] public void UnknownOptionalRecordSurvivesPlan(){var c=new StorageContainer(Address,1,new[]{new StorageRecord("progress",3,"bytes-v3",true,new byte[]{7}),new StorageRecord("unknown",9,"opaque",false,new byte[]{99})});var result=Prepare(new StorageRecovery(),c).Value;Assert.AreEqual(99,result.Records.Single(r=>r.ModuleId=="unknown").CopyPayload()[0]);}
    [Test] public void UnknownRequiredRecordBlocks(){var c=new StorageContainer(Address,1,new[]{new StorageRecord("unknown",1,"opaque",true,new byte[]{9})});Assert.AreEqual("storage.module.unknown_required",Prepare(new StorageRecovery(),c).Error.Code);}
    [Test] public void MissingRequiredRecordBlocks(){Assert.AreEqual("storage.module.missing_required",Prepare(new StorageRecovery(),new StorageContainer(Address,1,Array.Empty<StorageRecord>())).Error.Code);}
    [Test] public void ReadSnapshotReturnsCopiesAndMissingIsExplicit(){var p=new StorageReadSnapshot(4,Source().Records);var b=p.Read("progress").Value;b[0]=99;Assert.AreEqual(7,p.Read("progress").Value[0]);Assert.IsFalse(p.Read("other").IsSuccess);Assert.IsFalse(p.IsNew);Assert.AreEqual(4,p.Sequence);}
    [Test] public void NewSnapshotHasNoInventedData(){var p=new StorageReadSnapshot(0,Array.Empty<StorageRecord>());Assert.IsTrue(p.IsNew);Assert.IsFalse(p.Read("progress").IsSuccess);}
}
