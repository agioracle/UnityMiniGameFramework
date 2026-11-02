using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    internal static class CleanupWait
    {
        internal static void MainThread()
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("Cleanup ownership belongs to Unity main thread.");
        }
        internal static Result Error(ErrorKind kind, string code) => Result.Failure(new FrameworkError(kind, code));
        internal static long Now => Stopwatch.GetTimestamp();
        internal static Result? Interrupted(OperationContext wait, long start)
        {
            if (wait.Cancellation.IsCancellationRequested) return Error(ErrorKind.Canceled, "cleanup.wait.canceled");
            if (wait.Timeout != Timeout.InfiniteTimeSpan && (Now-start)/(double)Stopwatch.Frequency >= wait.Timeout.TotalSeconds)
                return Error(ErrorKind.Timeout, "cleanup.wait.timeout");
            return null;
        }
        internal static OperationContext Operation(CleanupContext context) => new OperationContext(context.ShutdownCancellation, context.Timeout);
    }
}
