using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UInput=UnityEngine.Input;
namespace MiniFramework.Input
{
    public sealed class UnityPointerSource:IPointerSource
    {
        readonly List<RaycastResult> hits=new List<RaycastResult>(32);
        EventSystem eventSystem;PointerEventData eventData;
        public bool IsSupported
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return true;
#else
                return false;
#endif
            }
        }
        bool OverUi(int id,Vector2 position)
        {
            var current=EventSystem.current;if(!current)return false;
            if(current!=eventSystem){eventSystem=current;eventData=new PointerEventData(current);}
            eventData.Reset();eventData.pointerId=id;eventData.position=position;hits.Clear();current.RaycastAll(eventData,hits);
            // Only uGUI raycasters block gameplay; a PhysicsRaycaster is a world hit.
            foreach(var hit in hits)if(hit.module is UnityEngine.UI.GraphicRaycaster)return true;
            return false;
        }
        public Result<PointerFrame> Capture(PointerSample[] destination)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if(destination==null||destination.Length==0)return Result<PointerFrame>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.buffer.invalid"));
            var safe=Screen.safeArea;var metrics=new DeviceMetrics(Screen.width,Screen.height,new ScreenArea(safe.x,safe.y,safe.width,safe.height));var count=0;
            if(UInput.touchCount>destination.Length)return Result<PointerFrame>.Failure(new FrameworkError(ErrorKind.Unavailable,"input.pointer.capacity"));
            for(var i=0;i<UInput.touchCount;i++)
            {
                var touch=UInput.GetTouch(i);PointerPhase phase;
                switch(touch.phase)
                {case TouchPhase.Began:phase=PointerPhase.Began;break;case TouchPhase.Moved:phase=PointerPhase.Moved;break;case TouchPhase.Stationary:phase=PointerPhase.Stationary;break;case TouchPhase.Ended:phase=PointerPhase.Ended;break;default:phase=PointerPhase.Canceled;break;}
                destination[count++]=new PointerSample(touch.fingerId,phase,new ScreenPoint(touch.position.x,touch.position.y),OverUi(touch.fingerId,touch.position));
            }
            // Avoid duplicate mouse events synthesized from a touch.
            if(count==0&&(UInput.GetMouseButtonDown(0)||UInput.GetMouseButtonUp(0)||UInput.GetMouseButton(0)))
            {
                var position=(Vector2)UInput.mousePosition;var phase=UInput.GetMouseButtonDown(0)?PointerPhase.Began:UInput.GetMouseButtonUp(0)?PointerPhase.Ended:PointerPhase.Moved;
                destination[count++]=new PointerSample(-1,phase,new ScreenPoint(position.x,position.y),OverUi(-1,position));
            }
            return Result<PointerFrame>.Success(new PointerFrame(metrics,count));
#else
            return Result<PointerFrame>.Failure(new FrameworkError(ErrorKind.NotSupported,"input.legacy.disabled"));
#endif
        }
        public void Reset(){hits.Clear();}
    }
    public static class InputUiCoordinates
    {
        public static Result<Vector2> ScreenToLocal(RectTransform target,ScreenPoint point)
        {
            if(!target)return Result<Vector2>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.ui.target"));
            var canvas=target.GetComponentInParent<Canvas>();if(!canvas)return Result<Vector2>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.ui.canvas"));
            canvas=canvas.rootCanvas;var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            if(canvas.renderMode!=RenderMode.ScreenSpaceOverlay&&!camera)return Result<Vector2>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.ui.camera"));
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(target,new Vector2(point.X,point.Y),camera,out var local)?Result<Vector2>.Success(local):Result<Vector2>.Failure(new FrameworkError(ErrorKind.Unavailable,"input.ui.projection"));
        }
        public static Result ApplySafeArea(RectTransform target,DeviceMetrics metrics)
        {
            if(!target||!metrics.IsValid)return Result.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.safe_area.arguments"));
            var canvas=target.GetComponentInParent<Canvas>();if(!canvas)return Result.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.safe_area.canvas"));canvas=canvas.rootCanvas;
            if(target.parent!=canvas.transform||canvas.renderMode==RenderMode.WorldSpace)return Result.Failure(new FrameworkError(ErrorKind.NotSupported,"input.safe_area.root_child_required"));
            if(canvas.renderMode==RenderMode.ScreenSpaceCamera)
            {
                var camera=canvas.worldCamera;if(!camera)return Result.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.safe_area.camera"));
                var viewport=camera.rect;if(viewport!=new Rect(0,0,1,1))return Result.Failure(new FrameworkError(ErrorKind.NotSupported,"input.safe_area.viewport"));
            }
            var safe=metrics.SafeArea;target.anchorMin=new Vector2(safe.X/metrics.Width,safe.Y/metrics.Height);target.anchorMax=new Vector2((safe.X+safe.Width)/metrics.Width,(safe.Y+safe.Height)/metrics.Height);target.offsetMin=Vector2.zero;target.offsetMax=Vector2.zero;return Result.Success();
        }
    }
}
