using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MiniGame.Framework
{
    public sealed class FrameworkRuntime : IDisposable
    {
        public enum Lifecycle { Created, Initializing, Ready, Stopped }
        public Lifecycle State { get; private set; }
        public TimerService Timers { get; private set; }
        public PauseGate Pauses { get; private set; }
        private int callbackBudget;
        private CancellationTokenSource lifetime;

        public async UniTask InitializeAsync(FrameworkOptions options, CancellationToken cancellation = default)
        {
            if (State != Lifecycle.Created) throw new InvalidOperationException("Runtime can only initialize once.");
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            State = Lifecycle.Initializing;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            CancellationToken token = lifetime.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                callbackBudget = options.TimerCallbacksPerFrame;
                Timers = new TimerService(options.TimerCapacity);
                Timers.CallbackFailed += Report;
                Pauses = new PauseGate();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                token.ThrowIfCancellationRequested();
                if (State != Lifecycle.Initializing) throw new OperationCanceledException(token);
                State = Lifecycle.Ready;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Tick(float scaledDelta, float unscaledDelta)
        {
            if (State != Lifecycle.Ready) return;
            Timers.Tick(Pauses.IsPaused ? 0 : scaledDelta, unscaledDelta, callbackBudget);
        }

        public void Dispose()
        {
            if (State == Lifecycle.Stopped) return;
            State = Lifecycle.Stopped;
            try { lifetime?.Cancel(); }
            catch (Exception error) { Report(error); }
            try { Timers?.Dispose(); }
            catch (Exception error) { Report(error); }
            try { Pauses?.Dispose(); }
            catch (Exception error) { Report(error); }
            lifetime?.Dispose();
            lifetime = null;
        }

        private static void Report(Exception error) => Debug.LogException(error);
    }
}
