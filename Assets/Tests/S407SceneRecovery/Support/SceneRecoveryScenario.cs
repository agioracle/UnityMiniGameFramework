using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiniFramework.S407SceneRecovery
{
    [Serializable] public sealed class SceneRecoveryCase
    {
        public string name, error = "", observedError = "";
        public bool passed, deferredObserved, closeWaited, canceledObserver, previousSessionPreserved,
            newSessionPreserved, retryRequired, retrySucceeded, distinctHandles, secondInstanceProtected,
            injectedCancelAtLoadStart, injectedUnloadRejection, bootstrapPreserved;
        public int nativeLoads, unloadAttempts, successfulUnloads, nativeActivations, peakHandles,
            modules, sceneLeases, providerHandles, flowScopes, fixtureScenes;
    }
    public sealed class SceneRecoveryScenario
    {
        public const string Directory = "Assets/__S407SceneRecovery";
        public static readonly string[] Names = { "deferred_owner_close", "canceled_flow_load", "flow_unload_retry", "same_path_independent_handles" };
        public readonly List<SceneRecoveryCase> Cases = new List<SceneRecoveryCase>();
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        static OperationContext Wait => OperationContext.Unlimited();
        public static void Require(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
        static ResourceKey Key(string address = "A") => ResourceKey.Scene("native", "fixture", "v1", address);
        static string PathFor(string address) => Directory + "/" + address + ".unity";
        static int FixtureScenes()
        {
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path.StartsWith(Directory + "/", StringComparison.Ordinal)) count++;
            return count;
        }
        sealed class Env
        {
            internal readonly UnitySceneProvider Provider;
            internal readonly SceneService Scenes;
            internal readonly SessionFlow Flow;
            internal readonly GameContext Context;
            internal readonly Scope Owner = new Scope("scene-recovery-owner", ScopeKind.Session);
            internal AsyncOperation LastLoad;
            internal Action OnLoadStart;
            internal bool RejectUnload;
            internal int UnloadAttempts, SuccessfulUnloads;
            internal Env()
            {
                Provider = new UnitySceneProvider("native", "fixture", "v1", new Dictionary<string, string> { { "A", PathFor("A") }, { "B", PathFor("B") } },
                    path => {
#if UNITY_EDITOR
                        LastLoad = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
#else
                        LastLoad = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
#endif
                        Require(LastLoad != null, "native.load.null"); OnLoadStart?.Invoke(); return LastLoad;
                    }, scene => { UnloadAttempts++; if (RejectUnload) return null; SuccessfulUnloads++; return SceneManager.UnloadSceneAsync(scene); });
                Scenes = new SceneService(Provider, "native", "fixture", "v1", Cleanup);
                Flow = new SessionFlow(Cleanup, Scenes);
                Context = new GameContext(new[] {
                    new ModuleRegistration(Scenes.Id, Scenes.Dependencies, () => Scenes, typeof(ISceneService)),
                    new ModuleRegistration(Flow.Id, Flow.Dependencies, () => Flow, typeof(ISessionFlow)) }, Cleanup);
            }
            internal async UniTask<ISceneLease> Load(bool activate = true)
            {
                var op = Scenes.Load(Key(), new SceneLoadOptions(SceneLoadMode.Additive, activate), Owner);
                Require(op.IsSuccess, "load.accept"); var result = await op.Value.WaitAsync(Wait);
                Require(result.IsSuccess, "load.result"); return result.Value;
            }
            internal async UniTask Finish()
            {
                // Drain real accepted operations even if a scenario assertion failed.
                RejectUnload = false; OnLoadStart = null;
                await Owner.CloseAsync(Cleanup); await Owner.RetryFailedAsync(Cleanup);
                var close = await Context.CloseAsync(Wait);
                if (!close.IsSuccess) close = await Context.RetryCleanupAsync(Cleanup, Wait);
                Require(close.IsSuccess, "context.close");
            }
        }
        public async UniTask Execute() { foreach (var name in Names) Require((await Run(name)).passed, "case.failed." + name); }
        public async UniTask<SceneRecoveryCase> Run(string name)
        {
            Require(Array.IndexOf(Names, name) >= 0, "case.unknown");
            Require(FixtureScenes() == 0, "fixture.preexisting");
            var bootstrap = SceneManager.GetActiveScene(); var env = new Env();
            int enabled = SceneRecoveryWitness.Enabled;
            var row = new SceneRecoveryCase { name = name }; Cases.Add(row);
            try
            {
                Require((await env.Context.StartAsync(Wait)).IsSuccess, "context.start");
                if (name == Names[0])
                {
                    var lease = await env.Load(false);
                    row.deferredObserved = lease.State == SceneLeaseState.AwaitingActivation && !env.LastLoad.isDone && !env.LastLoad.allowSceneActivation;
                    row.peakHandles = env.Provider.RetainedHandleCount;
                    var closing = env.Owner.CloseAsync(Cleanup).Preserve();
                    row.closeWaited = closing.Status == UniTaskStatus.Pending;
                    Require((await closing).IsFullyDrained && lease.State == SceneLeaseState.Released && env.LastLoad.isDone, "deferred.close");
                    Require(row.deferredObserved && row.closeWaited, "deferred.ownership");
                }
                else if (name == Names[1])
                {
                    var old = await env.Flow.EnterAsync("old", null, Wait); Require(old.IsSuccess, "flow.old");
                    using (var cancel = new CancellationTokenSource())
                    {
                        // Deterministic observer cancellation after the real native load starts.
                        env.OnLoadStart = () => cancel.Cancel(); row.injectedCancelAtLoadStart = true;
                        var result = await env.Flow.EnterAsync("abandoned", Key(), OperationContext.Unlimited(cancel.Token));
                        row.canceledObserver = !result.IsSuccess && result.Error.Kind == ErrorKind.Canceled;
                        row.observedError = result.IsSuccess ? "" : result.Error.Code;
                        row.peakHandles = env.Provider.RetainedHandleCount;
                        Require(row.canceledObserver && row.peakHandles == 1, "cancel.retains.native");
                        while (env.Flow.State == SessionFlowState.Transitioning) await UniTask.Yield();
                        row.previousSessionPreserved = ReferenceEquals(old.Value, env.Flow.Current) && old.Value.Scope.State == ScopeState.Open;
                        Require(row.previousSessionPreserved && env.Flow.State == SessionFlowState.Ready && env.LastLoad.isDone && env.Provider.RetainedHandleCount == 0, "cancel.drain");
                    }
                }
                else if (name == Names[2])
                {
                    var old = await env.Flow.EnterAsync("old", Key(), Wait); Require(old.IsSuccess, "flow.first");
                    env.RejectUnload = true; row.injectedUnloadRejection = true;
                    var next = await env.Flow.EnterAsync("new", Key("B"), Wait);
                    row.observedError = next.IsSuccess ? "" : next.Error.Code;
                    row.peakHandles = env.Provider.RetainedHandleCount;
                    row.newSessionPreserved = env.Flow.Current?.Id == "new" && SceneManager.GetActiveScene().path == PathFor("B");
                    row.retryRequired = !next.IsSuccess && next.Error.Kind == ErrorKind.CleanupFailed && env.Flow.State == SessionFlowState.Recovering &&
                        env.Scenes.NeedsRecovery && row.peakHandles == 2 && SceneManager.GetSceneByPath(PathFor("A")).isLoaded && !(await env.Flow.EnterAsync("blocked", null, Wait)).IsSuccess;
                    Require(row.newSessionPreserved && row.retryRequired, "retry.retains.both");
                    env.RejectUnload = false;
                    row.retrySucceeded = (await env.Flow.RetryRecoveryAsync(Cleanup)).IsSuccess && env.Flow.State == SessionFlowState.Ready &&
                        !env.Scenes.NeedsRecovery && env.Provider.RetainedHandleCount == 1 && !SceneManager.GetSceneByPath(PathFor("A")).isLoaded && env.Flow.Current.Id == "new";
                    Require(row.retrySucceeded, "retry.drains.old");
                }
                else
                {
                    var first = await env.Load(); var firstScene = SceneManager.GetActiveScene();
                    var second = await env.Load(); var secondScene = SceneManager.GetActiveScene();
                    row.peakHandles = env.Provider.RetainedHandleCount;
                    row.distinctHandles = firstScene.handle != secondScene.handle && row.peakHandles == 2;
                    Require((await first.UnloadAsync(Cleanup)).IsSuccess, "instance.first.unload");
                    row.secondInstanceProtected = secondScene.IsValid() && secondScene.isLoaded && second.State == SceneLeaseState.Active && env.Provider.RetainedHandleCount == 1;
                    Require(row.distinctHandles && row.secondInstanceProtected, "instance.isolation");
                }
            }
            catch (Exception e) { row.error = e.Message; }
            finally
            {
                try { await env.Finish(); } catch (Exception e) { row.error += ";cleanup:" + e.Message; }
                row.bootstrapPreserved = bootstrap.IsValid() && bootstrap.isLoaded;
                if (row.bootstrapPreserved) SceneManager.SetActiveScene(bootstrap);
                row.nativeLoads = env.Provider.LoadStartCount; row.unloadAttempts = env.UnloadAttempts; row.successfulUnloads = env.SuccessfulUnloads;
                row.nativeActivations = SceneRecoveryWitness.Enabled - enabled;
                row.modules = env.Context.RetainedModuleCount; row.sceneLeases = env.Scenes.RetainedSceneCount;
                row.providerHandles = env.Provider.RetainedHandleCount; row.flowScopes = env.Flow.ReadDiagnostics().ActiveScopes; row.fixtureScenes = FixtureScenes();
                row.passed = row.error == "" && row.bootstrapPreserved && row.modules == 0 && row.sceneLeases == 0 && row.providerHandles == 0 && row.flowScopes == 0 && row.fixtureScenes == 0;
            }
            return row;
        }
    }
}
