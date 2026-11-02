using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class StateMachine<TKey> : IDisposable
    {
        private readonly Dictionary<TKey, IStateNode> nodes = new Dictionary<TKey, IStateNode>();
        private IStateNode current;
        private TKey key;
        private bool busy;
        public StateMachineStatus Status { get; private set; }
        public TKey Current { get { CleanupWait.MainThread(); if (current == null) throw new InvalidOperationException(); return key; } }
        public Result Add(TKey id, IStateNode node)
        {
            CleanupWait.MainThread();
            if (busy || Status != StateMachineStatus.Stopped) return Invalid();
            if (id == null || node == null || nodes.ContainsKey(id) || nodes.ContainsValue(node))
                return CleanupWait.Error(ErrorKind.InvalidArgument, "fsm.registration");
            nodes.Add(id, node); return Result.Success();
        }
        public Result Change(TKey id)
        {
            CleanupWait.MainThread();
            if (busy || Status == StateMachineStatus.Closed || Status == StateMachineStatus.Faulted) return Invalid();
            if (id == null || !nodes.TryGetValue(id, out var next)) return CleanupWait.Error(ErrorKind.InvalidArgument, "fsm.missing");
            if (ReferenceEquals(next, current)) return Result.Success();
            busy = true;
            try
            {
                if (current != null)
                {
                    var exit = Invoke(current.Exit);
                    if (!exit.IsSuccess) { Status = StateMachineStatus.Faulted; return exit; }
                }
                current = next; key = id; // Partial Enter still owns Exit cleanup.
                var enter = Invoke(next.Enter);
                Status = enter.IsSuccess ? StateMachineStatus.Running : StateMachineStatus.Faulted;
                return enter;
            }
            finally { busy = false; }
        }
        public Result Tick(float deltaTime)
        {
            CleanupWait.MainThread();
            if (busy || Status != StateMachineStatus.Running) return Invalid();
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                return CleanupWait.Error(ErrorKind.InvalidArgument, "fsm.delta");
            busy = true;
            try
            {
                Result result;
                try { result = current.Tick(deltaTime); }
                catch (Exception) { result = CleanupWait.Error(ErrorKind.Unexpected, "fsm.callback"); }
                if (!result.IsSuccess) Status = StateMachineStatus.Faulted;
                return result;
            }
            finally { busy = false; }
        }
        public Result Stop()
        {
            CleanupWait.MainThread();
            if (busy) return Invalid();
            if (Status == StateMachineStatus.Closed) return Result.Success();
            busy = true;
            try
            {
                if (current != null)
                {
                    var result = Invoke(current.Exit);
                    if (!result.IsSuccess) { Status = StateMachineStatus.Faulted; return result; }
                }
                current = null; key = default; Status = StateMachineStatus.Stopped;
                return Result.Success();
            }
            finally { busy = false; }
        }
        public void Dispose()
        {
            var result = Stop();
            if (!result.IsSuccess) throw new InvalidOperationException(result.Error.Code);
            Status = StateMachineStatus.Closed; nodes.Clear();
        }
        private static Result Invoke(Func<Result> callback)
        {
            try { return callback(); }
            catch (Exception) { return CleanupWait.Error(ErrorKind.Unexpected, "fsm.callback"); }
        }
        private static Result Invalid() => CleanupWait.Error(ErrorKind.InvalidState, "fsm.state");
    }
}
