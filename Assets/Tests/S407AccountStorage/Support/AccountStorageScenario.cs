using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Accounts;
using MiniFramework.Contracts;
using MiniFramework.Ownership;
using MiniFramework.Providers.IdentityFake;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using UnityEngine.Scripting;

namespace MiniFramework.S407AccountStorage
{
    [Serializable, Preserve] public sealed class AccountStorageCase
    {
        public string name, error = "", observedError = "", aNamespace = "", bNamespace = "", aDigest = "", bDigest = "";
        public bool passed, namespacesIsolated, oldOwnerRejected, oldScopeRetained, lateWriteKeptA, bUnchanged, returnedARestored, newGeneration;
        public bool injectedWriteStartHold, closeWaited, lateIdentityFenced, simulatedPolicyRejected, fileCleanupPending;
        public int writes, identityAttempts, identityCleanups, modules, fileSessions, storagePending, accountPending, accountScopes;
        public long aGeneration, bGeneration, returnedGeneration, aSequence, bSequence;
    }
    public sealed class AccountStorageScenario
    {
        public static readonly string[] Names = { "late_commit_original_owner", "close_waits_for_retired_write", "canceled_identity_late_result", "default_policy_rejects_simulated" };
        public readonly List<AccountStorageCase> Cases = new List<AccountStorageCase>();
        public static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        static OperationContext Wait => OperationContext.Unlimited();
        readonly IWeChatStorageFiles files;
        public AccountStorageScenario(IWeChatStorageFiles files) { this.files = files; }
        public static void Require(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
        static void Check(Result value) => Require(value.IsSuccess, value.IsSuccess ? "" : value.Error.Code);
        static T Value<T>(Result<T> value) { Require(value.IsSuccess, value.IsSuccess ? "" : value.Error.Code); return value.Value; }
        static ModuleRegistration Reg(IModule module, params Type[] services) => new ModuleRegistration(module.Id, module.Dependencies, () => module, services);

        sealed class CountedFiles : IWeChatStorageFiles, IWeChatStorageCleanup
        {
            readonly IWeChatStorageFiles actual; public int Writes;
            public CountedFiles(IWeChatStorageFiles actual) { this.actual = actual; }
            public StoragePersistence Persistence => actual.Persistence;
            public bool CleanupPending => actual is IWeChatStorageCleanup cleanup && cleanup.CleanupPending;
            public Result RetryCleanup() => actual is IWeChatStorageCleanup cleanup ? cleanup.RetryCleanup() : Result.Success();
            public Result<StorageReadResult> Read(string key, int max) => actual.Read(key, max);
            public Result Write(string key, byte[] bytes) { var result = actual.Write(key, bytes); if (result.IsSuccess) Writes++; return result; }
        }
        // Delays the start of one actual write; it never manufactures a receipt or changes its address.
        sealed class HeldProvider : IStorageProvider
        {
            public readonly WeChatStorageProvider Actual;
            public readonly UniTaskCompletionSource Entered = new UniTaskCompletionSource(), Release = new UniTaskCompletionSource();
            public string HeldNamespace; public bool Armed; public Func<bool> Retired;
            public bool ActualWriteAfterRetirement;
            public HeldProvider(WeChatStorageProvider actual) { Actual = actual; }
            public string Id => Actual.Id;
            public StorageCapabilities Capabilities => Actual.Capabilities;
            public IStorageSession Create(StorageAddress address) => new Session(this, address, Actual.Create(address));
            sealed class Session : IStorageSession
            {
                readonly HeldProvider owner; readonly StorageAddress address; readonly IStorageSession actual;
                public Session(HeldProvider owner, StorageAddress address, IStorageSession actual) { this.owner = owner; this.address = address; this.actual = actual; }
                public UniTask<Result<StorageReadResult>> ReadAsync() => actual.ReadAsync();
                public async UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request)
                {
                    Require(request.Address.Namespace == address.Namespace && request.Address.Slot == address.Slot, "fixture.write.address");
                    if (owner.Armed && address.Namespace == owner.HeldNamespace)
                    {
                        owner.Armed = false; owner.Entered.TrySetResult(); await owner.Release.Task;
                        owner.ActualWriteAfterRetirement = owner.Retired != null && owner.Retired();
                    }
                    return await actual.CommitAsync(request);
                }
                public UniTask<Result> CleanupAsync(CleanupContext cleanup) => actual.CleanupAsync(cleanup);
            }
        }
        sealed class ContextCleanup : IAsyncCleanup
        {
            readonly GameContext actual; public string Id { get; }
            public ContextCleanup(GameContext actual, string id) { this.actual = actual; Id = id; }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup) => actual.CloseAsync(Wait);
        }
        sealed class Box
        {
            public readonly StorageAddress Address; public readonly StorageService Storage; public readonly GameContext Context;
            public byte Data;
            public Box(HeldProvider provider, StorageAddress address)
            {
                Address = address; var registry = new StorageRegistry();
                Check(registry.Register(new StorageModuleRegistration("progress", 1, "bytes", true, typeof(byte),
                    () => Result<byte[]>.Success(new[] { Data }), bytes => bytes != null && bytes.Length == 1 ? Result.Success() : Result.Failure(new FrameworkError(ErrorKind.Integrity, "fixture.payload")))));
                Storage = new StorageService(provider, address, registry, recovery: new StorageRecovery());
                Context = new GameContext(new[] { Reg(Storage, typeof(IStorageService), typeof(IStorageRecoveryService)) }, Cleanup);
            }
            public async UniTask Write(byte value)
            { Data = value; var receipt = await Value(Storage.Capture()).WaitAsync(Wait); Require(receipt.IsSuccess && receipt.Value.Disposition == StorageCommitDisposition.Committed, "fixture.commit"); }
            public byte Restored => Value(Storage.Restored.Read("progress"))[0];
        }
        sealed class Env
        {
            public readonly CountedFiles Files; public readonly HeldProvider Provider; public readonly FakeIdentityProvider Identity = new FakeIdentityProvider();
            public readonly AccountService Account; public readonly GameContext Context; public readonly List<Box> Boxes = new List<Box>();
            public Env(IWeChatStorageFiles files, bool simulated)
            {
                Files = new CountedFiles(files); Provider = new HeldProvider(new WeChatStorageProvider(Files, new WeChatStorageOptions(1048576)));
                Account = new AccountService(OwnershipPolicy.AccountScoped(simulated), new OwnerGenerationSequence(), Identity);
                Context = new GameContext(new[] { Reg(Account, typeof(IAccountService), typeof(IClaimOwnerAuthority)) }, Cleanup);
            }
            public async UniTask<IAccountSession> Login(string owner)
            { Identity.NextOwnerId = owner; return Value(await Value(Account.CaptureSwitch()).WaitAsync(Wait)); }
            public async UniTask<Box> Open(IAccountSession owner, string slot)
            {
                Require(owner.IsAccepting, "fixture.open.owner");
                var address = Value(Account.AddressForCurrent(slot)); Require(address.Namespace == owner.Owner.StorageNamespace, "fixture.open.address");
                var box = new Box(Provider, address); Boxes.Add(box);
                Value(owner.Scope.Cleanup.Register(new ContextCleanup(box.Context, "storage-" + Boxes.Count)));
                Check(await box.Context.StartAsync(Wait)); return box;
            }
            public async UniTask Finish()
            {
                Provider.Release.TrySetResult(); Identity.HoldResolve = Identity.HoldCleanup = false;
                foreach (var attempt in Identity.Attempts) { attempt.Complete(); attempt.CompleteCleanup(); }
                Check(await Context.CloseAsync(Wait));
                foreach (var box in Boxes) Check(await box.Context.CloseAsync(Wait));
            }
        }
        async UniTask<(byte data, long sequence, string digest)> Read(HeldProvider provider, StorageAddress address)
        {
            var session = provider.Actual.Create(address);
            try
            {
                var read = Value(await session.ReadAsync()); Require(read.Exists, "fixture.read.missing"); var bytes = read.CopyBytes();
                var decoded = Value(StorageFormat.Decode(bytes, new StorageLimits()));
                Require(decoded.Address.Namespace == address.Namespace && decoded.Address.Slot == address.Slot, "fixture.read.address");
                return (decoded.Records.Single().CopyPayload()[0], decoded.Sequence, StorageFormat.Digest(bytes));
            }
            finally { Check(await session.CleanupAsync(Cleanup)); }
        }
        static async UniTask Frames() { for (int i = 0; i < 4; i++) await UniTask.Yield(); }
        public async UniTask<AccountStorageCase> Run(string name)
        {
            Require(Names.Contains(name), "fixture.name");
            var row = new AccountStorageCase { name = name, injectedWriteStartHold = name == Names[0] || name == Names[1] }; Cases.Add(row);
            var env = new Env(files, name != Names[3]); var slot = "run-" + Guid.NewGuid().ToString("N");
            try
            {
                var start = await env.Context.StartAsync(Wait);
                if (name == Names[3])
                {
                    row.observedError = start.IsSuccess ? "" : start.Error.Code;
                    row.simulatedPolicyRejected = !start.IsSuccess && row.observedError == "account.simulated.disabled" && env.Identity.Attempts.Count == 0;
                    Require(row.simulatedPolicyRejected, "policy.simulated.allowed");
                }
                else
                {
                    Check(start); var a = await env.Login("account-A"); row.aNamespace = a.Owner.StorageNamespace; row.aGeneration = a.Owner.SessionGeneration;
                    var aBox = await env.Open(a, slot);
                    if (row.injectedWriteStartHold)
                    {
                        env.Provider.HeldNamespace = row.aNamespace; env.Provider.Armed = true; env.Provider.Retired = () => !a.IsAccepting;
                        var aWrite = aBox.Write(11).Preserve(); await env.Provider.Entered.Task;
                        var b = await env.Login("account-B"); row.bNamespace = b.Owner.StorageNamespace; row.bGeneration = b.Owner.SessionGeneration;
                        row.namespacesIsolated = row.aNamespace != row.bNamespace;
                        row.oldOwnerRejected = !env.Account.ValidateCurrent(a.Owner).IsSuccess && !a.IsAccepting;
                        row.oldScopeRetained = a.Scope.State == ScopeState.Closing && env.Account.RetiringCount == 1 && aWrite.Status == UniTaskStatus.Pending;
                        var bBox = await env.Open(b, slot); await bBox.Write(22); var beforeB = await Read(env.Provider, bBox.Address);
                        Require(row.namespacesIsolated && row.oldOwnerRejected && row.oldScopeRetained, "switch.owner.gate");
                        if (name == Names[1])
                        {
                            var closing = env.Context.CloseAsync(Wait).Preserve(); await Frames();
                            row.closeWaited = closing.Status == UniTaskStatus.Pending && env.Provider.Actual.OpenSessionCount > 0;
                            Require(row.closeWaited, "close.dropped_actual_write"); env.Provider.Release.TrySetResult(); await aWrite; Check(await closing);
                        }
                        else { env.Provider.Release.TrySetResult(); await aWrite; await Frames(); }
                        var readA = await Read(env.Provider, aBox.Address); var readB = await Read(env.Provider, bBox.Address);
                        row.aSequence = readA.sequence; row.bSequence = readB.sequence; row.aDigest = readA.digest; row.bDigest = readB.digest;
                        row.lateWriteKeptA = env.Provider.ActualWriteAfterRetirement && readA.data == 11 && readA.sequence == 1;
                        row.bUnchanged = readB.data == 22 && readB.sequence == 1 && readB.digest == beforeB.digest;
                        Require(row.lateWriteKeptA && row.bUnchanged, "write.owner.redirected");
                        if (name == Names[0])
                        {
                            var again = await env.Login("account-A"); var restored = await env.Open(again, slot); row.returnedGeneration = again.Owner.SessionGeneration;
                            row.returnedARestored = again.Owner.StorageNamespace == row.aNamespace && restored.Restored == 11 && restored.Storage.CommittedSequence == 1;
                            row.newGeneration = row.returnedGeneration > row.bGeneration && row.bGeneration > row.aGeneration && !env.Account.ValidateCurrent(a.Owner).IsSuccess;
                            Require(row.returnedARestored && row.newGeneration, "return.old_session_reused");
                        }
                    }
                    else
                    {
                        await aBox.Write(11); var before = await Read(env.Provider, aBox.Address);
                        env.Identity.HoldResolve = true; env.Identity.NextOwnerId = "account-B";
                        using (var cancellation = new CancellationTokenSource())
                        {
                            var pending = Value(env.Account.CaptureSwitch()).WaitAsync(OperationContext.Unlimited(cancellation.Token)).Preserve();
                            cancellation.Cancel(); var canceled = await pending; row.observedError = canceled.IsSuccess ? "" : canceled.Error.Code;
                            Require(!canceled.IsSuccess && row.observedError == "account.wait.canceled", "identity.observer");
                            Require(env.Account.Current == null && env.Account.PendingCount == 1, "identity.dropped_actual");
                            env.Identity.Attempts.Last().Complete(); await Frames();
                            var after = await Read(env.Provider, aBox.Address);
                            row.aSequence = after.sequence; row.aDigest = after.digest;
                            row.lateIdentityFenced = env.Account.Current == null && env.Account.PendingCount == 0 && after.data == 11 && after.digest == before.digest;
                            row.oldOwnerRejected = !env.Account.ValidateCurrent(a.Owner).IsSuccess;
                            Require(row.lateIdentityFenced && row.oldOwnerRejected, "identity.late_publish");
                        }
                    }
                }
            }
            catch (Exception e) { row.error = e.Message; }
            finally
            {
                try { await env.Finish(); } catch (Exception e) { row.error += ";" + e.Message; }
                row.writes = env.Files.Writes; row.identityAttempts = env.Identity.Attempts.Count; row.identityCleanups = env.Identity.Attempts.Sum(a => a.CleanupCount);
                row.modules = env.Context.RetainedModuleCount + env.Boxes.Sum(b => b.Context.RetainedModuleCount);
                row.fileSessions = env.Provider.Actual.OpenSessionCount; row.storagePending = env.Boxes.Sum(b => b.Storage.PendingCount);
                row.accountPending = env.Account.PendingCount; row.accountScopes = env.Account.ReadDiagnostics().ActiveScopes;
                row.fileCleanupPending = env.Files.CleanupPending;
                row.passed = row.error == "" && !row.fileCleanupPending && row.modules + row.fileSessions + row.storagePending + row.accountPending + row.accountScopes == 0;
            }
            return row;
        }
        public async UniTask Execute() { foreach (var name in Names) Require((await Run(name)).passed, "case.failed:" + name); }
    }
}
