using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Boot;
using Game.Features;
using MiniFramework.Contracts;
using NUnit.Framework;
using UnityEngine.TestTools;
public sealed class LocalCompositionTests
{
    [UnityTest] public IEnumerator LocalCompositionWorksWithoutPlatformPermission() => UniTask.ToCoroutine(async () =>
    {
        var local = new LocalGame("Local home");
        Assert.AreEqual(ErrorKind.NotReady, (await local.Home.RefreshAsync(OperationContext.Unlimited())).Error.Kind);
        Assert.AreEqual(ErrorKind.NotReady, (await HomePanel.OpenAsync(local.Access, OperationContext.Unlimited())).Error.Kind);
        Assert.IsTrue((await local.StartAsync(OperationContext.Unlimited())).IsSuccess);
        Assert.AreEqual("Local home", (await local.Home.RefreshAsync(OperationContext.Unlimited())).Value);
        var opened = await HomePanel.OpenAsync(local.Access, OperationContext.Unlimited());
        Assert.IsTrue(opened.IsSuccess);
        Assert.AreEqual("Local home", (await opened.Value.RefreshAsync(OperationContext.Unlimited())).Value);
        Assert.IsTrue((await local.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        Assert.AreEqual(ErrorKind.NotReady, (await local.Home.RefreshAsync(OperationContext.Unlimited())).Error.Kind);
        Assert.AreEqual(ErrorKind.NotReady, (await HomePanel.OpenAsync(local.Access, OperationContext.Unlimited())).Error.Kind);
    });
    [UnityTest] public IEnumerator SeparateCompositionsDoNotShareStaticState() => UniTask.ToCoroutine(async () =>
    {
        var a = new LocalGame("A"); var b = new LocalGame("B");
        await a.StartAsync(OperationContext.Unlimited()); await b.StartAsync(OperationContext.Unlimited());
        await a.CloseAsync(OperationContext.Unlimited());
        Assert.AreEqual(ErrorKind.NotReady, (await a.Home.RefreshAsync(OperationContext.Unlimited())).Error.Kind);
        Assert.AreEqual("B", (await b.Home.RefreshAsync(OperationContext.Unlimited())).Value);
        await b.CloseAsync(OperationContext.Unlimited());
    });
}
