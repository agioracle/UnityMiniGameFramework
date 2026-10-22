using System;
using System.Collections.Generic;

namespace MiniGame.Framework
{
    public sealed class PauseGate : IDisposable
    {
        private sealed class Lease : IDisposable
        {
            private PauseGate owner;
            private readonly long id;
            public Lease(PauseGate owner, long id) { this.owner = owner; this.id = id; }
            public void Dispose()
            {
                PauseGate current = owner;
                owner = null;
                current?.owners.Remove(id);
            }
        }

        private readonly HashSet<long> owners = new HashSet<long>();
        private long next;
        private bool disposed;
        public bool IsPaused => owners.Count != 0;
        public int Count => owners.Count;

        public IDisposable Acquire()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PauseGate));
            long id = checked(++next);
            owners.Add(id);
            return new Lease(this, id);
        }

        public void Dispose()
        {
            disposed = true;
            owners.Clear();
        }
    }
}
