using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
namespace MiniFramework.Storage
{
    public sealed class StorageMigrationStep
    {
        public string ModuleId {get;}public int FromSchema {get;}public int ToSchema=>FromSchema+1;
        public string FromCodec {get;}public string ToCodec {get;}
        readonly Func<byte[],Result<byte[]>> transform;
        public StorageMigrationStep(string moduleId,int fromSchema,string fromCodec,string toCodec,Func<byte[],Result<byte[]>> transform)
        {
            if(!StorageIdentifier.IsValid(moduleId)||fromSchema<=0||fromSchema==int.MaxValue||!StorageIdentifier.IsValid(fromCodec)||!StorageIdentifier.IsValid(toCodec)||transform==null)throw new ArgumentException("Explicit migration edge required.");
            ModuleId=moduleId;FromSchema=fromSchema;FromCodec=fromCodec;ToCodec=toCodec;this.transform=transform;
        }
        internal Result<byte[]> Apply(byte[] input)=>transform((byte[])input.Clone());
    }
    public sealed class StorageMigrationRegistry
    {
        readonly List<StorageMigrationStep> steps=new List<StorageMigrationStep>();bool frozen;
        public Result Register(StorageMigrationStep step)
        {
            if(frozen)return Failure("storage.migration.frozen",ErrorKind.InvalidState);
            if(step==null||steps.Count>=1024)return Failure("storage.migration.argument",ErrorKind.InvalidArgument);
            if(steps.Any(s=>s.ModuleId==step.ModuleId&&s.FromSchema==step.FromSchema&&s.FromCodec==step.FromCodec))return Failure("storage.migration.duplicate",ErrorKind.InvalidArgument);
            steps.Add(step);return Result.Success();
        }
        internal IReadOnlyList<StorageMigrationStep> Freeze(){frozen=true;return Array.AsReadOnly(steps.ToArray());}
        internal static Result Failure(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
    }
    public sealed class StorageReadSnapshot:IStorageReadSnapshot
    {
        readonly Dictionary<string,StorageRecord> records;
        public bool IsNew {get;}public long Sequence {get;}
        public StorageReadSnapshot(long sequence,IEnumerable<StorageRecord> records)
        {if(sequence<0||records==null)throw new ArgumentException();Sequence=sequence;IsNew=sequence==0;this.records=records.ToDictionary(r=>r.ModuleId,StringComparer.Ordinal);}
        public Result<byte[]> Read(string moduleId)
        {
            if(!StorageIdentifier.IsValid(moduleId))return Result<byte[]>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"storage.restore.id"));
            return records.TryGetValue(moduleId,out var record)?Result<byte[]>.Success(record.CopyPayload()):Result<byte[]>.Failure(new FrameworkError(ErrorKind.NotReady,"storage.restore.missing"));
        }
    }
    public sealed class StorageRecovery
    {
        readonly IReadOnlyList<StorageMigrationStep> steps;
        public StorageRecovery(StorageMigrationRegistry migrations=null){steps=(migrations??new StorageMigrationRegistry()).Freeze();}
        public Result<StorageContainer> Prepare(StorageContainer source,IReadOnlyList<StorageModuleRegistration> modules,StorageLimits limits)
        {
            if(source==null||modules==null||limits==null)return Fail("storage.migration.argument",ErrorKind.InvalidArgument);
            try
            {
                var result=new List<StorageRecord>();
                foreach(var record in source.Records)
                {
                    var module=modules.FirstOrDefault(m=>m.ModuleId==record.ModuleId);
                    if(module==null){if(record.Required)return Fail("storage.module.unknown_required");result.Add(record);continue;}
                    if(module.Required!=record.Required||record.SchemaVersion>module.SchemaVersion||module.SchemaVersion-record.SchemaVersion>1024)return Fail("storage.module.version");
                    var schema=record.SchemaVersion;var codec=record.CodecId;var bytes=record.CopyPayload();
                    while(schema<module.SchemaVersion)
                    {
                        var step=steps.FirstOrDefault(s=>s.ModuleId==record.ModuleId&&s.FromSchema==schema&&s.FromCodec==codec);
                        if(step==null)return Fail("storage.migration.missing");
                        var transformed=step.Apply(bytes);if(!transformed.IsSuccess)return Result<StorageContainer>.Failure(transformed.Error);
                        if(transformed.Value==null||transformed.Value.Length>limits.MaxPayloadBytes)return Fail("storage.migration.payload",ErrorKind.Integrity);
                        bytes=(byte[])transformed.Value.Clone();schema=step.ToSchema;codec=step.ToCodec;
                    }
                    if(codec!=module.CodecId)return Fail("storage.module.version");
                    var valid=module.ValidateSnapshot((byte[])bytes.Clone());if(!valid.IsSuccess)return Result<StorageContainer>.Failure(valid.Error);
                    result.Add(new StorageRecord(module.ModuleId,schema,codec,module.Required,bytes));
                }
                foreach(var module in modules)if(module.Required&&!result.Any(r=>r.ModuleId==module.ModuleId))return Fail("storage.module.missing_required",ErrorKind.Integrity);
                var candidate=new StorageContainer(source.Address,source.Sequence,result);var bounded=StorageFormat.Encode(candidate,limits);
                return bounded.IsSuccess?Result<StorageContainer>.Success(candidate):Result<StorageContainer>.Failure(bounded.Error);
            }
            catch(Exception){return Fail("storage.migration.exception",ErrorKind.Unexpected);}
        }
        static Result<StorageContainer> Fail(string code,ErrorKind kind=ErrorKind.NotSupported)=>Result<StorageContainer>.Failure(new FrameworkError(kind,code));
    }
    // Both physical and fake backends use the exact same conditional container preconditions.
    public static class StorageWriteValidation
    {
        public static Result Check(StorageWriteRequest request,StorageAddress address,byte[] current,StorageLimits limits)
        {
            if(request==null||request.Address.Namespace!=address.Namespace||request.Address.Slot!=address.Slot)return Fail("storage.backend.address");
            var bytes=request.CopyBytes();var next=StorageFormat.Decode(bytes,limits);
            if(!next.IsSuccess)return Result.Failure(next.Error);
            if(next.Value.Sequence!=request.Sequence||next.Value.Address.Namespace!=address.Namespace||next.Value.Address.Slot!=address.Slot||StorageFormat.Digest(bytes)!=request.Digest)return Fail("storage.backend.request");
            long sequence=0;var digest="";
            if(current!=null)
            {
                var old=StorageFormat.Decode(current,limits);if(!old.IsSuccess)return Result.Failure(old.Error);
                if(old.Value.Address.Namespace!=address.Namespace||old.Value.Address.Slot!=address.Slot)return Fail("storage.backend.address");
                sequence=old.Value.Sequence;digest=StorageFormat.Digest(current);
            }
            return sequence==request.ExpectedSequence&&digest==request.ExpectedDigest?Result.Success():Result.Failure(new FrameworkError(ErrorKind.Storage,"storage.backend.conflict"));
        }
        static Result Fail(string code)=>Result.Failure(new FrameworkError(ErrorKind.Integrity,code));
    }
}
