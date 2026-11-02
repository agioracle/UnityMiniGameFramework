using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // Tombstones preserve indices throughout nested dispatch; only the outermost return compacts.
    public sealed class EventChannel<T> : IEventStream<T>, IDisposable, IDiagnosticSource
    {
        public DiagnosticCounters ReadDiagnostics() => new DiagnosticCounters{Listeners=ListenerCount};
        private sealed class Subscription : IDisposable
        {
            internal EventChannel<T> Owner;
            internal Action<T> Callback;
            public void Dispose()
            {
                CleanupWait.MainThread();
                var owner = Owner;
                if (owner == null) return;
                Owner = null; Callback = null; owner.count--;
                if (owner.depth == 0) owner.Compact();
            }
        }
        private readonly List<Subscription> listeners = new List<Subscription>();
        private int depth, count;
        private bool closed;
        public int ListenerCount { get { CleanupWait.MainThread(); return count; } }
        public IDisposable Subscribe(Action<T> listener)
        {
            CleanupWait.MainThread();
            if (closed) throw new ObjectDisposedException(nameof(EventChannel<T>));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var subscription = new Subscription { Owner = this, Callback = listener };
            listeners.Add(subscription); count++; return subscription;
        }
        public Result Publish(T value)
        {
            CleanupWait.MainThread();
            if (closed) return CleanupWait.Error(ErrorKind.InvalidState, "event.closed");
            if (depth >= 32) return CleanupWait.Error(ErrorKind.InvalidState, "event.depth");
            var limit = listeners.Count;
            var failed = false;
            depth++;
            try
            {
                for (var i = 0; i < limit; i++)
                {
                    var callback = listeners[i].Callback;
                    if (callback == null) continue;
                    try { callback(value); }
                    catch (Exception) { failed = true; }
                }
            }
            finally { if (--depth == 0) Compact(); }
            return failed ? CleanupWait.Error(ErrorKind.Unexpected, "event.listener") : Result.Success();
        }
        private void Compact()
        {
            var write = 0;
            for (var read = 0; read < listeners.Count; read++)
                if (listeners[read].Owner != null) listeners[write++] = listeners[read];
            if (write < listeners.Count) listeners.RemoveRange(write, listeners.Count - write);
        }
        public void Dispose()
        {
            CleanupWait.MainThread(); closed = true;
            foreach (var item in listeners) { item.Owner = null; item.Callback = null; }
            count = 0;
            if (depth == 0) listeners.Clear();
        }
    }
    public static class ScopedEvents
    {
        // Register failure retains no listener and transfers no ownership.
        public static Result<ICleanupTicket> Subscribe<T>(this IEventStream<T> source, IScope scope,
            string id, Action<T> listener)
        {
            CleanupWait.MainThread();
            if (source == null || scope == null || listener == null || string.IsNullOrWhiteSpace(id))
                return Result<ICleanupTicket>.Failure(new FrameworkError(ErrorKind.InvalidArgument, "event.subscription"));
            if (scope.State != ScopeState.Open)
                return Result<ICleanupTicket>.Failure(new FrameworkError(ErrorKind.InvalidState, "event.scope.closed"));
            var subscription = source.Subscribe(listener);
            try
            {
                var result = scope.TrackSubscription(id, subscription);
                if (!result.IsSuccess) subscription.Dispose();
                return result;
            }
            catch { subscription.Dispose(); throw; }
        }
    }
}
