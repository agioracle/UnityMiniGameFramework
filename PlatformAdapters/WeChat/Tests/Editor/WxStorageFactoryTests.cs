using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Providers.WeChat.WxStorage;
using NUnit.Framework;
public sealed class WxStorageFactoryTests
{
    [Test] public void EditorCannotClaimNativeFileStorage() { var r = WxStorageFiles.CreateNative(); Assert.IsFalse(r.IsSuccess); Assert.AreEqual(ErrorKind.NotSupported, r.Error.Kind); }
    [Test] public void InvalidRootIsRejectedBeforeHostProbe() { Assert.AreEqual(ErrorKind.InvalidArgument, WxStorageFiles.CreateNative("../root").Error.Kind); }
    [Test] public void RootDotsAreRejectedConsistentlyWithNativeBridge() { Assert.AreEqual(ErrorKind.InvalidArgument, WxStorageFiles.CreateNative("root.name").Error.Kind); }
    [Test] public void FactoryRequiresActualUnityMainThread() { Result<IWeChatStorageFiles> r = default; var t = new Thread(() => r = WxStorageFiles.CreateNative()); t.Start(); t.Join(); Assert.AreEqual(ErrorKind.InvalidState, r.Error.Kind); }
}
