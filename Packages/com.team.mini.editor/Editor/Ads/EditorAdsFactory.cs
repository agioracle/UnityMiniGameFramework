using MiniFramework.Providers.AdsMock;
namespace MiniFramework.Ads.Editor
{
    public static class EditorAdsFactory
    {
        // Explicit opt-in. The platform Mock must independently enable RewardedAds capability.
        public static MockRewardedAdBackend CreateMock()=>new MockRewardedAdBackend();
    }
}
