using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.PlatformMock
{
    // Explicit development/test simulation. It never loads a platform SDK or authenticates an account.
    public sealed class MockPlatformBackend:IPlatformBackend
    {
        readonly List<MockPlatformSession> sessions=new List<MockPlatformSession>();
        public PlatformDescriptor Descriptor {get;}=new PlatformDescriptor("editor-mock",PlatformExecutionMode.Mock,"mock-v1");
        public IReadOnlyList<MockPlatformSession> Sessions=>sessions.AsReadOnly();
        public PlatformStartup Startup {get;set;}=new PlatformStartup(DefaultCapabilities(),PlatformVisibility.Foreground);
        public bool HoldInitialize {get;set;}public bool AutoReply {get;set;}=true;public bool HoldLoginCompletion {get;set;}
        public bool FailAttemptCleanup {get;set;}public bool FailSessionCleanup {get;set;}public bool FailCancel {get;set;}
        public bool ThrowOpen {get;set;}public bool ThrowCreate {get;set;}public bool ThrowStart {get;set;}public bool ThrowAttemptCleanup {get;set;}
        public FrameworkError? InitializeError {get;set;}public FrameworkError? LoginError {get;set;}
        public static PlatformCapabilities DefaultCapabilities(CapabilitySupport loginSupport=CapabilitySupport.Supported,CapabilityAvailability loginAvailability=CapabilityAvailability.Available,CapabilityConstraints constraints=CapabilityConstraints.ForegroundOnly|CapabilityConstraints.UserGestureRequired)
        {
            var list=new List<PlatformCapability>();foreach(PlatformFeature feature in Enum.GetValues(typeof(PlatformFeature)))
            {
                if(feature==PlatformFeature.Lifecycle)list.Add(new PlatformCapability(feature,CapabilitySupport.Supported,CapabilityAvailability.Available,"platform.mock.lifecycle"));
                else if(feature==PlatformFeature.LoginCredential)list.Add(new PlatformCapability(feature,loginSupport,loginAvailability,"platform.mock.login",constraints,1));
                else list.Add(new PlatformCapability(feature,CapabilitySupport.Unsupported,CapabilityAvailability.Unavailable,"platform.mock.not_implemented"));
            }
            return new PlatformCapabilities(list);
        }
        public IPlatformSession Open(string generation,IPlatformCallbackSink sink)
        {if(ThrowOpen)throw new InvalidOperationException("Injected open failure.");var session=new MockPlatformSession(this,generation,sink);sessions.Add(session);return session;}
    }
    public sealed class MockPlatformSession:IPlatformSession
    {
        readonly MockPlatformBackend owner;readonly object callbackGate=new object();IPlatformCallbackSink sink;readonly List<MockPlatformLoginAttempt> attempts=new List<MockPlatformLoginAttempt>();
        readonly UniTaskCompletionSource<Result<PlatformStartup>> initialized=new UniTaskCompletionSource<Result<PlatformStartup>>();
        bool initializing,closed;public string Generation {get;}public int InitializeCount {get;private set;}public int CleanupCount {get;private set;}
        public IReadOnlyList<MockPlatformLoginAttempt> Attempts=>attempts.AsReadOnly();
        internal MockPlatformSession(MockPlatformBackend owner,string generation,IPlatformCallbackSink sink){this.owner=owner;Generation=generation;sink=sink??throw new ArgumentNullException(nameof(sink));this.sink=sink;}
        Result<PlatformStartup> InitialResult()=>owner.InitializeError.HasValue?Result<PlatformStartup>.Failure(owner.InitializeError.Value):Result<PlatformStartup>.Success(owner.Startup);
        public UniTask<Result<PlatformStartup>> InitializeAsync()
        {if(closed||initializing)return UniTask.FromResult(Result<PlatformStartup>.Failure(Error("platform.mock.initialize_state",ErrorKind.InvalidState)));initializing=true;InitializeCount++;return owner.HoldInitialize?initialized.Task:UniTask.FromResult(InitialResult());}
        public bool CompleteInitialize()=>initialized.TrySetResult(InitialResult());
        public bool Publish(PlatformCallback callback)
        {if(callback==null)throw new ArgumentNullException(nameof(callback));lock(callbackGate){if(!closed&&sink!=null&&sink.TryPost(callback))return true;}callback.Discard();return false;}
        public IPlatformLoginAttempt CreateLogin(string attemptId,PlatformLoginOptions options)
        {if(closed||owner.ThrowCreate)throw new InvalidOperationException("Mock login creation failed.");var attempt=new MockPlatformLoginAttempt(owner,this,attemptId);attempts.Add(attempt);return attempt;}
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupCount++;if(owner.FailSessionCleanup)return UniTask.FromResult(Result.Failure(Error("platform.mock.session_cleanup",ErrorKind.CleanupFailed)));
            if(owner.HoldInitialize&&initializing&&initialized.Task.Status==UniTaskStatus.Pending)return UniTask.FromResult(Result.Failure(Error("platform.mock.initialize_pending",ErrorKind.CleanupFailed)));
            foreach(var attempt in attempts)if(attempt.StartCount>0&&!attempt.IsActuallyDone)return UniTask.FromResult(Result.Failure(Error("platform.mock.login_pending",ErrorKind.CleanupFailed)));
            lock(callbackGate){closed=true;sink=null;}return UniTask.FromResult(Result.Success());
        }
        internal static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Provider)=>new FrameworkError(kind,code);
    }
    public sealed class MockPlatformLoginAttempt:IPlatformLoginAttempt
    {
        readonly MockPlatformBackend owner;readonly MockPlatformSession session;readonly UniTaskCompletionSource<Result> ended=new UniTaskCompletionSource<Result>();
        bool closed;public string AttemptId {get;}public int StartCount {get;private set;}public int CancelCount {get;private set;}public int CleanupCount {get;private set;}
        public bool IsActuallyDone {get;private set;}public PlatformLoginCredential LastCredential {get;private set;}
        internal MockPlatformLoginAttempt(MockPlatformBackend owner,MockPlatformSession session,string attemptId){this.owner=owner;this.session=session;AttemptId=attemptId;}
        public UniTask<Result> StartAsync()
        {
            if(closed||StartCount>0)return UniTask.FromResult(Result.Failure(MockPlatformSession.Error("platform.mock.login_state",ErrorKind.InvalidState)));
            StartCount++;if(owner.ThrowStart){IsActuallyDone=true;throw new OperationCanceledException("Unrelated injected SDK exception.");}
            if(owner.AutoReply)
            {
                if(owner.LoginError.HasValue)session.Publish(PlatformCallback.Login(session.Generation,AttemptId,Result<PlatformLoginCredential>.Failure(owner.LoginError.Value)));
                else PublishSuccess();
            }
            if(!owner.HoldLoginCompletion)Complete();return ended.Task;
        }
        public PlatformLoginCredential PublishSuccess(string code="mock-code",string generation=null,string attemptId=null,PlatformExecutionMode mode=PlatformExecutionMode.Mock,DateTimeOffset? expiry=null)
        {
            var target=attemptId??AttemptId;LastCredential=new PlatformLoginCredential(owner.Descriptor.Id,target,mode,code,expiry);
            session.Publish(PlatformCallback.Login(generation??session.Generation,target,Result<PlatformLoginCredential>.Success(LastCredential)));return LastCredential;
        }
        public bool PublishFailure(FrameworkError error)=>session.Publish(PlatformCallback.Login(session.Generation,AttemptId,Result<PlatformLoginCredential>.Failure(error)));
        public bool PublishAgain(PlatformLoginCredential credential)=>session.Publish(PlatformCallback.Login(session.Generation,AttemptId,Result<PlatformLoginCredential>.Success(credential)));
        public bool Complete(Result? result=null){IsActuallyDone=true;return ended.TrySetResult(result??Result.Success());}
        public Result Cancel(){CancelCount++;return owner.FailCancel?Result.Failure(MockPlatformSession.Error("platform.mock.cancel")):Result.Success();}
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupCount++;if(owner.ThrowAttemptCleanup)throw new InvalidOperationException();
            if(owner.FailAttemptCleanup||StartCount>0&&!IsActuallyDone)return UniTask.FromResult(Result.Failure(MockPlatformSession.Error("platform.mock.attempt_cleanup",ErrorKind.CleanupFailed)));
            closed=true;return UniTask.FromResult(Result.Success());
        }
    }
}
