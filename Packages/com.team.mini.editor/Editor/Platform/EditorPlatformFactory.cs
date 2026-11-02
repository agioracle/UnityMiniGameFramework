using MiniFramework.Contracts;
using MiniFramework.Providers.PlatformMock;
namespace MiniFramework.Platform.Editor
{
    public static class EditorPlatformFactory
    {
        // A new isolated backend each time. No automatic SDK discovery or persistent global mock state.
        public static IPlatformBackend CreateMock()=>new MockPlatformBackend();
    }
}
