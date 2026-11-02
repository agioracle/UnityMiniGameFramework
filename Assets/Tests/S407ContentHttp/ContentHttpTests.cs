using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Network;
using MiniFramework.Network.Unity;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniFramework.S407ContentHttp.Tests
{
    // Conformance fixture only: these bytes are NOT AssetBundles, a production catalog or a CDN provider.
    public sealed class ContentHttpTests
    {
        const string Schema = "s407-fixture-v1";
        static readonly string[] Names = { "boot", "config" };
        static readonly byte[][] Bytes = { Encoding.UTF8.GetBytes("s407 boot payload v1"), Encoding.UTF8.GetBytes("{\"level\":1,\"reward\":5}") };
        static OperationContext Wait => new OperationContext(default, TimeSpan.FromSeconds(8));
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        readonly List<Fixture> fixtures = new List<Fixture>();
        string baseUrl, gateRoot;
        [Serializable] sealed class Info { public string baseUrl, gateRoot; }
        [Serializable] sealed class Catalog { public string package, version, configVersion, schema, program; public Entry[] files; }
        [Serializable] sealed class Entry { public string name, sha256; }
        [Serializable] sealed class Evidence
        {
            public string test, prefix, packageState, selectedSource;
            public int consumerStarts, modules, sessions, pending, exchanges, running, cleanupBeforeTerminal, retainedTransports;
            public string[] order;
        }
        static string Digest(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static void Check(Result result) => Assert.IsTrue(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code);
        static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Integrity) => new FrameworkError(kind, code);
        [SetUp] public void Setup()
        {
            var info = JsonUtility.FromJson<Info>(File.ReadAllText("Library/S407ContentServer.json")); baseUrl = info.baseUrl; gateRoot = info.gateRoot;
            var uri = new Uri(baseUrl); Assert.AreEqual("127.0.0.1", uri.Host); Assert.AreEqual("http", uri.Scheme);
            Assert.IsTrue(Directory.Exists(gateRoot));
        }

        sealed class Backend : IPackageBackend
        {
            readonly Fixture owner;
            public readonly List<Session> Sessions = new List<Session>();
            public readonly List<string> Order = new List<string>();
            public bool FailCleanup;
            public int OpenCount => Sessions.Count(s => !s.Closed);
            public Backend(Fixture owner) { this.owner = owner; }
            public IPackageSession Create(PackageProfile profile, PackageCandidate candidate)
            { var session = new Session(this, owner, candidate, Sessions.Count); Sessions.Add(session); return session; }
        }
        // Observe the real Unity transport without replacing its result or cancellation behavior.
        sealed class ObservedTransport : IHttpTransport
        {
            readonly UnityHttpTransport inner = new UnityHttpTransport();
            public readonly List<Exchange> Exchanges = new List<Exchange>();
            public int EarlyCleanup;
            public int Retained => Exchanges.Count(e => !e.Released);
            public IHttpExchange Open(HttpRequestSpec request, int maxResponseBytes)
            { var exchange = new Exchange(this, inner.Open(request, maxResponseBytes)); Exchanges.Add(exchange); return exchange; }
            public sealed class Exchange : IHttpExchange
            {
                readonly ObservedTransport owner; readonly IHttpExchange inner;
                bool started, ended;
                public bool Released;
                public Exchange(ObservedTransport owner, IHttpExchange inner) { this.owner = owner; this.inner = inner; }
                public async UniTask<HttpTransportResult> SendAsync()
                { started = true; try { return await inner.SendAsync(); } finally { ended = true; } }
                public Result Abort() => inner.Abort();
                public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
                {
                    if (started && !ended) { owner.EarlyCleanup++; return Result.Failure(Error("fixture.cleanup.before_terminal", ErrorKind.CleanupFailed)); }
                    var result = await inner.CleanupAsync(cleanup); if (result.IsSuccess) Released = true; return result;
                }
            }
        }
        sealed class Session : IPackageSession
        {
            readonly Backend backend; readonly Fixture owner; readonly PackageCandidate candidate;
            public readonly string Stage;
            public bool Closed;
            public int Cleans;
            public Session(Backend backend, Fixture owner, PackageCandidate candidate, int index)
            { this.backend = backend; this.owner = owner; this.candidate = candidate; Stage = Path.Combine(owner.Root, "stage-" + index); }
            async UniTask<Result<byte[]>> Fetch(string resource)
            {
                var captured = owner.Http.Capture(new HttpRequestSpec(HttpVerb.Get, owner.BaseUrl + owner.Prefix + "/" + resource));
                if (!captured.IsSuccess) return Result<byte[]>.Failure(captured.Error);
                var response = await captured.Value.WaitAsync(Wait);
                return response.IsSuccess ? Result<byte[]>.Success(response.Value.CopyBody()) : Result<byte[]>.Failure(response.Error);
            }
            public async UniTask<Result<PackageManifest>> PrepareAsync()
            {
                backend.Order.Add("prepare:" + candidate.Source);
                if (candidate.Source == PackageSource.CompatibleCache)
                {
                    for (int i = 0; i < Names.Length; i++)
                    {
                        var path = Path.Combine(owner.Cache, Names[i]);
                        if (!File.Exists(path) || new FileInfo(path).Length > 1024 || Digest(File.ReadAllBytes(path)) != Digest(Bytes[i]))
                            return Result<PackageManifest>.Failure(Error("fixture.cache.integrity"));
                    }
                    return Result<PackageManifest>.Success(Good());
                }
                var fetched = await Fetch("manifest"); if (!fetched.IsSuccess) return Result<PackageManifest>.Failure(fetched.Error);
                Catalog catalog;
                try { catalog = JsonUtility.FromJson<Catalog>(Encoding.UTF8.GetString(fetched.Value)); }
                catch (Exception) { return Result<PackageManifest>.Failure(Error("fixture.catalog.invalid")); }
                if (catalog?.files == null || catalog.files.Length != Names.Length || catalog.files.Any(f => f == null) ||
                    !catalog.files.Select(f => f.name).SequenceEqual(Names)) return Result<PackageManifest>.Failure(Error("fixture.catalog.members"));
                for (int i = 0; i < Names.Length; i++)
                {
                    if (catalog.files[i].sha256 != Digest(Bytes[i])) return Result<PackageManifest>.Failure(Error("fixture.catalog.digest"));
                    var payload = await Fetch(Names[i]); if (!payload.IsSuccess) return Result<PackageManifest>.Failure(payload.Error);
                    if (Digest(payload.Value) != catalog.files[i].sha256) return Result<PackageManifest>.Failure(Error("fixture.payload.integrity"));
                    Directory.CreateDirectory(Stage); File.WriteAllBytes(Path.Combine(Stage, Names[i]), payload.Value);
                }
                return Result<PackageManifest>.Success(new PackageManifest(catalog.package, catalog.version, catalog.configVersion,
                    catalog.schema, true, new[] { catalog.program }, Names));
            }
            public UniTask<Result> CleanupAsync()
            {
                Cleans++; backend.Order.Add("cleanup:" + candidate.Source);
                if (backend.FailCleanup) return UniTask.FromResult(Result.Failure(Error("fixture.cleanup", ErrorKind.CleanupFailed)));
                if (Directory.Exists(Stage)) Directory.Delete(Stage, true);
                Closed = true; return UniTask.FromResult(Result.Success());
            }
        }
        sealed class Consumer : IModule
        {
            readonly PackageStartup package;
            public int Starts;
            public string Id => "content-consumer";
            public IReadOnlyList<string> Dependencies => new[] { "package" };
            public Consumer(PackageStartup package) { this.package = package; }
            public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext wait)
            {
                var ready = package.CheckReady("main", "v1"); if (ready.IsSuccess) Starts++;
                return UniTask.FromResult(ready);
            }
            public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        }
        sealed class Fixture
        {
            public readonly string Root, Cache, Prefix, BaseUrl, Name, Gate;
            public readonly HttpClientService Http;
            public readonly ObservedTransport Transport = new ObservedTransport();
            public readonly Backend Backend;
            public readonly PackageStartup Package;
            public readonly Consumer Consumer;
            public readonly GameContext Context;
            public Fixture(string baseUrl, string gateRoot, string name, string mode, bool cache)
            {
                Name = name; BaseUrl = baseUrl; var caseId = Guid.NewGuid().ToString("N"); Prefix = "/content/" + name + "/" + caseId + "/" + mode;
                Gate = Path.Combine(gateRoot, caseId);
                Root = Path.Combine(Path.GetTempPath(), "mini-s407-content-" + Guid.NewGuid().ToString("N"));
                Cache = Path.Combine(Root, "cache"); Directory.CreateDirectory(Root);
                Http = new HttpClientService(Transport, new HttpClientLimits(maxResponseBytes: 1024), new HttpRetryPolicy(3, TimeSpan.FromMilliseconds(10)));
                Backend = new Backend(this);
                var candidates = new List<PackageCandidate> { new PackageCandidate(PackageSource.Remote, "v1", Prefix) };
                if (cache) candidates.Add(new PackageCandidate(PackageSource.CompatibleCache, "v1", Cache));
                Package = new PackageStartup(new PackageProfile("main", "app", Schema, Names, candidates), Backend, backendModuleId: Http.Id);
                Consumer = new Consumer(Package);
                Context = new GameContext(new[] { Reg(Http, typeof(IHttpClient)), Reg(Package, typeof(IPackageReadiness)), Reg(Consumer) }, Cleanup);
            }
            static ModuleRegistration Reg(IModule module, params Type[] services) => new ModuleRegistration(module.Id, module.Dependencies, () => module, services);
            public void SeedCache() { Directory.CreateDirectory(Cache); for (int i = 0; i < Names.Length; i++) File.WriteAllBytes(Path.Combine(Cache, Names[i]), Bytes[i]); }
            public async UniTask<int> Count(string resource)
            {
                var op = Http.Capture(new HttpRequestSpec(HttpVerb.Get, BaseUrl + "/__count?path=" + Uri.EscapeDataString(Prefix + "/" + resource)));
                Assert.IsTrue(op.IsSuccess); var result = await op.Value.WaitAsync(Wait); Assert.IsTrue(result.IsSuccess); return int.Parse(Encoding.UTF8.GetString(result.Value.CopyBody()));
            }
            public async UniTask AcceptedManifest()
            {
                for (int i = 0; i < 600; i++) { if (File.Exists(Gate + ".started")) return; await UniTask.Yield(); }
                Assert.Fail("Controlled server did not observe manifest request.");
            }
            public void ReleaseManifest() { File.WriteAllText(Gate + ".release", "release"); }
            public async UniTask Finish()
            {
                ReleaseManifest(); Backend.FailCleanup = false; var closed = await Context.CloseAsync(Wait);
                if (!closed.IsSuccess) closed = await Context.RetryCleanupAsync(Cleanup, Wait);
                Check(closed); Assert.AreEqual(0, Context.RetainedModuleCount); Assert.AreEqual(0, Backend.OpenCount);
                Assert.AreEqual(0, Http.PendingCount); Assert.AreEqual(0, Http.ActiveExchangeCount); Assert.AreEqual(0, Http.RunningRequestCount);
                Assert.AreEqual(0, Transport.EarlyCleanup); Assert.AreEqual(0, Transport.Retained);
                Assert.IsFalse(Directory.EnumerateDirectories(Root, "stage-*").Any());
            }
        }
        static PackageManifest Good() => new PackageManifest("main", "v1", "v1", Schema, true, new[] { "app" }, Names);
        Fixture New(string mode = "good", bool cache = false)
        { var e = new Fixture(baseUrl, gateRoot, TestContext.CurrentContext.Test.MethodName, mode, cache); fixtures.Add(e); return e; }
        static async UniTask Frames() { for (int i = 0; i < 5; i++) await UniTask.Yield(); }
        static void NotReady(Fixture e)
        { Assert.AreEqual(0, e.Consumer.Starts); Assert.IsFalse(e.Package.CheckReady("main", "v1").IsSuccess); Assert.IsNull(e.Package.ContentVersion); }
        [UnityTearDown] public IEnumerator Teardown() => UniTask.ToCoroutine(async () => {
            foreach (var e in fixtures)
            {
                await e.Finish(); Directory.CreateDirectory("Library/S407ContentEvidence");
                File.WriteAllText("Library/S407ContentEvidence/" + Guid.NewGuid().ToString("N") + ".json", JsonUtility.ToJson(new Evidence {
                    test = e.Name, prefix = e.Prefix, packageState = e.Package.State.ToString(), selectedSource = e.Package.SelectedSource?.ToString() ?? "",
                    consumerStarts = e.Consumer.Starts, modules = e.Context.RetainedModuleCount, sessions = e.Backend.OpenCount,
                    pending = e.Http.PendingCount, exchanges = e.Http.ActiveExchangeCount, running = e.Http.RunningRequestCount,
                    cleanupBeforeTerminal = e.Transport.EarlyCleanup, retainedTransports = e.Transport.Retained, order = e.Backend.Order.ToArray()
                }));
                Directory.Delete(e.Root, true);
            }
            fixtures.Clear();
        });

        [UnityTest] public IEnumerator TransientManifestRetriesBeforeConsumerStarts() => UniTask.ToCoroutine(async () => {
            var e = New("transient"); Check(await e.Context.StartAsync(Wait)); Assert.AreEqual(1, e.Consumer.Starts);
            Assert.AreEqual(3, await e.Count("manifest")); Assert.AreEqual(1, await e.Count("boot")); Assert.AreEqual(1, await e.Count("config"));
            Assert.AreEqual(PackageSource.Remote, e.Package.SelectedSource); Assert.IsTrue(e.Package.CheckReady("main", "v1").IsSuccess);
        });
        [UnityTest] public IEnumerator PartialRemoteCleansBeforeVerifiedCacheFallback() => UniTask.ToCoroutine(async () => {
            var e = New("missing", true); e.SeedCache(); Check(await e.Context.StartAsync(Wait));
            Assert.AreEqual(PackageSource.CompatibleCache, e.Package.SelectedSource); Assert.AreEqual(1, e.Consumer.Starts);
            CollectionAssert.AreEqual(new[] { "prepare:Remote", "cleanup:Remote", "prepare:CompatibleCache" }, e.Backend.Order);
            Assert.IsFalse(Directory.Exists(e.Backend.Sessions[0].Stage)); CollectionAssert.AreEqual(Bytes[1], File.ReadAllBytes(Path.Combine(e.Cache, "config")));
        });
        [UnityTest] public IEnumerator FirstOfflineWithoutCacheNeverStartsConsumer() => UniTask.ToCoroutine(async () => {
            var e = New("offline"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("network.http.503", e.Package.LastResult.Error.Code); Assert.AreEqual(1, e.Backend.Sessions.Count);
        });
        [UnityTest] public IEnumerator CorruptCacheIsPreservedAndNeverBecomesReady() => UniTask.ToCoroutine(async () => {
            var e = New("permission", true); e.SeedCache(); File.WriteAllBytes(Path.Combine(e.Cache, "config"), new byte[] { 7 });
            Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("fixture.cache.integrity", e.Package.LastResult.Error.Code); CollectionAssert.AreEqual(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(e.Cache, "config")));
        });
        [UnityTest] public IEnumerator MismatchedConfigurationVersionNeverPublishesReady() => UniTask.ToCoroutine(async () => {
            var e = New("wrong-config"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("package.manifest.compatibility", e.Package.LastResult.Error.Code);
        });
        [UnityTest] public IEnumerator CorruptRemotePayloadDoesNotPublishPartialContent() => UniTask.ToCoroutine(async () => {
            var e = New("corrupt"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("fixture.payload.integrity", e.Package.LastResult.Error.Code); Assert.IsFalse(e.Package.RetainsSession);
        });
        [UnityTest] public IEnumerator OversizedPayloadIsRejectedWithoutRetryOrConsumer() => UniTask.ToCoroutine(async () => {
            var e = New("oversize"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("network.response.limit", e.Package.LastResult.Error.Code);
        });
        [UnityTest] public IEnumerator RedirectManifestIsNotFollowedOrRetried() => UniTask.ToCoroutine(async () => {
            var e = New("redirect"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreEqual("network.http.307", e.Package.LastResult.Error.Code);
        });
        [UnityTest] public IEnumerator TruncatedSuccessfulStatusCannotPublishPackage() => UniTask.ToCoroutine(async () => {
            var e = New("truncated"); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.AreNotEqual(PackageState.Ready, e.Package.State);
        });
        [UnityTest] public IEnumerator CanceledStartupDoesNotPublishLatePackage() => UniTask.ToCoroutine(async () => {
            var e = New("held"); using (var cancel = new CancellationTokenSource()) {
                var start = e.Context.StartAsync(OperationContext.Unlimited(cancel.Token)).Preserve();
                await e.AcceptedManifest();
                cancel.Cancel(); Assert.AreEqual(ErrorKind.Canceled, (await start).Error.Kind);
                await e.Finish(); NotReady(e);
            }
        });
        [UnityTest] public IEnumerator CloseDuringPrepareAbortsAndDrainsBeforeReleasingNetwork() => UniTask.ToCoroutine(async () => {
            var e = New("held"); var start = e.Context.StartAsync(Wait).Preserve();
            await e.AcceptedManifest();
            var close = await e.Context.CloseAsync(new OperationContext(default, TimeSpan.FromMilliseconds(20)));
            if (!close.IsSuccess) { Assert.AreEqual(ErrorKind.Timeout, close.Error.Kind); Assert.Greater(e.Context.RetainedModuleCount, 0); }
            else { Assert.AreEqual(0, e.Http.RunningRequestCount); Assert.AreEqual(0, e.Transport.Retained); }
            await e.Finish(); Assert.IsFalse((await start).IsSuccess); NotReady(e); Assert.AreEqual(0, e.Transport.EarlyCleanup);
        });
        [UnityTest] public IEnumerator CleanupFailureBlocksFallbackUntilExplicitRetry() => UniTask.ToCoroutine(async () => {
            var e = New("missing", true); e.SeedCache(); e.Backend.FailCleanup = true;
            Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); NotReady(e);
            Assert.Greater(e.Context.RetainedModuleCount, 0); Assert.AreEqual(1, e.Backend.Sessions.Count); Assert.IsTrue(Directory.Exists(e.Backend.Sessions[0].Stage));
            int cleans = e.Backend.Sessions[0].Cleans; Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess); Assert.AreEqual(cleans, e.Backend.Sessions[0].Cleans);
            e.Backend.FailCleanup = false; Check(await e.Context.RetryCleanupAsync(Cleanup, Wait)); Assert.AreEqual(1, e.Backend.Sessions.Count);
            CollectionAssert.AreEqual(Bytes[0], File.ReadAllBytes(Path.Combine(e.Cache, "boot")));
        });
        [UnityTest] public IEnumerator ReadyVersionIsFrozenWithoutImplicitRefresh() => UniTask.ToCoroutine(async () => {
            var e = New(); Check(await e.Context.StartAsync(Wait));
            Assert.AreEqual("package.version.frozen", e.Package.CheckReady("main", "v2").Error.Code);
            Assert.IsFalse((await e.Package.RetryAsync(Wait)).IsSuccess); await Frames();
            Assert.AreEqual(1, await e.Count("manifest")); Assert.AreEqual("v1", e.Package.ContentVersion); Assert.AreEqual(1, e.Consumer.Starts);
        });
    }
}
