using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
namespace MiniFramework.Storage
{
    public sealed class StorageLimits
    {
        // Implementation envelope for whole-container buffering, not a platform disk quota.
        public const int MaxSupportedContainerBytes=16*1024*1024;
        public int MaxModules {get;}public int MaxPayloadBytes {get;}public int MaxContainerBytes {get;}public int MaxOperations {get;}
        public StorageLimits(int maxModules=32,int maxPayloadBytes=65536,int maxContainerBytes=1048576,int maxOperations=16)
        {if(maxModules<=0||maxModules>1024||maxPayloadBytes<=0||maxContainerBytes<256||maxPayloadBytes>maxContainerBytes||maxContainerBytes>MaxSupportedContainerBytes||maxOperations<=0||maxOperations>1024)throw new ArgumentOutOfRangeException();MaxModules=maxModules;MaxPayloadBytes=maxPayloadBytes;MaxContainerBytes=maxContainerBytes;MaxOperations=maxOperations;}
    }
    public sealed class StorageModuleRegistration
    {
        public string ModuleId {get;}public int SchemaVersion {get;}public string CodecId {get;}public bool Required {get;}public Type DataType {get;}
        readonly Func<Result<byte[]>> capture;readonly Func<byte[],Result> validate;
        public StorageModuleRegistration(string moduleId,int schemaVersion,string codecId,bool required,Type dataType,Func<Result<byte[]>> capture,Func<byte[],Result> validate)
        {if(!StorageIdentifier.IsValid(moduleId)||schemaVersion<=0||!StorageIdentifier.IsValid(codecId)||dataType==null||dataType.ContainsGenericParameters||capture==null||validate==null)throw new ArgumentException("Explicit module/schema/codec/type and snapshot callbacks required.");ModuleId=moduleId;SchemaVersion=schemaVersion;CodecId=codecId;Required=required;DataType=dataType;this.capture=capture;this.validate=validate;}
        public Result<byte[]> CaptureSnapshot()=>capture();
        public Result ValidateSnapshot(byte[] data)=>validate(data);
    }
    public sealed class StorageRegistry
    {
        readonly Dictionary<string,StorageModuleRegistration> modules=new Dictionary<string,StorageModuleRegistration>(StringComparer.Ordinal);bool frozen;
        public Result Register(StorageModuleRegistration module)
        {
            if(frozen)return Fail("storage.registry.frozen");if(module==null)return Fail("storage.registry.argument",ErrorKind.InvalidArgument);
            if(modules.ContainsKey(module.ModuleId))return Fail("storage.registry.duplicate_id",ErrorKind.InvalidArgument);
            // One codec per runtime type in this registry; another slot can have a separate registry.
            if(modules.Values.Any(m=>m.DataType==module.DataType))return Fail("storage.registry.duplicate_type",ErrorKind.InvalidArgument);
            modules.Add(module.ModuleId,module);return Result.Success();
        }
        public IReadOnlyList<StorageModuleRegistration> Freeze(){frozen=true;return Array.AsReadOnly(modules.Values.OrderBy(m=>m.ModuleId,StringComparer.Ordinal).ToArray());}
        internal static Result Fail(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result.Failure(new FrameworkError(kind,code));
    }
    // Concrete portable provider bridge. Host-specific file/dual-slot implementations are S4-02/S4-09.
    public sealed class StorageProvider:IStorageProvider
    {
        readonly Func<StorageAddress,IStorageSession> create;public string Id {get;}public StorageCapabilities Capabilities {get;}
        public StorageProvider(string id,StorageCapabilities capabilities,Func<StorageAddress,IStorageSession> create)
        {if(!StorageIdentifier.IsValid(id)||capabilities==null||create==null)throw new ArgumentException("Provider identity/capabilities/factory required.");Id=id;Capabilities=capabilities;this.create=create;}
        public IStorageSession Create(StorageAddress address){if(!address.IsValid)throw new ArgumentException("Address required.");return create(address);}
    }
}
