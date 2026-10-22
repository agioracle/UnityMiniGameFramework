using System;

namespace MiniGame.Framework
{
    [Serializable]
    public sealed class FrameworkOptions
    {
        public int TimerCapacity = 128;
        public int TimerCallbacksPerFrame = 64;

        internal void Validate()
        {
            if (TimerCapacity < 1 || TimerCapacity > 65536)
                throw new ArgumentOutOfRangeException(nameof(TimerCapacity));
            if (TimerCallbacksPerFrame < 1 || TimerCallbacksPerFrame > TimerCapacity)
                throw new ArgumentOutOfRangeException(nameof(TimerCallbacksPerFrame));
        }
    }
}
