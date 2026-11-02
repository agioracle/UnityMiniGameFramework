using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum PlatformExecutionMode { Mock, Native }
    public enum PlatformFeature { Lifecycle, LoginCredential, RewardedAds, Storage, Share, HostAudio, Haptics, MemoryWarning }
    public enum CapabilitySupport { Unknown, Supported, Unsupported }
    public enum CapabilityAvailability { Unknown, Available, Unavailable, PermissionDenied }
    [Flags] public enum CapabilityConstraints { None=0, ForegroundOnly=1, UserGestureRequired=2 }
    public enum PlatformVisibility { Unknown, Foreground, Background }
    public enum PlatformLifecycleKind { Shown, Hidden, MemoryWarning, QuitRequested }
    public enum PlatformState { Created, Initializing, Ready, Failed, Stopping, Closed }
    public enum PlatformLoginState { Captured, Starting, Succeeded, Failed, Canceled, TimedOut }
    public static class PlatformIdentifier
    {
        public static bool IsValid(string value)
        {if(string.IsNullOrEmpty(value)||value.Length>64||value[0]<'a'||value[0]>'z')return false;return value.All(c=>c>='a'&&c<='z'||c>='0'&&c<='9'||c=='.'||c=='_'||c=='-');}
    }
    public sealed class PlatformDescriptor
    {
        public string Id {get;}public PlatformExecutionMode Mode {get;}public string AdapterVersion {get;}
        public PlatformDescriptor(string id,PlatformExecutionMode mode,string adapterVersion)
        {if(!PlatformIdentifier.IsValid(id)||!Enum.IsDefined(typeof(PlatformExecutionMode),mode)||!PlatformIdentifier.IsValid(adapterVersion))throw new ArgumentException();Id=id;Mode=mode;AdapterVersion=adapterVersion;}
        public override string ToString()=>Id+":"+Mode+":"+AdapterVersion;
    }
    public readonly struct PlatformCapability
    {
        public PlatformFeature Feature {get;}public CapabilitySupport Support {get;}public CapabilityAvailability Availability {get;}
        public string ReasonCode {get;}public CapabilityConstraints Constraints {get;}public int MaxConcurrent {get;}
        public bool IsUsable=>Support==CapabilitySupport.Supported&&Availability==CapabilityAvailability.Available;
        public PlatformCapability(PlatformFeature feature,CapabilitySupport support,CapabilityAvailability availability,string reasonCode,CapabilityConstraints constraints=CapabilityConstraints.None,int maxConcurrent=0)
        {
            if(!Enum.IsDefined(typeof(PlatformFeature),feature)||!Enum.IsDefined(typeof(CapabilitySupport),support)||!Enum.IsDefined(typeof(CapabilityAvailability),availability)||!PlatformIdentifier.IsValid(reasonCode)||(constraints&~(CapabilityConstraints.ForegroundOnly|CapabilityConstraints.UserGestureRequired))!=0||maxConcurrent<0||maxConcurrent>1024||support==CapabilitySupport.Unsupported&&availability!=CapabilityAvailability.Unavailable||support==CapabilitySupport.Unknown&&availability!=CapabilityAvailability.Unknown)throw new ArgumentException("Explicit consistent capability required.");
            Feature=feature;Support=support;Availability=availability;ReasonCode=reasonCode;Constraints=constraints;MaxConcurrent=maxConcurrent;
        }
    }
    public sealed class PlatformCapabilities
    {
        readonly IReadOnlyDictionary<PlatformFeature,PlatformCapability> entries;
        public PlatformCapabilities(IEnumerable<PlatformCapability> capabilities)
        {
            if(capabilities==null)throw new ArgumentNullException(nameof(capabilities));var items=capabilities.ToArray();
            if(items.Any(c=>!PlatformIdentifier.IsValid(c.ReasonCode))||items.Select(c=>c.Feature).Distinct().Count()!=items.Length)throw new ArgumentException("Unique explicit capabilities required.");
            var map=items.ToDictionary(c=>c.Feature);foreach(PlatformFeature f in Enum.GetValues(typeof(PlatformFeature)))if(!map.ContainsKey(f))map.Add(f,new PlatformCapability(f,CapabilitySupport.Unknown,CapabilityAvailability.Unknown,"platform.capability.unknown"));entries=map;
        }
        public PlatformCapability Get(PlatformFeature feature)=>entries.TryGetValue(feature,out var value)?value:throw new ArgumentOutOfRangeException(nameof(feature));
    }
    public readonly struct PlatformLoginOptions
    {
        public bool UserInitiated {get;}public PlatformLoginOptions(bool userInitiated){UserInitiated=userInitiated;}
    }
    // The envelope can revoke its own reference; it cannot erase SDK or consumer string copies.
    public sealed class PlatformLoginCredential:IDisposable
    {
        string secret;int ownership; // 0 callback-owned, 1 accepted by service/consumer, 2 revoked.
        public string PlatformId {get;}public string AttemptId {get;}public PlatformExecutionMode Mode {get;}public DateTimeOffset? ExpiresAtUtc {get;}
        public bool IsDisposed=>Volatile.Read(ref ownership)==2;
        public bool IsClaimed=>Volatile.Read(ref ownership)==1;
        public PlatformLoginCredential(string platformId,string attemptId,PlatformExecutionMode mode,string authorizationCode,DateTimeOffset? expiresAtUtc=null)
        {
            if(!PlatformIdentifier.IsValid(platformId)||string.IsNullOrWhiteSpace(attemptId)||attemptId.Length>64||!Enum.IsDefined(typeof(PlatformExecutionMode),mode)||string.IsNullOrEmpty(authorizationCode)||authorizationCode.Length>16384||expiresAtUtc.HasValue&&expiresAtUtc.Value.Offset!=TimeSpan.Zero)throw new ArgumentException("Credential envelope invalid.");
            PlatformId=platformId;AttemptId=attemptId;Mode=mode;secret=authorizationCode;ExpiresAtUtc=expiresAtUtc;
        }
        public Result<string> ReadAuthorizationCode()
        {
            var value=Volatile.Read(ref secret);if(IsDisposed||value==null)return Result<string>.Failure(new FrameworkError(ErrorKind.InvalidState,"platform.credential.disposed"));
            if(ExpiresAtUtc.HasValue&&DateTimeOffset.UtcNow>=ExpiresAtUtc.Value)return Result<string>.Failure(new FrameworkError(ErrorKind.Unavailable,"platform.credential.expired"));
            return Result<string>.Success(value);
        }
        public bool TryClaim()=>Interlocked.CompareExchange(ref ownership,1,0)==0;
        public void DiscardUnclaimed(){if(Interlocked.CompareExchange(ref ownership,2,0)==0)Interlocked.Exchange(ref secret,null);}
        public void Dispose(){Interlocked.Exchange(ref ownership,2);Interlocked.Exchange(ref secret,null);}
        public override string ToString()=>"PlatformLoginCredential("+Mode+", redacted)";
    }
    public readonly struct PlatformLifecycleEvent
    {
        public string Generation {get;}public long Sequence {get;}public PlatformLifecycleKind Kind {get;}public PlatformVisibility Visibility {get;}
        public PlatformLifecycleEvent(string generation,long sequence,PlatformLifecycleKind kind,PlatformVisibility visibility)
        {Generation=generation;Sequence=sequence;Kind=kind;Visibility=visibility;}
    }
    public sealed class PlatformStartup
    {
        public PlatformCapabilities Capabilities {get;}public PlatformVisibility Visibility {get;}public long Sequence {get;}
        public PlatformStartup(PlatformCapabilities capabilities,PlatformVisibility visibility,long sequence=0)
        {if(capabilities==null||!Enum.IsDefined(typeof(PlatformVisibility),visibility)||sequence<0)throw new ArgumentException();Capabilities=capabilities;Visibility=visibility;Sequence=sequence;}
    }
    public enum PlatformCallbackKind { Lifecycle, Capabilities, Login }
    public sealed class PlatformCallback
    {
        public string Generation {get;}public long Sequence {get;}public PlatformCallbackKind Kind {get;}public PlatformLifecycleKind Lifecycle {get;}
        public PlatformCapabilities Capabilities {get;}public string AttemptId {get;}public Result<PlatformLoginCredential> LoginResult {get;}
        PlatformCallback(string generation,long sequence,PlatformCallbackKind kind,PlatformLifecycleKind lifecycle,PlatformCapabilities capabilities,string attempt,Result<PlatformLoginCredential> result)
        {if(string.IsNullOrWhiteSpace(generation)||generation.Length>64)throw new ArgumentException();Generation=generation;Sequence=sequence;Kind=kind;Lifecycle=lifecycle;Capabilities=capabilities;AttemptId=attempt;LoginResult=result;}
        public static PlatformCallback LifecycleChanged(string generation,long sequence,PlatformLifecycleKind kind)
        {if(sequence<1||!Enum.IsDefined(typeof(PlatformLifecycleKind),kind))throw new ArgumentException();return new PlatformCallback(generation,sequence,PlatformCallbackKind.Lifecycle,kind,null,null,default);}
        public static PlatformCallback CapabilitiesChanged(string generation,long sequence,PlatformCapabilities capabilities)
        {if(sequence<1||capabilities==null)throw new ArgumentException();return new PlatformCallback(generation,sequence,PlatformCallbackKind.Capabilities,default,capabilities,null,default);}
        public static PlatformCallback Login(string generation,string attemptId,Result<PlatformLoginCredential> result)
        {if(string.IsNullOrWhiteSpace(attemptId)||attemptId.Length>64||result.IsSuccess&&result.Value==null)throw new ArgumentException();return new PlatformCallback(generation,0,PlatformCallbackKind.Login,default,null,attemptId,result);}
        public void Discard(){if(Kind==PlatformCallbackKind.Login&&LoginResult.IsSuccess)LoginResult.Value.DiscardUnclaimed();}
        public override string ToString()=>"PlatformCallback("+Kind+", redacted)";
    }
    public interface IPlatformCapabilities { PlatformDescriptor Descriptor {get;}PlatformCapabilities Capabilities {get;} }
    public interface IPlatformLifecycle
    {
        PlatformState State {get;}PlatformVisibility Visibility {get;}
        Result<ICleanupTicket> SubscribeLifecycle(IScope scope,string id,Action<PlatformLifecycleEvent> listener);
        Result<ICleanupTicket> SubscribeCapabilities(IScope scope,string id,Action<PlatformCapabilities> listener);
    }
    public interface IPlatformLogin { Result<IPlatformLoginOperation> CaptureLogin(PlatformLoginOptions options); }
    public interface IPlatformLoginOperation
    {
        string AttemptId {get;}PlatformLoginState State {get;}FrameworkError? CancelError {get;}FrameworkError? CleanupError {get;}
        UniTask<Result<PlatformLoginCredential>> WaitAsync(OperationContext wait);
    }
    public interface IPlatformCallbackSink
    {
        // Any thread. True transfers ownership; false leaves callback/secret with the producer.
        bool TryPost(PlatformCallback callback);
    }
    public interface IPlatformBackend
    {
        PlatformDescriptor Descriptor {get;}
        IPlatformSession Open(string generation,IPlatformCallbackSink sink); // Cold, no callbacks or SDK work.
    }
    public interface IPlatformSession
    {
        UniTask<Result<PlatformStartup>> InitializeAsync(); // Actual completion; caller cancellation is not terminal.
        IPlatformLoginAttempt CreateLogin(string attemptId,PlatformLoginOptions options); // Cold, no SDK call.
        UniTask<Result> CleanupAsync(CleanupContext cleanup); // Unsubscribe; no further producer callbacks after success.
    }
    public interface IPlatformLoginAttempt
    {
        // Resolves at actual terminal state; all required result callbacks posted (or rejected) before it resolves.
        UniTask<Result> StartAsync();
        Result Cancel(); // Intent only; does not prove the SDK stopped.
        UniTask<Result> CleanupAsync(CleanupContext cleanup);
    }
}
