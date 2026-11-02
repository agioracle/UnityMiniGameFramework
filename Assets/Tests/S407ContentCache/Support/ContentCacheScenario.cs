using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Network;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Runtime;
using UnityEngine;
using UnityEngine.Scripting;
namespace MiniFramework.S407ContentCache
{
    public interface IContentCacheFiles
    {
        Result<StorageReadResult> Read(string id, int maxBytes);
        Result WriteOwnedCopy(string id, byte[] bytes);
        Result DeleteOwnedCopy(string id);
    }
    public sealed class ContentDescriptor
    {
        public readonly string Url, Sha; public readonly int Bytes;
        public ContentDescriptor(string url, int bytes, string sha) { Url = url; Bytes = bytes; Sha = sha; }
    }
    [Serializable, Preserve] public sealed class CacheCheckpoint
    { [Preserve] public int schemaVersion = 1; [Preserve] public string buildId, runId, launchId, digest; }
    [Serializable, Preserve] public sealed class ContentCase
    {
        [Preserve] public string name, state, source = "", error = "", diskSha = "";
        [Preserve] public bool passed, readyBeforePrepare, consumerVerified, sourceReleasedBeforeCache;
        [Preserve] public int consumers, actualDownloads, injectedNetworkFailures, modules, sessions, pending, exchanges, running;
        [Preserve] public long reservedResponseBytes;
        [Preserve] public List<string> order = new List<string>();
    }
    [Serializable, Preserve] public sealed class ContentCacheEvidence
    {
        [Preserve] public string phase = "created", error = "", previousRunId = "", previousLaunchId = "", previousDigest = "", digest = "";
        [Preserve] public bool passed, crossRuntimeCacheRestored, originalCachePreserved, corruptCopyRemoved;
        [Preserve] public List<ContentCase> cases = new List<ContentCase>();
    }
    // Opaque-byte catalog conformance fixture. Not a production CDN/AssetBundle/model provider.
    public sealed class ContentCacheScenario
    {
        public const string Build = "s407-content-cache-csharp-v1-20261031", Marker = "content-cache-checkpoint-v1", Content = "content-v1", Missing = "missing-v1", Corrupt = "corrupt-v1";
        const string Package = "content-fixture", Version = "v1", Schema = "opaque-fixture-v1", Program = "fixture-app-v1";
        readonly IContentCacheFiles files; readonly IWeChatStorageFiles markers; readonly IHttpTransport buffered; readonly IHttpDownloadTransport download; readonly ContentDescriptor descriptor; readonly string runId, launchId;
        public readonly ContentCacheEvidence Evidence = new ContentCacheEvidence();
        static OperationContext Wait => new OperationContext(default, TimeSpan.FromSeconds(90));
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        public ContentCacheScenario(IContentCacheFiles files, IWeChatStorageFiles markers, IHttpTransport buffered, IHttpDownloadTransport download, ContentDescriptor descriptor, string runId, string launchId)
        { this.files = files; this.markers = markers; this.buffered = buffered; this.download = download; this.descriptor = descriptor; this.runId = runId; this.launchId = launchId; }
        public static string Sha(byte[] bytes) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static void Require(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
        static T Value<T>(Result<T> r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); return r.Value; }
        static void Check(Result r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); }
        Result Verify(string id)
        {
            var read = files.Read(id, descriptor.Bytes); if (!read.IsSuccess) return Result.Failure(read.Error);
            if (!read.Value.Exists) return Result.Failure(new FrameworkError(ErrorKind.Unavailable, "fixture.cache.missing"));
            var bytes = read.Value.CopyBytes();
            return bytes.Length == descriptor.Bytes && Sha(bytes) == descriptor.Sha ? Result.Success() : Result.Failure(new FrameworkError(ErrorKind.Integrity, "fixture.cache.integrity"));
        }
        sealed class ObservedDownload : IHttpDownloadTransport
        {
            readonly IHttpDownloadTransport inner; readonly ContentCase row; readonly bool denied;
            public ObservedDownload(IHttpDownloadTransport inner, ContentCase row, bool denied) { this.inner = inner; this.row = row; this.denied = denied; }
            public IHttpDownloadExchange OpenDownload(HttpDownloadRequest request) => denied ? new Denied(row) : new Observed(inner.OpenDownload(request), row);
            sealed class Denied : IHttpDownloadExchange
            {
                readonly ContentCase row; public Denied(ContentCase row) { this.row = row; }
                public UniTask<HttpTransportResult> SendAsync() { row.injectedNetworkFailures++; return UniTask.FromResult(HttpTransportResult.Failed(new FrameworkError(ErrorKind.Network, "fixture.network.unavailable"))); }
                public Result Publish() => Result.Failure(new FrameworkError(ErrorKind.InvalidState, "fixture.denied.publish"));
                public Result Abort() => Result.Success();
                public UniTask<Result> CleanupAsync(CleanupContext c) => UniTask.FromResult(Result.Success());
            }
            sealed class Observed : IHttpDownloadExchange
            {
                readonly IHttpDownloadExchange inner; readonly ContentCase row;
                public Observed(IHttpDownloadExchange inner, ContentCase row) { this.inner = inner; this.row = row; }
                public UniTask<HttpTransportResult> SendAsync() { row.actualDownloads++; return inner.SendAsync(); }
                public Result Publish() => inner.Publish(); public Result Abort() => inner.Abort();
                public UniTask<Result> CleanupAsync(CleanupContext c) => inner.CleanupAsync(c);
            }
        }
        sealed class Backend : IPackageBackend
        {
            readonly ContentCacheScenario owner; readonly HttpClientService http; readonly ContentCase row; readonly string cacheId, configVersion;
            public int Open; bool remoteReleased;
            public Backend(ContentCacheScenario owner, HttpClientService http, ContentCase row, string cacheId, string configVersion)
            { this.owner = owner; this.http = http; this.row = row; this.cacheId = cacheId; this.configVersion = configVersion; }
            public IPackageSession Create(PackageProfile p, PackageCandidate c) { Open++; return new Session(this, c); }
            sealed class Session : IPackageSession
            {
                readonly Backend b; readonly PackageCandidate c; bool closed;
                public Session(Backend b, PackageCandidate c) { this.b = b; this.c = c; }
                public async UniTask<Result<PackageManifest>> PrepareAsync()
                {
                    b.row.order.Add("prepare:" + c.Source);
                    if (c.Source == PackageSource.Remote)
                    {
                        var d = b.owner.descriptor;
                        var captured = b.http.CaptureDownload(new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get, d.Url), Content, d.Bytes, d.Sha));
                        if (!captured.IsSuccess) return Result<PackageManifest>.Failure(captured.Error);
                        var result = await captured.Value.WaitAsync(Wait);
                        if (!result.IsSuccess) return Result<PackageManifest>.Failure(result.Error);
                        Require(result.Value.BodyLength == 0 && result.Value.Download != null && result.Value.Download.Bytes == d.Bytes && result.Value.Download.Sha256 == d.Sha, "download.receipt");
                    }
                    else { b.row.sourceReleasedBeforeCache = b.remoteReleased; Require(b.remoteReleased, "cache.before_remote_cleanup"); }
                    var verified = b.owner.Verify(c.Source == PackageSource.Remote ? Content : b.cacheId);
                    if (!verified.IsSuccess) return Result<PackageManifest>.Failure(verified.Error);
                    b.row.diskSha = b.owner.descriptor.Sha; b.row.order.Add("verified:" + c.Source);
                    return Result<PackageManifest>.Success(new PackageManifest(Package, Version, b.configVersion, Schema, true, new[] { Program }, new[] { "opaque" }));
                }
                public UniTask<Result> CleanupAsync()
                {
                    if (!closed) { closed = true; b.Open--; b.row.order.Add("cleanup:" + c.Source); if (c.Source == PackageSource.Remote) b.remoteReleased = true; }
                    return UniTask.FromResult(Result.Success());
                }
            }
        }
        sealed class Consumer : IModule
        {
            readonly PackageStartup package; readonly ContentCacheScenario owner; readonly ContentCase row; readonly string cacheId;
            public Consumer(PackageStartup package, ContentCacheScenario owner, ContentCase row, string cacheId) { this.package = package; this.owner = owner; this.row = row; this.cacheId = cacheId; }
            public string Id => "content-consumer"; public IReadOnlyList<string> Dependencies => new[] { "package" };
            public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext wait)
            {
                var r = package.CheckReady(Package, Version); if (!r.IsSuccess) return UniTask.FromResult(r);
                r = owner.Verify(package.SelectedSource == PackageSource.Remote ? Content : cacheId);
                if (r.IsSuccess) { row.consumerVerified = true; row.consumers++; row.order.Add("consumer"); }
                return UniTask.FromResult(r);
            }
            public UniTask<Result> StopAsync(CleanupContext c) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext c) => UniTask.FromResult(Result.Success());
        }
        async UniTask Case(string name, bool denied, bool cache, string cacheId = Content, string configVersion = Version, string expectedError = "")
        {
            var row = new ContentCase { name = name }; Evidence.cases.Add(row);
            var http = new HttpClientService(buffered, retry: new HttpRetryPolicy(1), downloadTransport: new ObservedDownload(download, row, denied));
            var backend = new Backend(this, http, row, cacheId, configVersion);
            var candidates = new List<PackageCandidate> { new PackageCandidate(PackageSource.Remote, Version, descriptor.Url) };
            if (cache) candidates.Add(new PackageCandidate(PackageSource.CompatibleCache, Version, cacheId));
            var package = new PackageStartup(new PackageProfile(Package, Program, Schema, new[] { "opaque" }, candidates), backend, backendModuleId: http.Id);
            var consumer = new Consumer(package, this, row, cacheId);
            row.readyBeforePrepare = package.CheckReady(Package, Version).IsSuccess; Require(!row.readyBeforePrepare, "ready.too_early");
            var context = new GameContext(new[] { Reg(http, typeof(IHttpClient), typeof(IHttpDownloadClient)), Reg(package, typeof(IPackageReadiness)), Reg(consumer) }, Cleanup);
            try
            {
                var r = await context.StartAsync(Wait); row.state = package.State.ToString(); row.source = package.SelectedSource?.ToString() ?? ""; row.error = r.IsSuccess ? "" : r.Error.Code;
                if (expectedError == "") Require(r.IsSuccess && row.consumers == 1 && row.consumerVerified, "case.expected_ready");
                else Require(!r.IsSuccess && row.error == expectedError && row.consumers == 0 && !package.CheckReady(Package, Version).IsSuccess, "case.expected_failure:" + row.error);
                if (r.IsSuccess) Require(!package.CheckReady(Package, "v2").IsSuccess, "version.not_frozen");
            }
            finally
            {
                Check(await context.CloseAsync(Wait)); row.modules = context.RetainedModuleCount; row.sessions = backend.Open; row.pending = http.PendingCount;
                row.exchanges = http.ActiveExchangeCount; row.running = http.RunningRequestCount; row.reservedResponseBytes = http.ReservedResponseBytes;
            }
            Require(row.modules == 0 && row.sessions == 0 && row.pending == 0 && row.exchanges == 0 && row.running == 0 && row.reservedResponseBytes == 0, "close.retained"); row.passed = true;
        }
        static ModuleRegistration Reg(IModule m, params Type[] s) => new ModuleRegistration(m.Id, m.Dependencies, () => m, s);
        public async UniTask Execute()
        {
            var marker = Value(markers.Read(Marker, 16384));
            if (!marker.Exists)
            {
                Require(!Value(files.Read(Content, descriptor.Bytes)).Exists, "cache.unowned_existing");
                await Case("remote_verified_before_consumer", false, false); Check(Verify(Content));
                Evidence.digest = Sha(Value(files.Read(Content, descriptor.Bytes)).CopyBytes());
                var checkpoint = new CacheCheckpoint { buildId = Build, runId = runId, launchId = launchId, digest = Evidence.digest };
                var raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(checkpoint)); Check(markers.Write(Marker, raw));
                Require(Value(markers.Read(Marker, 16384)).CopyBytes().SequenceEqual(raw), "marker.readback"); Evidence.phase = "cache_populated";
            }
            else
            {
                var p = JsonUtility.FromJson<CacheCheckpoint>(new UTF8Encoding(false, true).GetString(marker.CopyBytes()));
                Require(p != null && p.schemaVersion == 1 && p.buildId == Build && p.digest == descriptor.Sha && Guid.TryParseExact(p.runId, "N", out _) && Guid.TryParseExact(p.launchId, "N", out _), "marker.invalid");
                Require(p.runId != runId && p.launchId != launchId, "marker.fresh_runtime_required");
                Evidence.previousRunId = p.runId; Evidence.previousLaunchId = p.launchId; Evidence.previousDigest = p.digest;
                Check(Verify(Content)); Evidence.crossRuntimeCacheRestored = true;
                await Case("cached_after_injected_network_failure", true, true);
                Require(!Value(files.Read(Missing, descriptor.Bytes)).Exists, "missing.fixture_exists");
                await Case("missing_cache_denied", true, true, Missing, expectedError: "fixture.cache.missing");
                var original = Value(files.Read(Content, descriptor.Bytes)).CopyBytes(); var bad = (byte[])original.Clone(); bad[0] ^= 1;
                Require(!Value(files.Read(Corrupt, descriptor.Bytes)).Exists, "corrupt.fixture_exists");
                try { Check(files.WriteOwnedCopy(Corrupt, bad)); await Case("corrupt_cache_denied", true, true, Corrupt, expectedError: "fixture.cache.integrity"); }
                finally { Check(files.DeleteOwnedCopy(Corrupt)); }
                Evidence.corruptCopyRemoved = !Value(files.Read(Corrupt, descriptor.Bytes)).Exists;
                await Case("incompatible_catalog_denied", true, true, configVersion: "v2", expectedError: "package.manifest.compatibility");
                Evidence.digest = Sha(Value(files.Read(Content, descriptor.Bytes)).CopyBytes());
                Evidence.originalCachePreserved = Evidence.digest == p.digest && Evidence.corruptCopyRemoved; Require(Evidence.originalCachePreserved, "original.changed"); Evidence.phase = "cache_verified";
            }
            Evidence.passed = true;
        }
    }
}
