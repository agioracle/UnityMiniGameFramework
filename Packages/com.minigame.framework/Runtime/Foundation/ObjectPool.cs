using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MiniGame.Framework
{
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private sealed class IdentityComparer : IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Stack<T> available;
        private readonly HashSet<T> rented;
        private readonly Func<T> create;
        private readonly Action<T> reset;
        private readonly Action<T> destroy;
        private bool disposed;
        private bool creating;
        public int Capacity { get; }
        public int Count { get; private set; }
        public int RentedCount => rented.Count;

        public ObjectPool(int capacity, Func<T> create, Action<T> reset = null, Action<T> destroy = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.reset = reset;
            this.destroy = destroy;
            Capacity = capacity;
            available = new Stack<T>(capacity);
            rented = new HashSet<T>(capacity, new IdentityComparer());
        }

        public bool TryRent(out T item)
        {
            Guard();
            if (creating) throw new InvalidOperationException("Pool factory cannot rent recursively.");
            if (available.Count != 0) item = available.Pop();
            else if (Count < Capacity)
            {
                creating = true;
                try { item = create() ?? throw new InvalidOperationException("Pool factory returned null."); }
                finally { creating = false; }
                if (disposed)
                {
                    destroy?.Invoke(item);
                    throw new ObjectDisposedException(nameof(ObjectPool<T>));
                }
                if (rented.Contains(item)) throw new InvalidOperationException("Factory must create a distinct object.");
                Count++;
            }
            else { item = null; return false; }
            rented.Add(item);
            return true;
        }

        public bool Return(T item)
        {
            if (disposed || item == null || !rented.Remove(item)) return false;
            try { reset?.Invoke(item); }
            catch
            {
                if (!disposed) Count--;
                destroy?.Invoke(item);
                throw;
            }
            if (disposed)
            {
                destroy?.Invoke(item);
                return false;
            }
            available.Push(item);
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            List<Exception> failures = null;
            foreach (T item in rented) Destroy(item, ref failures);
            rented.Clear();
            while (available.Count > 0) Destroy(available.Pop(), ref failures);
            Count = 0;
            if (failures != null) throw new AggregateException(failures);
        }

        private void Destroy(T item, ref List<Exception> failures)
        {
            try { destroy?.Invoke(item); }
            catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
        }

        private void Guard()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
        }
    }
}
