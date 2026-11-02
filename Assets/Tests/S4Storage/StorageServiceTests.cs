using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;
public sealed class StorageServiceTests
{
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);static OperationContext Wait=>OperationContext.Unlimited();
    static readonly StorageAddress Address=new StorageAddress("local","main");
    sealed class Data{}
    sealed class Store {internal byte[] Bytes;internal long Sequence;internal string Digest="";}
    sealed class Session:IStorageSession
    {
        internal readonly Store Store;internal bool DelayRead,DelayCommit,Reject,Unknown,ThrowCommit,WrongReceipt,FailRead,FailCleanup;
        internal int Reads,Cleanups;internal readonly List<StorageWriteRequest> Writes=new List<StorageWriteRequest>();
        internal readonly List<UniTaskCompletionSource<StorageCommitReceipt>> Completions=new List<UniTaskCompletionSource<StorageCommitReceipt>>();
        internal readonly UniTaskCompletionSource<Result<StorageReadResult>> Reading=new UniTaskCompletionSource<Result<StorageReadResult>>();
        internal Session(Store store){Store=store;}
        internal Result<StorageReadResult> Read()=>FailRead?Result<StorageReadResult>.Failure(Err("test.read")):Result<StorageReadResult>.Success(new StorageReadResult(Store.Bytes!=null,Store.Bytes));
        public UniTask<Result<StorageReadResult>> ReadAsync(){Reads++;return DelayRead?Reading.Task:UniTask.FromResult(Read());}
        public UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request)
        {
            Writes.Add(request);var completion=new UniTaskCompletionSource<StorageCommitReceipt>();Completions.Add(completion);
            if(ThrowCommit)throw new Exception("test");if(!DelayCommit)Complete(Writes.Count-1);return completion.Task;
        }
        internal void Complete(int index)
        {
            if(Completions[index].Task.Status!=UniTaskStatus.Pending)return;
            var request=Writes[index];StorageCommitReceipt receipt;
            if(Unknown)receipt=StorageCommitReceipt.Indeterminate(Err("test.unknown"));
            else if(Reject)receipt=StorageCommitReceipt.Rejected(Err("test.rejected"));
            else if(Store.Sequence!=request.ExpectedSequence||Store.Digest!=request.ExpectedDigest)receipt=StorageCommitReceipt.Rejected(Err("test.conflict"));
            else {Store.Bytes=request.CopyBytes();Store.Sequence=request.Sequence;Store.Digest=request.Digest;receipt=StorageCommitReceipt.Committed(request.Sequence,WrongReceipt?"wrong":request.Digest,StoragePersistence.Volatile);}
            Completions[index].TrySetResult(receipt);
        }
        public UniTask<Result> CleanupAsync(CleanupContext cleanup){Cleanups++;return UniTask.FromResult(FailCleanup?Result.Failure(Err("test.cleanup",ErrorKind.CleanupFailed)):Result.Success());}
    }
    sealed class Env
    {
        internal readonly Store Store;internal readonly Session Session;internal readonly StorageService Service;internal readonly GameContext Context;
        internal byte Value=1;internal byte[] Shared=new byte[]{1};internal bool UseShared,CaptureThrows,CaptureFails,ValidateFails,Reenter,ReenteredRejected;
        internal int Captures,Creates;internal StorageRegistry Registry;
        internal Env(Store store=null,bool delayRead=false,bool cas=true,int maxOperations=16,int schema=1,string codec="bytes",bool required=true,bool register=true,string providerModule=null)
        {
            Store=store??new Store();Session=new Session(Store){DelayRead=delayRead};Registry=new StorageRegistry();
            if(register)Assert.IsTrue(Registry.Register(new StorageModuleRegistration("progress",schema,codec,required,typeof(Data),()=>
            {Captures++;if(Reenter)ReenteredRejected=!Service.Capture().IsSuccess;if(CaptureThrows)throw new Exception();return CaptureFails?Result<byte[]>.Failure(Err("test.capture")):Result<byte[]>.Success(UseShared?Shared:new[]{Value});},bytes=>ValidateFails?Result.Failure(Err("test.validate",ErrorKind.Integrity)):Result.Success())).IsSuccess);
            var provider=new StorageProvider("test",new StorageCapabilities(1048576,StoragePersistence.Volatile,cas),_=>{Creates++;return Session;});
            Service=new StorageService(provider,Address,Registry,new StorageLimits(maxOperations:maxOperations),providerModuleId:providerModule);
            Context=new GameContext(new[]{new ModuleRegistration(Service.Id,Service.Dependencies,()=>Service,typeof(IStorageService))},Cleanup);
        }
        internal async UniTask Start(){Check(await Context.StartAsync(Wait));}
        internal IStorageCommit Capture(){var result=Service.Capture();Check(result.IsSuccess?Result.Success():Result.Failure(result.Error));return result.Value;}
        internal async UniTask Finish()
        {
            Session.Reject=Session.Unknown=Session.ThrowCommit=Session.WrongReceipt=Session.FailCleanup=false;Session.Reading.TrySetResult(Session.Read());
            for(var frame=0;frame<30;frame++){for(var i=0;i<Session.Writes.Count;i++)Session.Complete(i);await UniTask.Yield();}
            var result=await Context.CloseAsync(Wait);if(!result.IsSuccess)result=await Context.RetryCleanupAsync(Cleanup,Wait);Check(result);Assert.AreEqual(0,Context.RetainedModuleCount);
        }
    }
    static FrameworkError Err(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,code);
    static void Check(Result r)=>Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);
    readonly List<Env> envs=new List<Env>();
    Env New(Store store=null,bool delayRead=false,bool cas=true,int maxOperations=16,int schema=1,string codec="bytes",bool required=true,bool register=true,string providerModule=null){var e=new Env(store,delayRead,cas,maxOperations,schema,codec,required,register,providerModule);envs.Add(e);return e;}
    static async UniTask Frames(int count=3){for(var i=0;i<count;i++)await UniTask.Yield();}
    [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var e in envs)await e.Finish();envs.Clear();});
    [UnityTest] public IEnumerator ConstructorDoesNotCreateProviderSession()=>UniTask.ToCoroutine(async()=>{var e=New();Assert.AreEqual(0,e.Creates);Assert.AreEqual(0,e.Session.Reads);await Frames();});
    [UnityTest] public IEnumerator MissingSlotStartsReadyWithoutWriting()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(StorageState.Ready,e.Service.State);Assert.AreEqual(0,e.Service.CommittedSequence);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator ContextResolvesStorageContract()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.IsTrue(e.Context.TryGet<IStorageService>(out var service));Assert.AreSame(e.Service,service);});
    [UnityTest] public IEnumerator CaptureIsColdAndCopiesLiveBytes()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.UseShared=true;var op=e.Capture();e.Shared[0]=9;Assert.AreEqual(0,e.Session.Writes.Count);Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,StorageFormat.Decode(e.Store.Bytes,new StorageLimits()).Value.Records[0].CopyPayload()[0]);});
    [UnityTest] public IEnumerator UnwaitedCapturedOperationReleasedOnClose()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var op=e.Capture();Check(await e.Context.CloseAsync(Wait));Assert.AreEqual(StorageCommitState.Canceled,op.State);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator SuccessfulCommitReturnsExactReceipt()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var op=e.Capture();var r=await op.WaitAsync(Wait);Assert.IsTrue(r.IsSuccess);Assert.AreEqual(1,r.Value.Sequence);Assert.AreEqual(e.Store.Digest,r.Value.Digest);Assert.AreEqual(StoragePersistence.Volatile,r.Value.Persistence);Assert.AreEqual(StorageCommitState.Committed,op.State);});
    [UnityTest] public IEnumerator WaitIsSingleUse()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var op=e.Capture();await op.WaitAsync(Wait);Assert.AreEqual("storage.wait.single_use",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(1,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator CanceledBeforeWaitDoesNotWrite()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Capture().WaitAsync(OperationContext.Unlimited(c.Token))).Error.Kind);}Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator DefaultTimeoutDoesNotWrite()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(ErrorKind.Timeout,(await e.Capture().WaitAsync(default)).Error.Kind);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator CancellationWinsOverZeroTimeout()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Capture().WaitAsync(new OperationContext(c.Token,TimeSpan.Zero))).Error.Kind);}});
    [UnityTest] public IEnumerator CancelingInFlightOnlyEndsObserver()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;var op=e.Capture();using(var c=new CancellationTokenSource()){var waiting=op.WaitAsync(OperationContext.Unlimited(c.Token));await Frames();c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await waiting).Error.Kind);Assert.AreEqual(StorageCommitState.Writing,op.State);e.Session.Complete(0);await Frames();Assert.AreEqual(StorageCommitState.Committed,op.State);Assert.AreEqual(1,e.Service.CommittedSequence);}});
    [UnityTest] public IEnumerator TimeoutDoesNotReleaseActualCommit()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;var op=e.Capture();var waiting=op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(1)));await UniTask.Delay(10,ignoreTimeScale:true);Assert.AreEqual(ErrorKind.Timeout,(await waiting).Error.Kind);Assert.AreEqual(1,e.Service.PendingCount);e.Session.Complete(0);await Frames();Assert.AreEqual(0,e.Service.PendingCount);});
    [UnityTest] public IEnumerator TwoCommitsSerializeAndAssignSequenceAfterCompletion()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;var a=e.Capture();var wa=a.WaitAsync(Wait);e.Value=2;var b=e.Capture();var wb=b.WaitAsync(Wait);Assert.AreEqual(1,e.Session.Writes.Count);Assert.AreEqual(StorageCommitState.Queued,b.State);e.Session.Complete(0);await Frames();Assert.AreEqual(2,e.Session.Writes.Count);Assert.AreEqual(1,e.Session.Writes[1].ExpectedSequence);Assert.AreEqual(e.Session.Writes[0].Digest,e.Session.Writes[1].ExpectedDigest);e.Session.Complete(1);Assert.IsTrue((await wa).IsSuccess);Assert.IsTrue((await wb).IsSuccess);Assert.AreEqual(2,e.Store.Sequence);Assert.AreEqual(2,StorageFormat.Decode(e.Store.Bytes,new StorageLimits()).Value.Records[0].CopyPayload()[0]);});
    [UnityTest] public IEnumerator QueuedWaitCancellationDoesNotDropAcceptedWrite()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;var a=e.Capture().WaitAsync(Wait);using(var c=new CancellationTokenSource()){e.Value=2;var b=e.Capture();var wb=b.WaitAsync(OperationContext.Unlimited(c.Token));c.Cancel();await Frames();Assert.AreEqual(ErrorKind.Canceled,(await wb).Error.Kind);e.Session.Complete(0);await Frames();e.Session.Complete(1);await a;await Frames();Assert.AreEqual(StorageCommitState.Committed,b.State);Assert.AreEqual(2,e.Service.CommittedSequence);}});
    [UnityTest] public IEnumerator RejectionRetainsOldDataAndFailedSnapshot()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();await e.Capture().WaitAsync(Wait);var old=(byte[])e.Store.Bytes.Clone();e.Value=2;e.Session.Reject=true;var op=e.Capture();Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(StorageCommitState.Rejected,op.State);CollectionAssert.AreEqual(old,e.Store.Bytes);Assert.AreEqual(1,e.Service.CommittedSequence);Assert.AreEqual(2,StorageFormat.Decode(e.Service.CopyFailedCommit(),new StorageLimits()).Value.Records[0].CopyPayload()[0]);});
    [UnityTest] public IEnumerator DefiniteRejectionDoesNotPoisonLaterWrites()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.Reject=true;await e.Capture().WaitAsync(Wait);e.Session.Reject=false;e.Value=3;Assert.IsTrue((await e.Capture().WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Store.Sequence);Assert.AreEqual(3,StorageFormat.Decode(e.Store.Bytes,new StorageLimits()).Value.Records[0].CopyPayload()[0]);});
    [UnityTest] public IEnumerator IndeterminateBlocksFollowingQueuedWrites()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;e.Session.Unknown=true;var a=e.Capture().WaitAsync(Wait);var b=e.Capture().WaitAsync(Wait);e.Session.Complete(0);Assert.IsFalse((await a).IsSuccess);Assert.AreEqual("storage.recovery_required",(await b).Error.Code);Assert.AreEqual(StorageState.RecoveryRequired,e.Service.State);Assert.IsFalse(e.Service.Capture().IsSuccess);Assert.AreEqual(1,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator ProviderExceptionIsIndeterminateNotRejected()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.ThrowCommit=true;var op=e.Capture();Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(StorageCommitState.Indeterminate,op.State);Assert.AreEqual(StorageState.RecoveryRequired,e.Service.State);});
    [UnityTest] public IEnumerator MismatchedCommitReceiptRequiresReconciliation()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.WrongReceipt=true;var op=e.Capture();Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Store.Sequence);Assert.AreEqual(0,e.Service.CommittedSequence);Assert.AreEqual(StorageState.RecoveryRequired,e.Service.State);});
    [UnityTest] public IEnumerator NewContextReconcilesActuallyCommittedBytes()=>UniTask.ToCoroutine(async()=>{var first=New();await first.Start();first.Session.WrongReceipt=true;await first.Capture().WaitAsync(Wait);Check(await first.Context.CloseAsync(Wait));var second=New(first.Store);await second.Start();Assert.AreEqual(1,second.Service.CommittedSequence);Assert.IsTrue((await second.Capture().WaitAsync(Wait)).IsSuccess);Assert.AreEqual(2,second.Store.Sequence);});
    [UnityTest] public IEnumerator CompareExchangePreventsStaleContextOverwrite()=>UniTask.ToCoroutine(async()=>{var store=new Store();var a=New(store);var b=New(store);await a.Start();await b.Start();Assert.IsTrue((await a.Capture().WaitAsync(Wait)).IsSuccess);b.Value=9;var rejected=await b.Capture().WaitAsync(Wait);Assert.IsFalse(rejected.IsSuccess);Assert.AreEqual("test.conflict",rejected.Error.Code);Assert.AreEqual(1,store.Sequence);Assert.AreEqual(1,StorageFormat.Decode(store.Bytes,new StorageLimits()).Value.Records[0].CopyPayload()[0]);});
    [UnityTest] public IEnumerator CloseDrainsAcceptedWritesBeforeProviderCleanup()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.DelayCommit=true;var a=e.Capture().WaitAsync(Wait);e.Value=2;var b=e.Capture().WaitAsync(Wait);var closing=e.Context.CloseAsync(Wait);await Frames();Assert.AreEqual(0,e.Session.Cleanups);Assert.AreEqual(UniTaskStatus.Pending,closing.Status);e.Session.Complete(0);await Frames();e.Session.Complete(1);Check(await closing);Assert.IsTrue((await a).IsSuccess);Assert.IsTrue((await b).IsSuccess);Assert.AreEqual(1,e.Session.Cleanups);Assert.AreEqual(2,e.Store.Sequence);});
    [UnityTest] public IEnumerator CleanupFailureRetainsModuleUntilExplicitRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Session.FailCleanup=true;Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Context.RetainedModuleCount);Assert.AreEqual(1,e.Session.Cleanups);e.Session.FailCleanup=false;Check(await e.Context.RetryCleanupAsync(Cleanup,Wait));Assert.AreEqual(2,e.Session.Cleanups);});
    [UnityTest] public IEnumerator StopRejectsNewCaptures()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();await e.Service.StopAsync(Cleanup);Assert.IsFalse(e.Service.Capture().IsSuccess);});
    [UnityTest] public IEnumerator UnsupportedConditionalBackendFailsBeforeIO()=>UniTask.ToCoroutine(async()=>{var e=New(cas:false);Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Creates);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator MissingProviderModuleDependencyFailsBeforeFactory()=>UniTask.ToCoroutine(async()=>{var e=New(providerModule:"missing");Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Creates);});
    [UnityTest] public IEnumerator CaptureBeforeReadyRejected()=>UniTask.ToCoroutine(async()=>{var e=New();Assert.IsFalse(e.Service.Capture().IsSuccess);Assert.AreEqual(0,e.Captures);await Frames();});
    [UnityTest] public IEnumerator SnapshotFailureDoesNotWrite()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.CaptureFails=true;Assert.AreEqual("test.capture",e.Service.Capture().Error.Code);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator SnapshotExceptionIsObserved()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.CaptureThrows=true;Assert.AreEqual(ErrorKind.Unexpected,e.Service.Capture().Error.Kind);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator ValidationFailureDoesNotWrite()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.ValidateFails=true;Assert.AreEqual("test.validate",e.Service.Capture().Error.Code);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator SnapshotReentryIsRejected()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Reenter=true;Assert.IsTrue(e.Service.Capture().IsSuccess);Assert.IsTrue(e.ReenteredRejected);});
    [UnityTest] public IEnumerator BoundedColdOperationCapacityRecoversOnCancel()=>UniTask.ToCoroutine(async()=>{var e=New(maxOperations:1);await e.Start();var a=e.Capture();Assert.AreEqual("storage.queue.capacity",e.Service.Capture().Error.Code);await a.WaitAsync(default);Assert.IsTrue(e.Service.Capture().IsSuccess);});
    static Store Saved(params StorageRecord[] records){var data=StorageFormat.Encode(new StorageContainer(Address,4,records),new StorageLimits()).Value;return new Store{Bytes=data,Sequence=4,Digest=StorageFormat.Digest(data)};}
    static StorageRecord Record(string id="progress",int schema=1,string codec="bytes",bool required=true)=>new StorageRecord(id,schema,codec,required,new byte[]{7});
    [UnityTest] public IEnumerator ExistingSequenceAndDigestLoaded()=>UniTask.ToCoroutine(async()=>{var e=New(Saved(Record()));await e.Start();Assert.AreEqual(4,e.Service.CommittedSequence);Assert.AreEqual(e.Store.Digest,e.Service.CommittedDigest);await e.Capture().WaitAsync(Wait);Assert.AreEqual(5,e.Store.Sequence);});
    [UnityTest] public IEnumerator UnknownOptionalModuleBytesPreserved()=>UniTask.ToCoroutine(async()=>{var e=New(Saved(Record(),Record("uninstalled",7,"opaque",false)));await e.Start();await e.Capture().WaitAsync(Wait);var row=StorageFormat.Decode(e.Store.Bytes,new StorageLimits()).Value.Records.Single(x=>x.ModuleId=="uninstalled");Assert.AreEqual(7,row.SchemaVersion);Assert.AreEqual("opaque",row.CodecId);CollectionAssert.AreEqual(new byte[]{7},row.CopyPayload());});
    [UnityTest] public IEnumerator UnknownRequiredModuleBlocksStartupWithoutOverwrite()=>UniTask.ToCoroutine(async()=>{var store=Saved(Record(),Record("uninstalled"));var old=(byte[])store.Bytes.Clone();var e=New(store);Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.IsFalse(e.Service.Capture().IsSuccess);CollectionAssert.AreEqual(old,store.Bytes);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator FutureModuleSchemaBlocksOverwrite()=>UniTask.ToCoroutine(async()=>{var e=New(Saved(Record(schema:2)));Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator CodecMismatchBlocksOverwrite()=>UniTask.ToCoroutine(async()=>{var e=New(Saved(Record(codec:"other")));Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator MissingRequiredRecordBlocksOverwrite()=>UniTask.ToCoroutine(async()=>{var e=New(Saved());Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator CorruptContainerPreservedAndNeverImported()=>UniTask.ToCoroutine(async()=>{var store=new Store{Bytes=new byte[]{1,2,3}};var e=New(store);Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);CollectionAssert.AreEqual(new byte[]{1,2,3},store.Bytes);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator AddressMismatchBlocksOverwrite()=>UniTask.ToCoroutine(async()=>{var bytes=StorageFormat.Encode(new StorageContainer(new StorageAddress("other","main"),1,new[]{Record()}),new StorageLimits()).Value;var e=New(new Store{Bytes=bytes});Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);CollectionAssert.AreEqual(bytes,e.Store.Bytes);});
    [UnityTest] public IEnumerator ReadFailureIsNotMissingSlot()=>UniTask.ToCoroutine(async()=>{var e=New();e.Session.FailRead=true;Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Session.Writes.Count);});
    [UnityTest] public IEnumerator ReadCancellationRetainsBackendUntilActualCompletion()=>UniTask.ToCoroutine(async()=>{var e=New(delayRead:true);using(var c=new CancellationTokenSource()){var start=e.Context.StartAsync(OperationContext.Unlimited(c.Token));await Frames();c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await start).Error.Kind);var close=e.Context.CloseAsync(Wait);await Frames();Assert.AreEqual(0,e.Session.Cleanups);e.Session.Reading.TrySetResult(e.Session.Read());await close;Assert.AreNotEqual(StorageState.Ready,e.Service.State);Assert.AreEqual(1,e.Session.Cleanups);}});
    [UnityTest] public IEnumerator ManySequentialCommitsLeaveNoPendingOperations()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();for(var i=0;i<100;i++){e.Value=(byte)i;Assert.IsTrue((await e.Capture().WaitAsync(Wait)).IsSuccess);}Assert.AreEqual(100,e.Service.CommittedSequence);Assert.AreEqual(0,e.Service.ReadDiagnostics().PendingOperations);});
}
