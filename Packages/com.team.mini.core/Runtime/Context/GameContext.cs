using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Kernel;

namespace MiniFramework.Runtime
{
    // Calls and module completions belong to the creating thread (Unity main thread).
    // Only wait observations may terminate early; owned lifecycle work is always observed.
    public sealed class GameContext : IGameServices, IDiagnosticSource
    {
        private sealed class Entry
        {
            public ModuleRegistration Registration;
            public IModule Module;
            public ICleanupRegistry Cleanup;
            public ModuleState State = ModuleState.Created;
            public bool StopFailed;
            public bool DisposeAttempted, DisposeFailed;
            public Scope Scope;
        }
        private class ModuleContext : IModuleContext, IServiceResolver
        {
            private readonly GameContext owner;
            private readonly Entry entry;
            public ModuleContext(GameContext owner, Entry entry) { this.owner = owner; this.entry = entry; }
            public IServiceResolver Services => this;
            public CancellationToken LifetimeCancellation => entry.Scope?.LifetimeCancellation ?? owner.lifetimeToken;
            public ICleanupRegistry Cleanup => entry.Cleanup;
            public bool TryGet<T>(out T service) where T : class
            {
                owner.CheckThread();
                service = null;
                if (owner.State != GameContextState.Starting && owner.State != GameContextState.Ready) return false;
                foreach (var candidate in owner.created)
                    if (entry.Registration.Dependencies.Contains(candidate.Registration.Id) &&
                        candidate.State == ModuleState.Ready && candidate.Registration.Services.Contains(typeof(T)))
                    { service = candidate.Module as T; return service != null; }
                return false;
            }
        }
        private sealed class ScopedModuleContext : ModuleContext, IScopedModuleContext
        {
            public IScope Scope { get; }
            public ScopedModuleContext(GameContext owner,Entry entry) : base(owner,entry) { Scope=entry.Scope; }
        }
        private readonly ModuleRegistration[] registrations;
        private readonly Func<string, ICleanupRegistry> cleanupFactory;
        private readonly CleanupContext cleanupPolicy;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private readonly List<Entry> created = new List<Entry>();
        private readonly List<FrameworkError> diagnostics = new List<FrameworkError>();
        private readonly UniTaskCompletionSource startupDrained = new UniTaskCompletionSource();
        private UniTaskCompletionSource<Result> closed = new UniTaskCompletionSource<Result>();
        private UniTask startupDriver, closeDriver;
        private readonly List<FrameworkError> lateDiagnostics = new List<FrameworkError>();
        public IReadOnlyList<FrameworkError> LateCompletionDiagnostics => lateDiagnostics.AsReadOnly();
        private Result startupResult;
        private bool startupSettled, closeStarted;
        private OperationContext startupWait;
        private long startupTimestamp;
        public GameContextState State { get; private set; } = GameContextState.Created;
        public IReadOnlyList<FrameworkError> CleanupDiagnostics => diagnostics.AsReadOnly();
        public int RetainedModuleCount => created.Count;

        public readonly struct ModuleSnapshot
        {
            public readonly string Id;
            public readonly ModuleState State;
            public ModuleSnapshot(string id,ModuleState state){Id=id;State=state;}
        }
        public ModuleSnapshot[] CaptureModules()
        {
            CheckThread();var result=new ModuleSnapshot[created.Count];
            for(var i=0;i<result.Length;i++)result[i]=new ModuleSnapshot(created[i].Registration.Id,created[i].State);
            return result;
        }
        public DiagnosticCounters ReadDiagnostics()
        {
            CheckThread();var result=new DiagnosticCounters{RetainedModules=created.Count};
            foreach(var entry in created)if(entry.Scope!=null)result.Add(entry.Scope.ReadDiagnostics());
            return result;
        }
        public GameContext(IEnumerable<ModuleRegistration> registrations, CleanupContext cleanupPolicy)
            : this(registrations,id => new Scope(id,ScopeKind.App),cleanupPolicy) { }

        public GameContext(IEnumerable<ModuleRegistration> registrations, Func<string, ICleanupRegistry> cleanupFactory, CleanupContext cleanupPolicy)
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("Create GameContext on Unity main thread.");
            this.registrations = (registrations ?? throw new ArgumentNullException(nameof(registrations))).ToArray();
            this.cleanupFactory = cleanupFactory ?? throw new ArgumentNullException(nameof(cleanupFactory));
            this.cleanupPolicy = cleanupPolicy;
            lifetimeToken = lifetime.Token;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("GameContext must be accessed on its owner thread.");
        }
        private static Result Fail(ErrorKind kind, string code, string message = "") => Result.Failure(new FrameworkError(kind, code, message));
        private static bool Expired(OperationContext operation, long started) => operation.Timeout != Timeout.InfiniteTimeSpan &&
            (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency >= operation.Timeout.TotalSeconds;
        private Result? Interrupted(OperationContext operation, long started)
        {
            if (operation.Cancellation.IsCancellationRequested) return Fail(ErrorKind.Canceled, "context.canceled");
            if (Expired(operation, started)) return Fail(ErrorKind.Timeout, "context.timeout");
            return null;
        }
        public bool TryGet<T>(out T service) where T : class
        {
            CheckThread(); service = null;
            if (State != GameContextState.Ready) return false;
            foreach (var entry in created)
                if (entry.State == ModuleState.Ready && entry.Registration.Services.Contains(typeof(T)))
                { service = entry.Module as T; return service != null; }
            return false;
        }
        public ModuleState? GetModuleState(string id)
        {
            CheckThread(); return created.FirstOrDefault(e => e.Registration.Id == id)?.State;
        }
        // Deterministic DFS: registration order, then declared dependency order. No factories on graph failure.
        private List<ModuleRegistration> Sort()
        {
            var byId = new Dictionary<string, ModuleRegistration>(StringComparer.Ordinal);
            var serviceTypes = new HashSet<Type>();
            foreach (var r in registrations)
            {
                if (r == null || byId.ContainsKey(r.Id)) throw new ArgumentException("Null registration or duplicate module id.");
                byId.Add(r.Id, r);
                foreach (var type in r.Services)
                    if (type == null || !type.IsInterface || !serviceTypes.Add(type))
                        throw new ArgumentException("Services must be unique interface types.");
            }
            var marks = new Dictionary<string, int>();
            var result = new List<ModuleRegistration>();
            foreach (var r in registrations) Visit(r, byId, marks, result);
            return result;
        }
        private static void Visit(ModuleRegistration r, Dictionary<string, ModuleRegistration> byId, Dictionary<string, int> marks, List<ModuleRegistration> result)
        {
            if (marks.TryGetValue(r.Id, out var mark))
            { if (mark == 1) throw new ArgumentException("Dependency cycle at " + r.Id); return; }
            marks[r.Id] = 1;
            var seen = new HashSet<string>();
            foreach (var id in r.Dependencies)
            {
                if (id == null || id == r.Id || !seen.Add(id) || !byId.TryGetValue(id, out var dependency))
                    throw new ArgumentException("Invalid dependency for " + r.Id + ": " + id);
                Visit(dependency, byId, marks, result);
            }
            marks[r.Id] = 2; result.Add(r);
        }
        public UniTask<Result> StartAsync(OperationContext operation)
        {
            CheckThread();
            if (State != GameContextState.Created) return UniTask.FromResult(Fail(ErrorKind.InvalidState, "context.start.once"));
            State = GameContextState.Starting;
            startupWait = operation; startupTimestamp = Stopwatch.GetTimestamp();
            var interrupted = Interrupted(operation, startupTimestamp);
            if (interrupted.HasValue)
            { SetStartup(interrupted.Value); startupDrained.TrySetResult(); BeginClose(); }
            else startupDriver = RunStartupAsync().Preserve();
            return ObserveStartupAsync();
        }
        private void SetStartup(Result result)
        {
            if (startupSettled) return;
            startupResult = result; startupSettled = true;
        }
        private async UniTask<Result> ObserveStartupAsync()
        {
            while (!startupSettled)
            {
                var interrupted = Interrupted(startupWait, startupTimestamp);
                if (interrupted.HasValue) { SetStartup(interrupted.Value); BeginClose(); break; }
                await UniTask.Yield();
            }
            return startupResult;
        }
        private async UniTask RunStartupAsync()
        {
            try
            {
                List<ModuleRegistration> order;
                try { order = Sort(); }
                catch (Exception error) { SetStartup(Fail(ErrorKind.Dependency, "context.graph.invalid", error.Message)); return; }
                foreach (var registration in order)
                {
                    if (startupSettled) return;
                    // Record immediately after creation, before inspecting metadata or creating the registry.
                    var module = registration.Factory();
                    if (module == null) throw new InvalidOperationException("Factory returned null: " + registration.Id);
                    var entry = new Entry { Registration = registration, Module = module };
                    created.Add(entry);
                    if (created.Any(e => e != entry && ReferenceEquals(e.Module, module)))
                    { created.Remove(entry); throw new InvalidOperationException("Module instance reused."); }
                    if (module.Id != registration.Id || module.Dependencies == null || !module.Dependencies.SequenceEqual(registration.Dependencies))
                        throw new InvalidOperationException("Module metadata disagrees with registration: " + registration.Id);
                    if (registration.Services.Any(type => !type.IsInstanceOfType(module)))
                        throw new InvalidOperationException("Module does not implement declared service: " + registration.Id);
                    var registry = cleanupFactory(registration.Id) ?? throw new InvalidOperationException("Cleanup factory returned null.");
                    if (registry.State != CleanupRegistryState.Open || created.Any(e => e != entry && ReferenceEquals(e.Cleanup, registry)))
                        throw new InvalidOperationException("Each module requires a fresh, open cleanup registry.");
                    entry.Cleanup = registry; entry.Scope=registry as Scope;
                }
                foreach (var entry in created)
                {
                    var interrupted = Interrupted(startupWait, startupTimestamp);
                    if (startupSettled || interrupted.HasValue) { if (interrupted.HasValue) SetStartup(interrupted.Value); return; }
                    entry.State = ModuleState.Initializing;
                    Result result;
                    try { result = await entry.Module.InitializeAsync((entry.Scope == null ? new ModuleContext(this, entry) : new ScopedModuleContext(this, entry)), new OperationContext(lifetime.Token, startupWait.Timeout)); }
                    catch (Exception error) { result = Fail(ErrorKind.Unexpected, "context.initialize.exception", error.GetType().Name); }
                    await UniTask.SwitchToMainThread();
                    CheckThread();
                    interrupted = Interrupted(startupWait, startupTimestamp);
                    if (startupSettled || interrupted.HasValue)
                    { entry.State = ModuleState.Failed; if (!result.IsSuccess) lateDiagnostics.Add(result.Error); if (interrupted.HasValue) SetStartup(interrupted.Value); return; }
                    if (!result.IsSuccess) { entry.State = ModuleState.Failed; SetStartup(result); return; }
                    entry.State = ModuleState.Ready;
                }
                State = GameContextState.Ready; SetStartup(Result.Success());
            }
            catch (Exception error) { SetStartup(Fail(ErrorKind.Unexpected, "context.composition.exception", error.GetType().Name)); }
            finally
            {
                startupDrained.TrySetResult();
                if (!startupResult.IsSuccess) BeginClose();
            }
        }
        private void BeginClose()
        {
            if (closeStarted) return;
            closeStarted = true;
            var wasCreated = State == GameContextState.Created;
            State = GameContextState.Closing; // Gate access BEFORE cancellation callbacks run.
            if (!startupSettled) SetStartup(Fail(ErrorKind.Canceled, "context.closed.during.start"));
            if (wasCreated) startupDrained.TrySetResult();
            foreach(var entry in created) entry.Scope?.GateTree();
            foreach(var entry in created) entry.Scope?.CancelTree();
            try { lifetime.Cancel(); }
            catch (Exception error) { diagnostics.Add(new FrameworkError(ErrorKind.CleanupFailed, "context.cancel.callback", error.GetType().Name)); }
            closeDriver = RunCloseAsync().Preserve();
        }
        // Each caller has its own observation budget. First close starts one owned drain.
        // Canceling a waiter never cancels that drain or authorizes duplicate dispose.
        public UniTask<Result> CloseAsync(OperationContext wait)
        {
            CheckThread(); BeginClose(); return ObserveCloseAsync(wait);
        }
        private async UniTask<Result> ObserveCloseAsync(OperationContext wait)
        {
            var completion = closed; // A retry generation cannot replace this waiter's outcome.
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                // Check signals before accepting a buffered completion on every batch,
                // including the frame in which the shared drain finished.
                var interrupted = Interrupted(wait, started);
                if (interrupted.HasValue) return interrupted.Value;
                if (completion.Task.Status != UniTaskStatus.Pending) return await completion.Task;
                await UniTask.Yield();
            }
        }
        private async UniTask InvokeCleanup(Func<UniTask<Result>> phase, string id, string name)
        {
            try
            {
                var result = await phase();
                await UniTask.SwitchToMainThread();
                if (!result.IsSuccess) diagnostics.Add(new FrameworkError(ErrorKind.CleanupFailed, "context." + name, id + ": " + result.Error.Code));
            }
            catch (Exception error) { await UniTask.SwitchToMainThread(); diagnostics.Add(new FrameworkError(ErrorKind.CleanupFailed, "context." + name + ".exception", id + ": " + error.GetType().Name)); }
        }
        private async UniTask RunCloseAsync()
        {
            await startupDrained.Task; // Never dispose while initialization can still mutate the instance.
            for (var i = created.Count - 1; i >= 0; --i)
            {
                var entry = created[i]; entry.State = ModuleState.Stopping;
                var before = diagnostics.Count;
                await InvokeCleanup(() => entry.Module.StopAsync(cleanupPolicy), entry.Registration.Id, "stop");
                entry.StopFailed = diagnostics.Count != before;
            }
            await DrainModulesAsync(cleanupPolicy,false);
        }
        // Only ticket/registry failures are retryable here. Never replay a failed module phase.
        public UniTask<Result> RetryCleanupAsync(CleanupContext cleanup,OperationContext wait)
        {
            CheckThread();
            if(State==GameContextState.Closing || State==GameContextState.Closed) return ObserveCloseAsync(wait);
            if(State!=GameContextState.CleanupFailed || diagnostics.Any(error=>!error.Code.StartsWith("context.registry.",StringComparison.Ordinal)))
                return UniTask.FromResult(Fail(ErrorKind.InvalidState,"context.retry.module.phase.unsupported"));
            diagnostics.Clear();
            closed=new UniTaskCompletionSource<Result>();State=GameContextState.Closing;
            closeDriver=DrainModulesAsync(cleanup,true).Preserve();
            return ObserveCloseAsync(wait);
        }
        private async UniTask DrainModulesAsync(CleanupContext cleanup,bool retry)
        {
            for(var i=created.Count-1;i>=0;--i)
            {
                var entry=created[i];
                if(entry.State==ModuleState.Disposed) continue;
                entry.State=ModuleState.Disposing;
                if(!entry.DisposeAttempted)
                {
                    entry.DisposeAttempted=true;var before=diagnostics.Count;
                    await InvokeCleanup(()=>entry.Module.DisposeAsync(cleanup),entry.Registration.Id,"dispose");
                    entry.DisposeFailed=diagnostics.Count!=before;
                }
                var registryFailed=false;
                if(entry.Cleanup!=null)
                {
                    try
                    {
                        CleanupReport report;
                        if(retry && entry.Cleanup is IRetryableCleanupRegistry retryable && retryable.Snapshot.FailedCount>0)
                            report=await retryable.RetryFailedAsync(cleanup);
                        else report=await entry.Cleanup.CloseAsync(cleanup);
                        await UniTask.SwitchToMainThread();
                        while(report.PendingCount>0)
                        {
                            await UniTask.Yield();report=await entry.Cleanup.CloseAsync(cleanup);
                            await UniTask.SwitchToMainThread();
                        }
                        registryFailed=!report.IsFullyDrained;
                        if(registryFailed) diagnostics.Add(new FrameworkError(ErrorKind.CleanupFailed,"context.registry.not.drained",entry.Registration.Id));
                    }
                    catch(Exception error)
                    {
                        await UniTask.SwitchToMainThread();registryFailed=true;
                        diagnostics.Add(new FrameworkError(ErrorKind.CleanupFailed,"context.registry.exception",error.GetType().Name));
                    }
                }
                entry.State=entry.StopFailed || entry.DisposeFailed || registryFailed ? ModuleState.CleanupFailed : ModuleState.Disposed;
                // Failed tickets still own resources; their provider dependencies remain intact for retry.
                if(registryFailed) break;
            }
            State=diagnostics.Count==0 ? GameContextState.Closed : GameContextState.CleanupFailed;
            if(State==GameContextState.Closed) { lifetime.Dispose();created.Clear(); }
            closed.TrySetResult(State==GameContextState.Closed ? Result.Success() : Fail(ErrorKind.CleanupFailed,"context.close.failed"));
        }
    }
}
