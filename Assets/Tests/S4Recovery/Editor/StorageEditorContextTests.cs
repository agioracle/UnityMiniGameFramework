using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Runtime;
using MiniFramework.Providers.StorageEditor;
using NUnit.Framework;
using UnityEngine.TestTools;
public sealed class StorageEditorContextTests
{
    sealed class Data{}
    [UnityTest] public IEnumerator RealEditorContextMigratesCommitsReopensAndRestores()=>UniTask.ToCoroutine(async()=>
    {
        var root=Path.GetFullPath("Library/S402StorageTests/"+Guid.NewGuid().ToString("N"));var address=new StorageAddress("local","main");
        var provider=new EditorStorageProvider(root);byte model=1;
        var text=File.ReadAllText("Assets/Tests/S4Recovery/Fixtures/schema-v1.hex").Trim();var raw=Enumerable.Range(0,text.Length/2).Select(i=>Convert.ToByte(text.Substring(i*2,2),16)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(provider.PathFor(address)));File.WriteAllBytes(provider.PathFor(address),raw);
        StorageService Build(EditorStorageProvider backend)
        {
            var registry=new StorageRegistry();registry.Register(new StorageModuleRegistration("progress",3,"bytes-v3",true,typeof(Data),()=>Result<byte[]>.Success(new byte[]{model,0,9}),b=>b.Length==3?Result.Success():Result.Failure(new FrameworkError(ErrorKind.Integrity,"test.invalid"))));
            var migrations=new StorageMigrationRegistry();migrations.Register(new StorageMigrationStep("progress",1,"bytes-v1","bytes-v2",b=>Result<byte[]>.Success(b.Concat(new byte[]{0}).ToArray())));migrations.Register(new StorageMigrationStep("progress",2,"bytes-v2","bytes-v3",b=>Result<byte[]>.Success(b.Concat(new byte[]{9}).ToArray())));
            return new StorageService(backend,address,registry,recovery:new StorageRecovery(migrations));
        }
        GameContext Context(StorageService s)=>new GameContext(new[]{new ModuleRegistration("storage",Array.Empty<string>(),()=>s,typeof(IStorageRecoveryService))},new CleanupContext(default,Timeout.InfiniteTimeSpan));
        var first=Build(provider);var context=Context(first);GameContext second=null;
        try
        {
            Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(5,first.CommittedSequence);Assert.AreEqual(7,first.Restored.Read("progress").Value[0]);Assert.AreEqual(StoragePersistence.RestartRecoverable,first.LastReceipt.Persistence);
            model=42;var save=await first.Capture().Value.WaitAsync(OperationContext.Unlimited());Assert.IsTrue(save.IsSuccess);Assert.AreEqual(6,save.Value.Sequence);Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
            var reopened=Build(new EditorStorageProvider(root));second=Context(reopened);Assert.IsTrue((await second.StartAsync(OperationContext.Unlimited())).IsSuccess);model=reopened.Restored.Read("progress").Value[0];Assert.AreEqual(42,model);Assert.AreEqual(6,reopened.CommittedSequence);Assert.IsNull(reopened.LastReceipt);CollectionAssert.AreEqual(new byte[]{201,202},reopened.Restored.Read("addon").Value);
        }
        finally
        {
            await context.CloseAsync(OperationContext.Unlimited());if(second!=null)await second.CloseAsync(OperationContext.Unlimited());if(Directory.Exists(root))Directory.Delete(root,true);
        }
    });
}
