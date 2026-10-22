using System;

namespace MiniGame.Framework
{
    public readonly struct TimerHandle
    {
        internal readonly TimerService Owner;
        internal readonly int Index;
        internal readonly ulong Generation;

        internal TimerHandle(TimerService owner, int index, ulong generation)
        {
            Owner = owner;
            Index = index;
            Generation = generation;
        }

        public bool IsActive => Owner != null && Owner.IsActive(this);
    }

    public sealed class TimerService : IDisposable
    {
        private struct Slot
        {
            public Action Callback;
            public double Due;
            public double Interval;
            public ulong Generation;
            public ulong EligibleTick;
            public bool Unscaled;
        }

        private readonly Slot[] slots;
        private double scaledTime;
        private double unscaledTime;
        private ulong tick;
        private int cursor;
        private bool ticking;
        private bool disposed;

        public int Count { get; private set; }
        public int Capacity => slots.Length;
        public event Action<Exception> CallbackFailed;

        public TimerService(int capacity = 128)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            slots = new Slot[capacity];
        }

        public TimerHandle Schedule(double delay, Action callback, bool unscaled = false, double interval = 0)
        {
            ThrowIfDisposed();
            ValidateTime(delay, nameof(delay));
            ValidateTime(interval, nameof(interval));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Callback != null || slots[i].Generation == ulong.MaxValue) continue;
                ref Slot slot = ref slots[i];
                slot.Generation++;
                slot.Callback = callback;
                slot.Due = (unscaled ? unscaledTime : scaledTime) + delay;
                slot.Interval = interval;
                slot.Unscaled = unscaled;
                slot.EligibleTick = tick + 1;
                Count++;
                return new TimerHandle(this, i, slot.Generation);
            }
            throw new InvalidOperationException("Timer capacity exhausted.");
        }

        public bool Cancel(TimerHandle handle)
        {
            if (!IsActive(handle)) return false;
            slots[handle.Index].Callback = null;
            Count--;
            return true;
        }

        public bool IsActive(TimerHandle handle)
        {
            return !disposed && ReferenceEquals(handle.Owner, this) && handle.Index >= 0 &&
                   handle.Index < slots.Length && slots[handle.Index].Callback != null &&
                   slots[handle.Index].Generation == handle.Generation;
        }

        public void Tick(double scaledDelta, double unscaledDelta, int callbackBudget = 64)
        {
            ThrowIfDisposed();
            ValidateTime(scaledDelta, nameof(scaledDelta));
            ValidateTime(unscaledDelta, nameof(unscaledDelta));
            if (callbackBudget < 1) throw new ArgumentOutOfRangeException(nameof(callbackBudget));
            if (ticking) throw new InvalidOperationException("Timer Tick is not reentrant.");
            scaledTime += scaledDelta;
            unscaledTime += unscaledDelta;
            tick++;
            ticking = true;
            try
            {
                for (int visited = 0; visited < slots.Length && callbackBudget > 0 && !disposed; visited++)
                {
                    int i = cursor;
                    cursor = (cursor + 1) % slots.Length;
                    ref Slot slot = ref slots[i];
                    double now = slot.Unscaled ? unscaledTime : scaledTime;
                    if (slot.Callback == null || slot.EligibleTick > tick || slot.Due > now) continue;
                    Action callback = slot.Callback;
                    if (slot.Interval > 0)
                    {
                        slot.Due = now + slot.Interval;
                        slot.EligibleTick = tick + 1;
                    }
                    else
                    {
                        slot.Callback = null;
                        Count--;
                    }
                    callbackBudget--;
                    try { callback(); }
                    catch (Exception error)
                    {
                        if (CallbackFailed == null) throw;
                        CallbackFailed(error);
                    }
                }
            }
            finally { ticking = false; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < slots.Length; i++) slots[i].Callback = null;
            Count = 0;
            CallbackFailed = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(TimerService));
        }

        private static void ValidateTime(double value, string name)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
