using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using NUnit.Framework;
public sealed class StorageFormatTests
{
    readonly StorageAddress address=new StorageAddress("local","main");readonly StorageLimits limits=new StorageLimits();
    StorageRecord Record(string id="game.progress",byte[] bytes=null,int schema=1,string codec="json-v1",bool required=true)=>new StorageRecord(id,schema,codec,required,bytes??new byte[]{1,2,3});
    byte[] Enc(params StorageRecord[] rows)=>StorageFormat.Encode(new StorageContainer(address,1,rows),limits).Value;
    byte[] Seal(byte[] data){using(var sha=SHA256.Create()){var body=data.Take(data.Length-32).ToArray();var digest=sha.ComputeHash(body);Buffer.BlockCopy(digest,0,data,data.Length-32,32);return data;}}
    [Test] public void GoldenBytesMatchIndependentWriter(){var expected=File.ReadAllText("Assets/Tests/S4Storage/Fixtures/container-v1.hex").Trim();Assert.AreEqual(expected,BitConverter.ToString(Enc(Record())).Replace("-","").ToLowerInvariant());}
    [Test] public void RoundTripPreservesMetadata(){var v=StorageFormat.Decode(Enc(Record()),limits).Value;Assert.AreEqual(1,v.Sequence);Assert.AreEqual("local",v.Address.Namespace);Assert.AreEqual("main",v.Address.Slot);Assert.AreEqual("game.progress",v.Records[0].ModuleId);Assert.AreEqual(1,v.Records[0].SchemaVersion);Assert.AreEqual("json-v1",v.Records[0].CodecId);Assert.IsTrue(v.Records[0].Required);}
    [Test] public void EmptyContainerRoundTrips(){var bytes=Enc();Assert.IsTrue(StorageFormat.Decode(bytes,limits).IsSuccess);}
    [Test] public void MinimalIdsEmptyContainerRoundTrip(){var bytes=StorageFormat.Encode(new StorageContainer(new StorageAddress("a","b"),1,Array.Empty<StorageRecord>()),limits).Value;Assert.AreEqual(62,bytes.Length);Assert.IsTrue(StorageFormat.Decode(bytes,limits).IsSuccess);}
    [Test] public void RecordDefensivelyCopiesInput(){var b=new byte[]{4};var r=Record(bytes:b);b[0]=9;Assert.AreEqual(4,r.CopyPayload()[0]);}
    [Test] public void RecordOutputCannotMutate(){var r=Record();var b=r.CopyPayload();b[0]=9;Assert.AreEqual(1,r.CopyPayload()[0]);}
    [Test] public void InputOrderDoesNotChangeBytes(){CollectionAssert.AreEqual(Enc(Record("z"),Record("a")),Enc(Record("a"),Record("z")));}
    [Test] public void DuplicateModuleIdsRejected(){Assert.Throws<ArgumentException>(()=>new StorageContainer(address,1,new[]{Record(),Record()}));}
    [Test] public void RuntimeTypeRenameDoesNotChangePersistentBytes(){var old=Module("progress",typeof(OldName));var renamed=Module("progress",typeof(Renamed));CollectionAssert.AreEqual(Enc(new StorageRecord(old.ModuleId,old.SchemaVersion,old.CodecId,old.Required,old.CaptureSnapshot().Value)),Enc(new StorageRecord(renamed.ModuleId,renamed.SchemaVersion,renamed.CodecId,renamed.Required,renamed.CaptureSnapshot().Value)));Assert.AreNotEqual(old.DataType,renamed.DataType);}
    [Test] public void PortableIdsRejectTypeNamesAndPaths(){foreach(var id in new[]{"","A","../a","a/b","a\\b","a..b","_a","中文",new string('a',65)})Assert.IsFalse(StorageIdentifier.IsValid(id),id);}
    [Test] public void PortableIdBoundaryAccepted(){Assert.IsTrue(StorageIdentifier.IsValid("a.b_c-d9"));Assert.IsTrue(StorageIdentifier.IsValid(new string('a',64)));}
    [Test] public void InvalidDefaultAddressRejected(){Assert.Throws<ArgumentException>(()=>new StorageContainer(default,1,Array.Empty<StorageRecord>()));}
    [Test] public void NonPositiveSequenceRejected(){Assert.Throws<ArgumentException>(()=>new StorageContainer(address,0,Array.Empty<StorageRecord>()));}
    [Test] public void InvalidSchemaRejected(){Assert.Throws<ArgumentException>(()=>Record(schema:0));}
    [Test] public void CorruptPayloadFailsWholeChecksum(){var b=Enc(Record());b[45]^=1;Assert.AreEqual("storage.format.checksum",StorageFormat.Decode(b,limits).Error.Code);}
    [Test] public void CorruptMetadataFailsChecksum(){var b=Enc(Record());b[10]^=1;Assert.AreEqual("storage.format.checksum",StorageFormat.Decode(b,limits).Error.Code);}
    [Test] public void FutureFormatRejectedWithoutImport(){var b=Enc(Record());b[8]=2;Assert.AreEqual("storage.format.unsupported",StorageFormat.Decode(Seal(b),limits).Error.Code);}
    [Test] public void LegacyMagicRejected(){var b=Enc(Record());b[0]=(byte)'L';Assert.AreEqual("storage.format.magic",StorageFormat.Decode(Seal(b),limits).Error.Code);}
    [Test] public void TruncatedDataRejected(){var b=Enc(Record());Assert.IsFalse(StorageFormat.Decode(b.Take(b.Length-1).ToArray(),limits).IsSuccess);}
    [Test] public void NullInputRejected(){Assert.IsFalse(StorageFormat.Decode(null,limits).IsSuccess);}
    [Test] public void MaxContainerBoundChecked(){Assert.IsFalse(StorageFormat.Decode(new byte[limits.MaxContainerBytes+1],limits).IsSuccess);}
    [Test] public void PayloadBoundCheckedBeforeEncode(){var r=Record(bytes:new byte[9]);Assert.IsFalse(StorageFormat.Encode(new StorageContainer(address,1,new[]{r}),new StorageLimits(2,8,256)).IsSuccess);}
    [Test] public void ModuleCountBoundChecked(){Assert.IsFalse(StorageFormat.Encode(new StorageContainer(address,1,new[]{Record("a"),Record("b")}),new StorageLimits(1,8,256)).IsSuccess);}
    [Test] public void AggregateByteBoundChecked(){var rows=new[]{Record("a",new byte[80]),Record("b",new byte[80])};Assert.IsFalse(StorageFormat.Encode(new StorageContainer(address,1,rows),new StorageLimits(2,80,256)).IsSuccess);}
    [Test] public void TrailingDataRejectedAfterReseal(){var b=Enc(Record());var longer=new byte[b.Length+1];Buffer.BlockCopy(b,0,longer,0,b.Length-32);longer[b.Length-32]=12;Assert.IsFalse(StorageFormat.Decode(Seal(longer),limits).IsSuccess);}
    [Test] public void PayloadChecksumRejectsResealedContainer(){var b=Enc(Record());b[70]^=1;Assert.AreEqual("storage.format.payload_checksum",StorageFormat.Decode(Seal(b),limits).Error.Code);}
    [Test] public void ResealedMalformedHeadersFailClosed(){foreach(var offset in new[]{25,33,37,52,65,66}){var b=Enc(Record());if(offset==65)b[offset]=2;else if(offset==33||offset==66){for(var i=0;i<4;i++)b[offset+i]=255;}else if(offset==25){for(var i=0;i<8;i++)b[offset+i]=0;}else{b[offset]=0;b[offset+1]=0;}Assert.IsFalse(StorageFormat.Decode(Seal(b),limits).IsSuccess,"malformed header at "+offset);}}
    [Test] public void DigestIsStableSha256(){Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",StorageFormat.Digest(new byte[]{97,98,99}));}
    [Test] public void RegistryRejectsDuplicateId(){var r=new StorageRegistry();Assert.IsTrue(r.Register(Module("a",typeof(OldName))).IsSuccess);Assert.AreEqual("storage.registry.duplicate_id",r.Register(Module("a",typeof(Renamed))).Error.Code);}
    [Test] public void RegistryRejectsDuplicateType(){var r=new StorageRegistry();r.Register(Module("a",typeof(OldName)));Assert.AreEqual("storage.registry.duplicate_type",r.Register(Module("b",typeof(OldName))).Error.Code);}
    [Test] public void RegistryFreezesAndSorts(){var r=new StorageRegistry();r.Register(Module("z",typeof(OldName)));r.Register(Module("a",typeof(Renamed)));CollectionAssert.AreEqual(new[]{"a","z"},r.Freeze().Select(x=>x.ModuleId));Assert.IsFalse(r.Register(Module("b",typeof(string))).IsSuccess);}
    [Test] public void RegistryRejectsNull(){Assert.IsFalse(new StorageRegistry().Register(null).IsSuccess);}
    [Test] public void RegistrationRequiresStableCodec(){Assert.Throws<ArgumentException>(()=>new StorageModuleRegistration("a",1,"Bad Codec",true,typeof(OldName),()=>Result<byte[]>.Success(new byte[0]),_=>Result.Success()));}
    [Test] public void ReadResultOwnsBytes(){var b=new byte[]{1};var r=new StorageReadResult(true,b);b[0]=2;var c=r.CopyBytes();c[0]=3;Assert.AreEqual(1,r.CopyBytes()[0]);}
    [Test] public void MissingReadCannotCarryPayload(){Assert.Throws<ArgumentException>(()=>new StorageReadResult(false,new byte[0]));}
    [Test] public void WriteRequestOwnsBytes(){var b=new byte[]{1};var w=new StorageWriteRequest(address,0,"",1,"digest",b);b[0]=2;w.CopyBytes()[0]=3;Assert.AreEqual(1,w.CopyBytes()[0]);}
    [Test] public void WriteRequestRejectsNonMonotonicSequence(){Assert.Throws<ArgumentException>(()=>new StorageWriteRequest(address,1,"x",3,"y",new byte[0]));}
    [Test] public void ProviderBridgeConstructionIsCold(){var calls=0;new StorageProvider("test",new StorageCapabilities(256,StoragePersistence.Volatile,true),_=>{calls++;return null;});Assert.AreEqual(0,calls);}
    [Test] public void NegativeLimitsRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>new StorageLimits(maxOperations:0));}
    [Test] public void ReceiptRejectsDefaultError(){Assert.Throws<ArgumentException>(()=>StorageCommitReceipt.Rejected(default));}
    class OldName{}class Renamed{}
    static StorageModuleRegistration Module(string id,Type type)=>new StorageModuleRegistration(id,1,"bytes",true,type,()=>Result<byte[]>.Success(new byte[]{1}),_=>Result.Success());
}
