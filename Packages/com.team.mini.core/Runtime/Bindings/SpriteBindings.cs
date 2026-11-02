using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine;
using UnityEngine.UI;
namespace MiniFramework.Bindings
{
    public static class SpriteBindings
    {
        public static Result<AssetBinding<Sprite>> ForImage(string id,Image image,IAssetsService assets,IScope owner,CleanupContext cleanup)
            =>AssetBinding<Sprite>.Create(id,assets,new ImageTarget(image),owner,cleanup);
        public static Result<AssetBinding<Sprite>> ForRenderer(string id,SpriteRenderer renderer,IAssetsService assets,IScope owner,CleanupContext cleanup)
            =>AssetBinding<Sprite>.Create(id,assets,new RendererTarget(renderer),owner,cleanup);
        public static Result<AssetBinding<Sprite>> ForChildImage(string id,Image image,IPoolRent<GameObject> rent,IAssetsService assets,CleanupContext cleanup)
        {
            if(rent==null||rent.State!=PoolRentState.Active||image==null)
                return Invalid();
            try {if(!image.transform.IsChildOf(rent.Value.transform))return Invalid();}
            catch(System.InvalidOperationException){return Invalid();}
            return ForImage(id,image,assets,rent.RentScope,cleanup);
        }
        private static Result<AssetBinding<Sprite>> Invalid()=>Result<AssetBinding<Sprite>>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"binding.child.owner"));
        private sealed class ImageTarget:IResourceTarget<Sprite>
        {private readonly Image image;internal ImageTarget(Image image){this.image=image;}public bool IsAlive=>image!=null;public Sprite Value{get=>image.sprite;set=>image.sprite=value;}}
        private sealed class RendererTarget:IResourceTarget<Sprite>
        {private readonly SpriteRenderer renderer;internal RendererTarget(SpriteRenderer renderer){this.renderer=renderer;}public bool IsAlive=>renderer!=null;public Sprite Value{get=>renderer.sprite;set=>renderer.sprite=value;}}
    }
}
