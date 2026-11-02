using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.UI
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasGroup))]
    public abstract class UIPanel : MonoBehaviour,IUiPanel
    {
        public virtual UniTask<Result> OnOpenAsync(IScope viewScope,OperationContext operation)=>UniTask.FromResult(Result.Success());
        // Retried only after failure. User cleanup must be idempotent on retry.
        public virtual UniTask<Result> OnCloseAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
    }
    public enum PanelMultiplicity { Single, Multiple }
    public enum PanelCachePolicy { ReturnToPool, RetainInstance }
    public sealed class PanelRegistration
    {
        public Type PanelType {get;} public ResourceKey Prefab {get;} public int Layer {get;}
        public bool Modal {get;} public PanelMultiplicity Multiplicity {get;} public PanelCachePolicy Cache {get;}
        private PanelRegistration(Type type,ResourceKey prefab,int layer,bool modal,PanelMultiplicity multiplicity,PanelCachePolicy cache)
        {
            if(!prefab.IsValid||prefab.Kind!=ResourceKind.Asset||prefab.AssetType!=typeof(GameObject)||!Enum.IsDefined(typeof(PanelMultiplicity),multiplicity)||!Enum.IsDefined(typeof(PanelCachePolicy),cache))throw new ArgumentException("Valid prefab key and policies required.");
            PanelType=type;Prefab=prefab;Layer=layer;Modal=modal;Multiplicity=multiplicity;Cache=cache;
        }
        public static PanelRegistration For<T>(ResourceKey prefab,int layer=0,bool modal=false,PanelMultiplicity multiplicity=PanelMultiplicity.Single,PanelCachePolicy cache=PanelCachePolicy.ReturnToPool) where T:UIPanel=>new PanelRegistration(typeof(T),prefab,layer,modal,multiplicity,cache);
    }
    // Existing authored layer and optional full-screen raycast graphic. Never creates UI hierarchy.
    public sealed class UiLayerHost
    {
        public int Order {get;} public Transform Root {get;} public GameObject ModalMask {get;}
        public UiLayerHost(int order,Transform root,GameObject modalMask=null)
        {
            if(root==null||modalMask!=null&&modalMask.transform.parent!=root)throw new ArgumentException("Authored root and direct-child mask required.");
            Order=order;Root=root;ModalMask=modalMask;
        }
    }
    // Match subscription ownership to a single ViewScope, including cached panels.
    public sealed class UiSubscription : IDisposable
    {
        private Action unsubscribe;
        private UiSubscription(Action unsubscribe){this.unsubscribe=unsubscribe;}
        public static Result<ICleanupTicket> Bind(IScope view,string id,Action subscribe,Action unsubscribe)
        {
            if(view==null||subscribe==null||unsubscribe==null)return Result<ICleanupTicket>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"ui.subscription.argument"));
            var lease=new UiSubscription(unsubscribe);var registered=view.TrackSubscription(id,lease);
            if(!registered.IsSuccess)return registered;
            try{subscribe();return registered;}
            catch(Exception e){return Result<ICleanupTicket>.Failure(new FrameworkError(ErrorKind.Unexpected,"ui.subscription.attach",e.Message));}
        }
        public void Dispose(){var action=unsubscribe;if(action==null)return;action();unsubscribe=null;}
    }
}
