using System;
using System.Collections.Generic;

namespace MiniGame.Framework
{
    public sealed class StateMachine<TState> : IDisposable
    {
        private sealed class State
        {
            public Action Enter;
            public Action Exit;
            public Action<float> Update;
        }

        private readonly Dictionary<TState, State> states = new Dictionary<TState, State>();
        private State active;
        private bool transitioning;
        private bool disposed;
        public bool IsRunning => active != null;
        public TState Current { get; private set; }

        public void Add(TState id, Action enter = null, Action exit = null, Action<float> update = null)
        {
            Guard();
            if (active != null || transitioning) throw new InvalidOperationException("Register states before starting.");
            states.Add(id, new State { Enter = enter, Exit = exit, Update = update });
        }

        public void Change(TState id)
        {
            Guard();
            if (transitioning) throw new InvalidOperationException("Transitions inside Enter/Exit are not supported.");
            if (!states.TryGetValue(id, out State next)) throw new KeyNotFoundException("State is not registered.");
            if (ReferenceEquals(active, next)) return;
            transitioning = true;
            State previous = active;
            active = null;
            Current = default;
            try
            {
                previous?.Exit?.Invoke();
                Current = id;
                active = next;
                next.Enter?.Invoke();
            }
            catch
            {
                active = null;
                Current = default;
                throw;
            }
            finally { transitioning = false; }
        }

        public void Tick(float deltaTime)
        {
            Guard();
            if (deltaTime < 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            active?.Update?.Invoke(deltaTime);
        }

        public void Dispose()
        {
            if (disposed) return;
            if (transitioning) throw new InvalidOperationException("Cannot dispose during a transition.");
            disposed = true;
            State previous = active;
            active = null;
            Current = default;
            states.Clear();
            previous?.Exit?.Invoke();
        }

        private void Guard()
        {
            if (disposed) throw new ObjectDisposedException(nameof(StateMachine<TState>));
        }
    }
}
