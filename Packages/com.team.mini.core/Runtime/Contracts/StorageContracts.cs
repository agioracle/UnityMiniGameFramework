using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum StoragePersistence { Volatile, RestartRecoverable }
    public enum StorageCommitDisposition { Invalid, Committed, Rejected, Indeterminate }
    public enum StorageCommitState { Captured, Queued, Writing, Committed, Rejected, Indeterminate, Canceled }
    public enum StorageState { Uninitialized, Loading, Ready, Failed, RecoveryRequired, Closed }
    public readonly struct StorageAddress
    {
        public string Namespace {get;} public string Slot {get;}
        public bool IsValid=>StorageIdentifier.IsValid(Namespace)&&StorageIdentifier.IsValid(Slot);
        public StorageAddress(string nameSpace,string slot){Namespace=nameSpace;Slot=slot;if(!IsValid)throw new ArgumentException("Portable namespace and slot required.");}
    }
    public static class StorageIdentifier
    {
        public static bool IsValid(string value)
        {
            if(string.IsNullOrEmpty(value)||value.Length>64||value[0]<'a'||value[0]>'z'||value.Contains(".."))return false;
            foreach(var c in value)if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='.'||c=='-'||c=='_'))return false;
            return true;
        }
    }
    public sealed class StorageCapabilities
    {
        public int MaxContainerBytes {get;} public StoragePersistence Persistence {get;} public bool SupportsCompareExchange {get;}
        public StorageCapabilities(int maxContainerBytes,StoragePersistence persistence,bool supportsCompareExchange)
        {if(maxContainerBytes<=0||!Enum.IsDefined(typeof(StoragePersistence),persistence))throw new ArgumentException("Storage capabilities invalid.");MaxContainerBytes=maxContainerBytes;Persistence=persistence;SupportsCompareExchange=supportsCompareExchange;}
    }
    public sealed class StorageReadResult
    {
        readonly byte[] bytes;public bool Exists {get;}
        public StorageReadResult(bool exists,byte[] bytes=null){if(exists&&bytes==null||!exists&&bytes!=null)throw new ArgumentException("Missing and existing data are distinct.");Exists=exists;this.bytes=bytes==null?null:(byte[])bytes.Clone();}
        public byte[] CopyBytes()=>bytes==null?null:(byte[])bytes.Clone();
    }
    public sealed class StorageWriteRequest
    {
        readonly byte[] bytes;public StorageAddress Address {get;}public long ExpectedSequence {get;}public string ExpectedDigest {get;}public long Sequence {get;}public string Digest {get;}
        public int ByteCount=>bytes.Length;
        public StorageWriteRequest(StorageAddress address,long expectedSequence,string expectedDigest,long sequence,string digest,byte[] bytes)
        {if(!address.IsValid||expectedSequence<0||sequence!=expectedSequence+1||sequence<=0||expectedDigest==null||string.IsNullOrEmpty(digest)||bytes==null)throw new ArgumentException("Conditional write identity required.");Address=address;ExpectedSequence=expectedSequence;ExpectedDigest=expectedDigest;Sequence=sequence;Digest=digest;this.bytes=(byte[])bytes.Clone();}
        public byte[] CopyBytes()=>(byte[])bytes.Clone();
    }
    public sealed class StorageCommitReceipt
    {
        public StorageCommitDisposition Disposition {get;}public long Sequence {get;}public string Digest {get;}public StoragePersistence Persistence {get;}public FrameworkError? Error {get;}
        StorageCommitReceipt(StorageCommitDisposition disposition,long sequence,string digest,StoragePersistence persistence,FrameworkError? error){Disposition=disposition;Sequence=sequence;Digest=digest;Persistence=persistence;Error=error;}
        public static StorageCommitReceipt Committed(long sequence,string digest,StoragePersistence persistence)
        {if(sequence<=0||string.IsNullOrEmpty(digest)||!Enum.IsDefined(typeof(StoragePersistence),persistence))throw new ArgumentException("Commit receipt invalid.");return new StorageCommitReceipt(StorageCommitDisposition.Committed,sequence,digest,persistence,null);}
        public static StorageCommitReceipt Rejected(FrameworkError error)=>Failure(StorageCommitDisposition.Rejected,error);
        public static StorageCommitReceipt Indeterminate(FrameworkError error)=>Failure(StorageCommitDisposition.Indeterminate,error);
        static StorageCommitReceipt Failure(StorageCommitDisposition status,FrameworkError error){if(error.Kind==ErrorKind.None)throw new ArgumentException("Error required.");return new StorageCommitReceipt(status,0,"",StoragePersistence.Volatile,error);}
    }
    // A computed view over the actual receipt; observer cancellation never fabricates this result.
    public readonly struct StorageCommitResult
    {
        public StorageCommitReceipt Receipt {get;}
        public StorageCommitResult(StorageCommitReceipt receipt){Receipt=receipt;}
        public bool HasReceipt=>Receipt!=null;
        public bool IsCommitted=>Receipt?.Disposition==StorageCommitDisposition.Committed;
        public bool IsRestartRecoverable=>IsCommitted&&Receipt.Persistence==StoragePersistence.RestartRecoverable;
        public bool RequiresReconciliation=>Receipt?.Disposition==StorageCommitDisposition.Indeterminate;
    }
    public interface IStorageReadSnapshot
    {
        bool IsNew {get;}long Sequence {get;}
        Result<byte[]> Read(string moduleId);
    }
    public interface IStorageRecoveryService:IStorageService
    {
        // Null until the entire load/migration is published; null again after Stop.
        IStorageReadSnapshot Restored {get;}
    }
    public interface IStorageProvider
    {
        string Id {get;}StorageCapabilities Capabilities {get;}
        // Cold construction. Ownership transfers before ReadAsync or CommitAsync can start.
        IStorageSession Create(StorageAddress address);
    }
    public interface IStorageSession
    {
        // Actual completion only. No caller cancellation; no hidden retry or live-object serialization.
        UniTask<Result<StorageReadResult>> ReadAsync();
        // Whole-container conditional replacement. Rejected guarantees old bytes unchanged.
        // Exceptions/null receipts are INDETERMINATE, never evidence that a write did not happen.
        UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request);
        UniTask<Result> CleanupAsync(CleanupContext cleanup);
    }
    public interface IStorageCommit
    {
        string Id {get;}StorageCommitState State {get;}StorageCommitReceipt Receipt {get;}
        StorageCommitResult CommitResult {get;}
        // Single use; once queued, cancellation/timeout ends only this observer.
        UniTask<Result<StorageCommitReceipt>> WaitAsync(OperationContext wait);
    }
    public interface IStorageService
    {
        StorageState State {get;}long CommittedSequence {get;}string CommittedDigest {get;}
        // Synchronous read-only bounded snapshot. Does not start provider IO until WaitAsync.
        Result<IStorageCommit> Capture();
    }
}
