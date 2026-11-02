#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.S407AccountStorage.Tests
{
    public sealed class AccountStorageTests
    {
        sealed class Disk : IWeChatStorageFiles
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "s407-account-" + Guid.NewGuid().ToString("N"));
            public Disk() { Directory.CreateDirectory(Root); }
            public StoragePersistence Persistence => StoragePersistence.Volatile; // No host restart guarantee from Editor files.
            public Result<StorageReadResult> Read(string key, int max)
            { var path = Path.Combine(Root, key); if (!File.Exists(path)) return Result<StorageReadResult>.Success(new StorageReadResult(false)); Assert.LessOrEqual(new FileInfo(path).Length, max); return Result<StorageReadResult>.Success(new StorageReadResult(true, File.ReadAllBytes(path))); }
            public Result Write(string key, byte[] bytes) { File.WriteAllBytes(Path.Combine(Root, key), bytes); return Result.Success(); }
        }
        IEnumerator Run(string name) => UniTask.ToCoroutine(async () => {
            var disk = new Disk();
            try { var row = await new AccountStorageScenario(disk).Run(name); Assert.IsTrue(row.passed, row.error); }
            finally { Directory.Delete(disk.Root, true); } // Only this test's unique temporary directory.
        });
        [UnityTest] public IEnumerator LateWriteStaysWithOriginalAccountAndRestores() => Run(AccountStorageScenario.Names[0]);
        [UnityTest] public IEnumerator CloseWaitsForRetiredAccountActualWrite() => Run(AccountStorageScenario.Names[1]);
        [UnityTest] public IEnumerator CanceledIdentityCannotPublishLateOrRewriteSave() => Run(AccountStorageScenario.Names[2]);
        [UnityTest] public IEnumerator DefaultProductionPolicyRejectsSimulatedIdentity() => Run(AccountStorageScenario.Names[3]);
    }
}
#endif
