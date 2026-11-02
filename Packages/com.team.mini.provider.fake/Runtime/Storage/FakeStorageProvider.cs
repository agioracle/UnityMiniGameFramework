using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
namespace MiniFramework.Providers.StorageFake
{
    public enum FakeStorageFault { None, Reject, Quota, UnknownBeforeWrite, UnknownAfterWrite, ExceptionBeforeWrite, ExceptionAfterWrite }
    public sealed class FakeStorageStore
    {
        readonly object gate=new object();readonly Dictionary<string,byte[]> slots=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        public Queue<FakeStorageFault> Faults {get;}=new Queue<FakeStorageFault>();
        public long QuotaBytes {get;set;}=4194304;
        public bool FailRead {get;set;}public bool FailCleanup {get;set;}
        static string Key(StorageAddress address)=>address.Namespace+"/"+address.Slot;
        public void SeedRaw(StorageAddress address,byte[] data){if(!address.IsValid||data==null)throw new ArgumentException();lock(gate)slots[Key(address)]=(byte[])data.Clone();}
        public byte[] CopyRaw(StorageAddress address){lock(gate)return slots.TryGetValue(Key(address),out var bytes)?(byte[])bytes.Clone():null;}
        internal StorageCommitReceipt Apply(StorageWriteRequest request,StorageAddress address,StorageLimits limits)
        {
            lock(gate)
            {
                var fault=Faults.Count==0?FakeStorageFault.None:Faults.Dequeue();
                if(fault==FakeStorageFault.ExceptionBeforeWrite)throw new InvalidOperationException("Injected before-write failure.");
                if(fault==FakeStorageFault.UnknownBeforeWrite)return StorageCommitReceipt.Indeterminate(Error("fake.storage.unknown_before"));
                if(fault==FakeStorageFault.Reject)return StorageCommitReceipt.Rejected(Error("fake.storage.rejected"));
                var old=CopyRaw(address);var valid=StorageWriteValidation.Check(request,address,old,limits);if(!valid.IsSuccess)return StorageCommitReceipt.Rejected(valid.Error);
                long bytes=0;foreach(var item in slots.Values)bytes+=item.Length;
                if(fault==FakeStorageFault.Quota||bytes-(old?.Length??0)+request.ByteCount>QuotaBytes)return StorageCommitReceipt.Rejected(Error("fake.storage.quota"));
                slots[Key(address)]=request.CopyBytes();
                if(fault==FakeStorageFault.ExceptionAfterWrite)throw new InvalidOperationException("Injected after-write failure.");
                if(fault==FakeStorageFault.UnknownAfterWrite)return StorageCommitReceipt.Indeterminate(Error("fake.storage.unknown_after"));
                return StorageCommitReceipt.Committed(request.Sequence,request.Digest,StoragePersistence.Volatile);
            }
        }
        internal static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,code);
    }
    public sealed class FakeStorageProvider:IStorageProvider
    {
        readonly FakeStorageStore store;readonly StorageLimits limits;readonly List<FakeStorageSession> sessions=new List<FakeStorageSession>();
        public string Id=>"fake-storage";public StorageCapabilities Capabilities {get;}
        public IReadOnlyList<FakeStorageSession> Sessions=>sessions.AsReadOnly();
        public bool HoldReads {get;set;}public bool HoldCommits {get;set;}
        public FakeStorageProvider(FakeStorageStore store,int maxContainerBytes=1048576)
        {this.store=store??throw new ArgumentNullException(nameof(store));limits=new StorageLimits(1024,maxContainerBytes,maxContainerBytes);Capabilities=new StorageCapabilities(maxContainerBytes,StoragePersistence.Volatile,true);}
        public IStorageSession Create(StorageAddress address)
        {if(!address.IsValid)throw new ArgumentException();var session=new FakeStorageSession(store,address,limits,HoldReads,HoldCommits);sessions.Add(session);return session;}
    }
    public sealed class FakeStorageSession:IStorageSession
    {
        readonly FakeStorageStore store;readonly StorageAddress address;readonly StorageLimits limits;readonly bool holdRead,holdWrite;
        readonly UniTaskCompletionSource<Result<StorageReadResult>> read=new UniTaskCompletionSource<Result<StorageReadResult>>();
        readonly Queue<Tuple<StorageWriteRequest,UniTaskCompletionSource<StorageCommitReceipt>>> pending=new Queue<Tuple<StorageWriteRequest,UniTaskCompletionSource<StorageCommitReceipt>>>();
        bool closed,reading;
        public int ReadCount {get;private set;}public int CommitCount {get;private set;}public int CleanupCount {get;private set;}
        public int PendingCommits=>pending.Count;
        internal FakeStorageSession(FakeStorageStore store,StorageAddress address,StorageLimits limits,bool holdRead,bool holdWrite){this.store=store;this.address=address;this.limits=limits;this.holdRead=holdRead;this.holdWrite=holdWrite;}
        Result<StorageReadResult> Read()
        {
            if(store.FailRead)return Result<StorageReadResult>.Failure(FakeStorageStore.Error("fake.storage.read"));
            var bytes=store.CopyRaw(address);if(bytes!=null&&bytes.Length>limits.MaxContainerBytes)return Result<StorageReadResult>.Failure(FakeStorageStore.Error("fake.storage.size",ErrorKind.Integrity));
            return Result<StorageReadResult>.Success(new StorageReadResult(bytes!=null,bytes));
        }
        public UniTask<Result<StorageReadResult>> ReadAsync()
        {if(closed||reading)return UniTask.FromResult(Result<StorageReadResult>.Failure(FakeStorageStore.Error("fake.storage.read_state",ErrorKind.InvalidState)));reading=true;ReadCount++;return holdRead?read.Task:UniTask.FromResult(Read());}
        public bool CompleteRead()=>reading&&read.TrySetResult(Read());
        public UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request)
        {
            if(closed)return UniTask.FromResult(StorageCommitReceipt.Rejected(FakeStorageStore.Error("fake.storage.closed",ErrorKind.InvalidState)));
            CommitCount++;if(!holdWrite)return UniTask.FromResult(store.Apply(request,address,limits));
            var done=new UniTaskCompletionSource<StorageCommitReceipt>();pending.Enqueue(Tuple.Create(request,done));return done.Task;
        }
        public bool CompleteNextCommit()
        {if(pending.Count==0)return false;var item=pending.Dequeue();try{item.Item2.TrySetResult(store.Apply(item.Item1,address,limits));}catch(Exception e){item.Item2.TrySetException(e);}return true;}
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupCount++;
            if(store.FailCleanup)return UniTask.FromResult(Result.Failure(FakeStorageStore.Error("fake.storage.cleanup",ErrorKind.CleanupFailed)));
            if(pending.Count>0||holdRead&&reading&&read.Task.Status==UniTaskStatus.Pending)return UniTask.FromResult(Result.Failure(FakeStorageStore.Error("fake.storage.pending",ErrorKind.CleanupFailed)));
            closed=true;return UniTask.FromResult(Result.Success());
        }
    }
}
