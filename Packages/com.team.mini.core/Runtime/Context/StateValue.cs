using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class StateValue<T> : IDisposable, IDiagnosticSource
    {
        public DiagnosticCounters ReadDiagnostics() => changed.ReadDiagnostics();
        private sealed class View : IReadOnlyState<T>
        {
            private readonly StateValue<T> owner;
            internal View(StateValue<T> owner) { this.owner = owner; }
            public T Value { get { CleanupWait.MainThread(); return owner.value; } }
            public IDisposable Subscribe(Action<T> listener) => owner.changed.Subscribe(listener);
        }
        private readonly EventChannel<T> changed = new EventChannel<T>();
        private readonly IEqualityComparer<T> comparer;
        private T value;
        private bool writing, closed;
        public IReadOnlyState<T> ReadOnly { get; }
        public StateValue(T initial, IEqualityComparer<T> comparer = null)
        {
            CleanupWait.MainThread(); value = initial;
            this.comparer = comparer ?? EqualityComparer<T>.Default; ReadOnly = new View(this);
        }
        public Result Set(T next)
        {
            CleanupWait.MainThread();
            if (closed || writing) return CleanupWait.Error(ErrorKind.InvalidState, "state.write.reentrant_or_closed");
            writing = true;
            try
            {
                if (comparer.Equals(value, next)) return Result.Success();
                value = next; // Commit precedes notification; listener failure does not roll back.
                return changed.Publish(next);
            }
            catch (Exception) { return CleanupWait.Error(ErrorKind.Unexpected, "state.compare"); }
            finally { writing = false; }
        }
        public void Dispose() { CleanupWait.MainThread(); closed = true; changed.Dispose(); }
    }
}
