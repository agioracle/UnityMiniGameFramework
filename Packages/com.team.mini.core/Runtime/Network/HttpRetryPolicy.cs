using System;
using System.Globalization;
using MiniFramework.Contracts;
namespace MiniFramework.Network
{
    public sealed class HttpClientLimits
    {
        public int MaxOperations {get;}public int MaxConcurrent {get;}public int MaxUploadBytes {get;}public int MaxResponseBytes {get;}
        // Reservation of active buffered body capacity; not a bound on SDK memory, copies or completed results.
        public long MaxBufferedResponseBytesInFlight {get;}
        public HttpClientLimits(int maxOperations=16,int maxConcurrent=4,int maxUploadBytes=HttpBufferLimits.DefaultUploadBytes,int maxResponseBytes=HttpBufferLimits.DefaultResponseBytes,long? maxBufferedResponseBytesInFlight=null)
        {
            if(maxOperations<1||maxOperations>1024||maxConcurrent<1||maxConcurrent>maxOperations||maxUploadBytes<0||maxUploadBytes>HttpBufferLimits.MaxSupportedBytes||maxResponseBytes<1||maxResponseBytes>HttpBufferLimits.MaxSupportedBytes)throw new ArgumentOutOfRangeException();
            MaxOperations=maxOperations;MaxConcurrent=maxConcurrent;MaxUploadBytes=maxUploadBytes;MaxResponseBytes=maxResponseBytes;
            MaxBufferedResponseBytesInFlight=maxBufferedResponseBytesInFlight??(long)maxConcurrent*maxResponseBytes;
            if(MaxBufferedResponseBytesInFlight<maxResponseBytes)throw new ArgumentOutOfRangeException(nameof(maxBufferedResponseBytesInFlight));
        }
    }
    public sealed class HttpRetryPolicy
    {
        public int MaxReadAttempts {get;}public TimeSpan InitialDelay {get;}public TimeSpan MaxDelay {get;}
        public HttpRetryPolicy(int maxReadAttempts=3,TimeSpan? initialDelay=null,TimeSpan? maxDelay=null)
        {MaxReadAttempts=maxReadAttempts;InitialDelay=initialDelay??TimeSpan.FromMilliseconds(100);MaxDelay=maxDelay??TimeSpan.FromSeconds(2);if(maxReadAttempts<1||maxReadAttempts>4||InitialDelay<TimeSpan.Zero||MaxDelay<InitialDelay||MaxDelay>TimeSpan.FromMinutes(1))throw new ArgumentOutOfRangeException();}
        public bool TryDelay(HttpVerb verb,int attempts,HttpTransportResult result,DateTimeOffset now,out TimeSpan delay)
        {
            delay=TimeSpan.Zero;if(verb!=HttpVerb.Get&&verb!=HttpVerb.Head||attempts<1||attempts>=MaxReadAttempts||result==null)return false;
            var response=result.Response;var status=response?.StatusCode??0;
            var transient=status==408||status==429||status==500||status==502||status==503||status==504;
            if(!transient&&!(result.CanRetry&&result.Error?.Kind==ErrorKind.Network))return false;
            delay=TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds,InitialDelay.TotalMilliseconds*Math.Pow(2,attempts-1)));
            var retryAfter=response?.Header("Retry-After");TimeSpan requested=TimeSpan.Zero;
            if(long.TryParse(retryAfter,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds))
            {if(seconds>MaxDelay.TotalSeconds)return false;requested=TimeSpan.FromSeconds(seconds);}
            else if(DateTimeOffset.TryParseExact(retryAfter,"r",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date))requested=date>now?date-now:TimeSpan.Zero;
            if(requested>MaxDelay)return false;if(requested>delay)delay=requested;return true;
        }
        public static FrameworkError Classify(HttpTransportResult result)
        {
            if(result==null)return new FrameworkError(ErrorKind.Provider,"network.transport.null");
            if(result.Error.HasValue)return result.Error.Value;
            var code=result.Response.StatusCode;var kind=code==401||code==403?ErrorKind.PermissionDenied:code==408?ErrorKind.Timeout:code==429?ErrorKind.Unavailable:ErrorKind.Network;
            return new FrameworkError(kind,"network.http."+code.ToString(CultureInfo.InvariantCulture));
        }
    }
}
