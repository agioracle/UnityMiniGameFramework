using System;
using System.Threading;
namespace MiniFramework.Contracts
{
    public readonly struct OperationContext
    {
        public CancellationToken Cancellation { get; }
        public TimeSpan Timeout { get; }
        // Zero/default means an immediate deadline, never an accidental infinite wait.
        public OperationContext(CancellationToken cancellation, TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan) throw new ArgumentOutOfRangeException(nameof(timeout));
            Cancellation = cancellation; Timeout = timeout;
        }
        public static OperationContext Unlimited(CancellationToken cancellation = default) =>
            new OperationContext(cancellation, System.Threading.Timeout.InfiniteTimeSpan);
    }
    public readonly struct CleanupContext
    {
        public CancellationToken ShutdownCancellation { get; }
        public TimeSpan Timeout { get; }
        // Must be supplied by the cleanup owner, not linked to a canceled business operation.
        public CleanupContext(CancellationToken shutdownCancellation, TimeSpan timeout)
        {
            var validated = new OperationContext(shutdownCancellation, timeout);
            ShutdownCancellation = validated.Cancellation; Timeout = validated.Timeout;
        }
    }
}
