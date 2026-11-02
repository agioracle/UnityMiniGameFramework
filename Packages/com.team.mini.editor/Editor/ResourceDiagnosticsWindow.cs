using System.Text;
using MiniFramework.Contracts;
using UnityEditor;
using UnityEngine;
namespace MiniFramework.EditorFoundation
{
    // Explicit source injection: the editor never discovers or owns runtime contexts.
    public sealed class ResourceDiagnosticsWindow:EditorWindow
    {
        private IAssetResourceDiagnostics assets;private IPoolResourceDiagnostics pool;private Vector2 scroll;
        public bool HasSources=>assets!=null||pool!=null;
        public string SnapshotText {get;private set;}="No resource sources attached.";
        [MenuItem("Tools/MiniFramework/Resource Diagnostics")]
        public static void Open()=>GetWindow<ResourceDiagnosticsWindow>("Resource Diagnostics").Show();
        public void Attach(IAssetResourceDiagnostics assetSource,IPoolResourceDiagnostics poolSource)
        {assets=assetSource;pool=poolSource;Capture();}
        public void Capture()
        {SnapshotText=Format(assets?.CaptureResources(),pool?.CapturePoolResources());Repaint();}
        public void Detach(){assets=null;pool=null;SnapshotText="No resource sources attached.";}
        private void OnDisable()=>Detach();
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Logical ownership only; no memory-size estimate. Attach diagnostic interfaces from your Editor composition, then refresh on demand.",MessageType.Info);
            using(new EditorGUI.DisabledScope(!HasSources))if(GUILayout.Button("Refresh snapshot"))Capture();
            if(GUILayout.Button("Detach"))Detach();
            scroll=EditorGUILayout.BeginScrollView(scroll);EditorGUILayout.SelectableLabel(SnapshotText,EditorStyles.textArea,GUILayout.ExpandHeight(true));EditorGUILayout.EndScrollView();
        }
        public static string Format(AssetResourceSnapshot assets,PoolResourceSnapshot pool)
        {
            var text=new StringBuilder();
            if(assets!=null)
            {
                text.AppendLine("ASSETS");foreach(var e in assets.Entries)text.AppendLine($"{Key(e.Key)} interests={e.Interests} pending={e.Pending} load={e.Loading} download={e.Downloading} release={e.Releasing} failed={e.ReleaseFailed}");
                foreach(var owner in assets.Owners)text.AppendLine($"  {Key(owner.Key)} request={owner.RequestId} owner={owner.OwnerId} state={owner.OwnerState} lease={owner.HasLease} cleanup={owner.Cleaning}");
            }
            if(pool!=null)
            {
                text.AppendLine("POOL");foreach(var b in pool.Buckets)text.AppendLine($"{Key(b.Key)} owner={b.OwnerId} active={b.Active} idle={b.Idle} pending={b.Pending} destroying={b.Destroying} closing={b.Closing} failed={b.FailedCleanup}");
                foreach(var r in pool.Rents)text.AppendLine($"  {Key(r.Key)} request={r.RequestId} owner={r.OwnerId} rent={r.RentScopeId} state={r.State}");
            }
            return text.Length==0?"No resource sources attached.":text.ToString();
        }
        private static string Key(ResourceKey key)=>$"{key.Provider}/{key.Package}/{key.ContentVersion}/{key.Address} [{key.Kind}:{key.AssetType?.FullName}]";
    }
}
