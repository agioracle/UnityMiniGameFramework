using System;
namespace MiniFramework.Contracts
{
    public enum PointerPhase { Began, Moved, Stationary, Ended, Canceled }
    public readonly struct ScreenPoint
    {
        public float X {get;} public float Y {get;}
        public ScreenPoint(float x,float y){if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(y)||float.IsInfinity(y))throw new ArgumentException("Finite screen point required.");X=x;Y=y;}
    }
    public readonly struct ScreenArea
    {
        public float X {get;}public float Y {get;}public float Width {get;}public float Height {get;}
        public ScreenArea(float x,float y,float width,float height){new ScreenPoint(x,y);new ScreenPoint(width,height);if(width<0||height<0)throw new ArgumentException("Nonnegative area required.");X=x;Y=y;Width=width;Height=height;}
    }
    public readonly struct DeviceMetrics
    {
        public int Width {get;}public int Height {get;}public ScreenArea SafeArea {get;}
        public bool IsValid=>Width>0&&Height>0&&SafeArea.Width>0&&SafeArea.Height>0;
        public DeviceMetrics(int width,int height,ScreenArea safeArea)
        {
            if(width<=0||height<=0)throw new ArgumentException("Positive display size required.");
            var x=Math.Max(0,Math.Min(width,safeArea.X));var y=Math.Max(0,Math.Min(height,safeArea.Y));
            var right=Math.Max(x,Math.Min(width,safeArea.X+safeArea.Width));var top=Math.Max(y,Math.Min(height,safeArea.Y+safeArea.Height));
            Width=width;Height=height;SafeArea=right>x&&top>y?new ScreenArea(x,y,right-x,top-y):new ScreenArea(0,0,width,height);
        }
        public ScreenPoint Normalize(ScreenPoint point){if(!IsValid)throw new InvalidOperationException("Device metrics unavailable.");return new ScreenPoint(point.X/Width,point.Y/Height);}
    }
    public readonly struct PointerSample
    {
        public int Id {get;}public PointerPhase Phase {get;}public ScreenPoint Position {get;}public bool OverUi {get;}
        public PointerSample(int id,PointerPhase phase,ScreenPoint position,bool overUi=false)
        {if((uint)phase>(uint)PointerPhase.Canceled)throw new ArgumentOutOfRangeException(nameof(phase));Id=id;Phase=phase;Position=position;OverUi=overUi;}
    }
    public interface IInputDeviceService
    {
        DeviceMetrics Metrics {get;}
        bool IsPaused {get;}
        // UI capture is fixed at Began for the entire gesture. Owner gates every callback.
        Result<IDisposable> Subscribe(IScope owner,Action<PointerSample> listener,bool includeUi=false);
    }
}
