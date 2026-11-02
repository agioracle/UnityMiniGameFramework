using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // Managed objects only: no external resources, Unity objects, or destruction callbacks.
    public sealed class ObjectPool<T> : IDisposable, IDiagnosticSource where T : class
    {
        public DiagnosticCounters ReadDiagnostics() => new DiagnosticCounters{ActiveRentals=ActiveCount};
        private sealed class Identity : IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
        public readonly struct Lease : IDisposable
        {
            private readonly ObjectPool<T> owner;
            private readonly T value;
            private readonly long generation;
            internal Lease(ObjectPool<T> owner, T value, long generation)
            { this.owner = owner; this.value = value; this.generation = generation; }
            public T Value
            {
                get
                {
                    CleanupWait.MainThread();
                    if (owner == null || !owner.IsActive(value, generation)) throw new ObjectDisposedException(nameof(Lease));
                    return value;
                }
            }
            public Result Return() => owner == null ? CleanupWait.Error(ErrorKind.InvalidState, "pool.lease.default") : owner.Return(value, generation);
            public void Dispose()
            {
                var result = Return();
                if (!result.IsSuccess) throw new InvalidOperationException(result.Error.Code);
            }
        }
        private readonly Func<T> factory;
        private readonly Action<T> reset;
        private readonly int capacity;
        private readonly Stack<T> idle;
        private readonly HashSet<T> known = new HashSet<T>(new Identity());
        private readonly Dictionary<T, long> active = new Dictionary<T, long>(new Identity());
        private long generation;
        private bool closed, busy;
        public int ActiveCount { get { CleanupWait.MainThread(); return active.Count; } }
        public int CachedCount { get { CleanupWait.MainThread(); return idle.Count; } }
        public ObjectPool(Func<T> factory, Action<T> reset = null, int capacity = 32)
        {
            CleanupWait.MainThread();
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.factory = factory; this.reset = reset; this.capacity = capacity; idle = new Stack<T>(capacity);
        }
        public Result<Lease> Rent()
        {
            CleanupWait.MainThread();
            if (closed || busy || generation == long.MaxValue) return Fail("pool.closed_or_busy");
            busy = true;
            try
            {
                T value;
                if (idle.Count > 0) value = idle.Pop();
                else
                {
                    value = factory();
                    if (value == null || !known.Add(value)) return Fail("pool.factory.identity");
                }
                var version = ++generation; active.Add(value, version);
                return Result<Lease>.Success(new Lease(this, value, version));
            }
            catch (Exception) { return Result<Lease>.Failure(new FrameworkError(ErrorKind.Unexpected, "pool.factory")); }
            finally { busy = false; }
        }
        public Result<Lease> Rent(IScope scope, string id)
        {
            CleanupWait.MainThread();
            if (scope == null || scope.State != ScopeState.Open || string.IsNullOrWhiteSpace(id)) return Fail("pool.scope");
            var holder = new ScopedLease();
            var tracked = scope.TrackSubscription(id, holder);
            if (!tracked.IsSuccess) return Result<Lease>.Failure(tracked.Error);
            var rented = Rent();
            if (rented.IsSuccess) { holder.Lease = rented.Value; holder.HasLease = true; }
            holder.Pending = false;
            // Reentrant scope close records Failed while the factory is in flight.
            // The holder retains the eventual lease for explicit cleanup retry.
            if (scope.State != ScopeState.Open) return Fail("pool.scope.closed_during_rent");
            return rented;
        }
        private sealed class ScopedLease : IDisposable
        {
            internal Lease Lease;
            internal bool HasLease;
            internal bool Pending = true;
            public void Dispose()
            {
                if (Pending) throw new InvalidOperationException("Rental factory is still executing.");
                if (HasLease) Lease.Dispose();
            }
        }

        private bool IsActive(T value, long version) => !closed && active.TryGetValue(value, out var found) && found == version;
        private Result Return(T value, long version)
        {
            CleanupWait.MainThread();
            if (busy) return CleanupWait.Error(ErrorKind.InvalidState, "pool.busy");
            // A stale copy is an idempotent no-op, never a return of a newer rental.
            if (!IsActive(value, version)) return Result.Success();
            busy = true;
            try
            {
                reset?.Invoke(value);
                active.Remove(value);
                if (idle.Count < capacity) idle.Push(value); else known.Remove(value);
                return Result.Success();
            }
            catch (Exception) { return CleanupWait.Error(ErrorKind.CleanupFailed, "pool.reset"); }
            finally { busy = false; }
        }
        public void Dispose()
        {
            CleanupWait.MainThread();
            if (closed) return;
            if (busy || active.Count != 0) throw new InvalidOperationException("Return active leases before closing pool.");
            closed = true; idle.Clear(); known.Clear();
        }
        private static Result<Lease> Fail(string code) => Result<Lease>.Failure(new FrameworkError(ErrorKind.InvalidState, code));
    }
}
