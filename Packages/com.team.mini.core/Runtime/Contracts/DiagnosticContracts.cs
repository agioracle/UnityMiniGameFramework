namespace MiniFramework.Contracts
{
    public struct DiagnosticCounters
    {
        public int ActiveScopes, Listeners, Timers, Ticks, PendingOperations, FailedCleanup, QueuedCallbacks, ActiveRentals, RetainedModules;
        public void Add(DiagnosticCounters other)
        {
            ActiveScopes+=other.ActiveScopes;Listeners+=other.Listeners;Timers+=other.Timers;Ticks+=other.Ticks;
            PendingOperations+=other.PendingOperations;FailedCleanup+=other.FailedCleanup;QueuedCallbacks+=other.QueuedCallbacks;
            ActiveRentals+=other.ActiveRentals;RetainedModules+=other.RetainedModules;
        }
    }
    public interface IDiagnosticSource { DiagnosticCounters ReadDiagnostics(); }
}
