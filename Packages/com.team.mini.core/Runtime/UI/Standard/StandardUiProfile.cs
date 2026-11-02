using System;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.UI.Standard
{
    // Simple, versioned DTO. Persistence format and data migration remain S4 responsibilities.
    [Serializable]
    public sealed class StandardUiProfile
    {
        public int schemaVersion=1;
        public string unity="2022.3.62f3";
        public string presentation="uGUI";
        public string animation="UnityAnimator";
        public string visibilityParameter=AnimatorPresentation.VisibilityParameter;
        public string renderPipeline="URP-2D";
        public string fontIntegration="TextMeshPro";
        public string serialization="JsonUtility-SimpleDTO";
        public string accountMode="LocalOnly";
        public string assetProvider="YooAsset";
        public string editorProvider="FakeOrLocal";
        public string uguiVersion="1.0.0";
        public string urpVersion="14.0.12";
        public string tmpVersion="3.0.7";
        public string yooAssetVersion="3.0.6";
        public string fontAssetGuid="";
        public string fontLicenseEvidence="";
        public string glyphCoverageEvidence="";
        public static IPanelSource Compose(IPoolService<GameObject> pool)=>new PooledPanelSource(pool);
    }
}
