using System;
using System.Threading;
namespace MiniFramework.Kernel
{
    public enum WaitOutcome { Pending, Succeeded, Failed, Canceled, TimedOut }
    // One arbiter per logical waiter. No backend/lease ownership and no callbacks here.
    public sealed class CompletionArbiter
    {
        private int outcome;
        public WaitOutcome Outcome => (WaitOutcome)Volatile.Read(ref outcome);
        public bool TryComplete(WaitOutcome terminal)
        {
            if (terminal < WaitOutcome.Succeeded || terminal > WaitOutcome.TimedOut) throw new ArgumentOutOfRangeException(nameof(terminal));
            return Interlocked.CompareExchange(ref outcome, (int)terminal, 0) == 0;
        }
    }
}
