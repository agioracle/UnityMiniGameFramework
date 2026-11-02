using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MiniFramework.Contracts;
namespace MiniFramework.Host
{
    public sealed class StopwatchClock : IMonotonicClock
    {
        public double Seconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
    public sealed class HostScheduler : IRuntimeScheduler, IApplicationLifecycle, IDisposable, IDiagnosticSource
    {
        public DiagnosticCounters ReadDiagnostics() { CheckThread();return new DiagnosticCounters{Listeners=PauseListenerCount,Timers=TimerCount,Ticks=TickCount,PendingOperations=PendingOperationCount,QueuedCallbacks=QueuedCount,FailedCleanup=FailedDiscardCount}; }
        private sealed class Registration : IDisposable
        {
            internal HostScheduler Owner;
            internal Action Callback;
            internal Action<float,float> Tick;
            internal Action<bool> Pause;
            internal ClockDomain Clock;
            internal double Due, Interval;
            internal bool Active = true;
            public void Dispose() { Owner.CheckThread(); Active = false; Callback = null; Tick = null; Pause = null; if (!Owner.pumping && !Owner.closing) Owner.Compact(); }
        }
        private sealed class Operation : IHostOperation
        {
            internal HostScheduler Owner;
            internal OperationContext Context;
            internal double Started;
            internal Action<FrameworkError> Interrupted;
            internal bool Pending = true;
            public bool IsPending { get { Owner.CheckThread(); return Pending; } }
            internal bool Check()
            {
                if (!Pending) return false;
                FrameworkError error;
                if (Context.Cancellation.IsCancellationRequested) error = new FrameworkError(ErrorKind.Canceled,"host.operation.canceled");
                else if (Context.Timeout != Timeout.InfiniteTimeSpan && Owner.Realtime - Started >= Context.Timeout.TotalSeconds)
                    error = new FrameworkError(ErrorKind.Timeout,"host.operation.timeout");
                else return true;
                Finish(error); return false;
            }
            internal void Finish(FrameworkError error)
            {
                if (!Pending) return;
                Pending = false; var callback = Interrupted; Interrupted = null;
                try { callback(error); } catch (Exception) { Owner.Record("host.operation.callback"); }
            }
            public Result TryComplete(Action commit)
            {
                Owner.CheckThread();
                if (commit == null) throw new ArgumentNullException(nameof(commit));
                if (Owner.closed || Owner.IsPaused || !Check()) return Fail("host.operation.not_pending");
                Pending = false; Interrupted = null;
                try { commit(); return Result.Success(); }
                catch (Exception) { Owner.Record("host.operation.commit"); return Result.Failure(new FrameworkError(ErrorKind.Unexpected,"host.operation.commit")); }
            }
            public void Dispose() { Owner.CheckThread(); Finish(new FrameworkError(ErrorKind.Canceled,"host.operation.disposed")); if(!Owner.pumping && !Owner.closing) Owner.Compact(); }
        }
        private readonly struct Work
        {
            internal readonly Action Dispatch, Discard;
            internal Work(Action dispatch, Action discard) { Dispatch=dispatch; Discard=discard; }
        }
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly IMonotonicClock clock;
        private readonly List<Registration> timers = new List<Registration>();
        private readonly List<Registration> ticks = new List<Registration>();
        private readonly List<Registration> pauses = new List<Registration>();
        private readonly List<Operation> operations = new List<Operation>();
        private readonly Queue<Work> queue = new Queue<Work>();
        private readonly object queueGate = new object();
        private readonly int queueCapacity;
        private bool closed, pumping, closing, notifyingPause;
        private bool? queuedPause;
        private readonly Queue<Work> failedDiscards = new Queue<Work>();
        public int FailedDiscardCount { get { CheckThread(); return failedDiscards.Count; } }
        private double lastRealtime;
        public double ScaledTime { get; private set; }
        public double UnscaledTime { get; private set; }
        public double Realtime { get { CheckThread(); var now=clock.Seconds; if(double.IsNaN(now)||double.IsInfinity(now)||now<lastRealtime) throw new InvalidOperationException("Monotonic clock regressed."); lastRealtime=now;return now; } }
        public bool IsPaused { get; private set; }
        public int ErrorCount { get; private set; }
        public string LastErrorCode { get; private set; }
        public int TimerCount { get { CheckThread(); var n=0;foreach(var x in timers)if(x.Active)n++;return n; } }
        public int TickCount { get { CheckThread(); var n=0;foreach(var x in ticks)if(x.Active)n++;return n; } }
        public int PendingOperationCount { get { CheckThread();var n=0;foreach(var x in operations)if(x.Pending)n++;return n; } }
        public int QueuedCount { get { lock(queueGate)return queue.Count; } }
        public HostScheduler(IMonotonicClock clock = null, int queueCapacity = 1024)
        {
            if(queueCapacity<=0)throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            this.clock=clock??new StopwatchClock();this.queueCapacity=queueCapacity;
            lastRealtime=double.NegativeInfinity; var ignored=Realtime;
        }
        private void CheckThread() { if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Host operations require owner thread."); }
        private void Open() { CheckThread();if(closed)throw new ObjectDisposedException(nameof(HostScheduler)); }
        private static Result Fail(string code) => Result.Failure(new FrameworkError(ErrorKind.InvalidState,code));
        private void Record(string code) { ErrorCount++;LastErrorCode=code; }
        public IDisposable Schedule(double delay, ClockDomain domain, Action callback, double repeatInterval=0)
        {
            Open();
            if(double.IsNaN(delay)||double.IsInfinity(delay)||delay<0||double.IsNaN(repeatInterval)||double.IsInfinity(repeatInterval)||repeatInterval<0)
                throw new ArgumentOutOfRangeException(nameof(delay));
            if(callback==null)throw new ArgumentNullException(nameof(callback));
            var item=new Registration{Owner=this,Callback=callback,Clock=domain,Due=Time(domain)+delay,Interval=repeatInterval};
            timers.Add(item);return item;
        }
        public IDisposable SubscribeTick(Action<float,float> tick)
        {
            Open();if(tick==null)throw new ArgumentNullException(nameof(tick));
            var item=new Registration{Owner=this,Tick=tick};ticks.Add(item);return item;
        }
        public int PauseListenerCount { get { CheckThread();var count=0;foreach(var item in pauses)if(item.Active)count++;return count; } }
        public IDisposable SubscribePause(Action<bool> changed)
        {
            Open();if(changed==null)throw new ArgumentNullException(nameof(changed));
            var item=new Registration{Owner=this,Pause=changed};pauses.Add(item);return item;
        }
        public IHostOperation Observe(OperationContext context, Action<FrameworkError> interrupted)
        {
            Open();if(interrupted==null)throw new ArgumentNullException(nameof(interrupted));
            var item=new Operation{Owner=this,Context=context,Started=Realtime,Interrupted=interrupted};
            operations.Add(item);item.Check();return item;
        }
        public bool Post(Action dispatch,Action discard)
        {
            if(dispatch==null||discard==null)throw new ArgumentNullException();
            lock(queueGate) { if(closed||queue.Count>=queueCapacity)return false;queue.Enqueue(new Work(dispatch,discard));return true; }
        }
        public void SetPaused(bool paused)
        {
            Open();if(notifyingPause){queuedPause=paused;return;}
            queuedPause=paused;notifyingPause=true;
            try
            {
                var transitions=0;
                while(queuedPause.HasValue&&!closed)
                {
                    var next=queuedPause.Value;queuedPause=null;if(next==IsPaused)continue;
                    if(++transitions>32){Record("host.pause.reentrant.limit");break;}
                    IsPaused=next;
                    foreach(var item in pauses.ToArray())
                    {
                        if(closed)break;if(!item.Active)continue;
                        try{item.Pause(next);}catch(Exception){Record("host.pause.callback");}
                    }
                }
            }
            finally{notifyingPause=false;queuedPause=null;if(!pumping)Compact();}
        }
        private double Time(ClockDomain domain)
        {
            switch(domain){case ClockDomain.Scaled:return ScaledTime;case ClockDomain.Unscaled:return UnscaledTime;case ClockDomain.Realtime:return Realtime;default:throw new ArgumentOutOfRangeException(nameof(domain));}
        }
        private void Sweep()
        {
            var limit=operations.Count;
            for(var i=0;i<limit&&!closed;i++)operations[i].Check();
        }
        public Result Pump(float scaledDelta,float unscaledDelta)
        {
            Open();
            if(pumping)return Fail("host.pump.reentrant");
            if(float.IsNaN(scaledDelta)||float.IsInfinity(scaledDelta)||scaledDelta<0||float.IsNaN(unscaledDelta)||float.IsInfinity(unscaledDelta)||unscaledDelta<0)
                throw new ArgumentOutOfRangeException(nameof(scaledDelta));
            if(IsPaused)return Result.Success();
            pumping=true;var errors=ErrorCount;
            // All phase boundaries are fixed before callbacks; additions run in a later pump.
            var timerLimit=timers.Count;var tickLimit=ticks.Count;var workLimit=QueuedCount;
            try
            {
                ScaledTime+=scaledDelta;UnscaledTime+=unscaledDelta;
                Sweep();
                for(var i=0;i<workLimit&&!closed&&!IsPaused;i++)
                {
                    Sweep();if(closed||IsPaused)break;
                    Work work;lock(queueGate){if(queue.Count==0)break;work=queue.Dequeue();}
                    try { work.Dispatch(); } catch(Exception){Record("host.dispatch");}
                }
                for(var i=0;i<timerLimit&&!closed&&!IsPaused;i++)
                {
                    var item=timers[i];if(!item.Active||Time(item.Clock)<item.Due)continue;
                    var callback=item.Callback;
                    if(item.Interval==0){item.Active=false;item.Callback=null;}
                    else item.Due=Time(item.Clock)+item.Interval; // No catch-up storm after suspension.
                    try {callback();}catch(Exception){Record("host.timer");}
                }
                for(var i=0;i<tickLimit&&!closed&&!IsPaused;i++)
                {
                    var item=ticks[i];if(!item.Active)continue;
                    try {item.Tick(scaledDelta,unscaledDelta);}catch(Exception){Record("host.tick");}
                }
            }
            finally {pumping=false;Compact();}
            return ErrorCount==errors?Result.Success():Result.Failure(new FrameworkError(ErrorKind.Unexpected,"host.callbacks"));
        }
        private void Compact()
        {
            timers.RemoveAll(DeadRegistration);ticks.RemoveAll(DeadRegistration);pauses.RemoveAll(DeadRegistration);operations.RemoveAll(DeadOperation);
        }
        private static readonly Predicate<Registration> DeadRegistration=x=>!x.Active;
        private static readonly Predicate<Operation> DeadOperation=x=>!x.Pending;
        public void Dispose()
        {
            CheckThread();if(closing)return;lock(queueGate){closed=true;}
            closing=true;
            try
            {
                foreach(var x in timers){x.Active=false;x.Callback=null;}foreach(var x in ticks){x.Active=false;x.Tick=null;}foreach(var x in pauses){x.Active=false;x.Pause=null;}
                var limit=operations.Count;for(var i=0;i<limit;i++)operations[i].Finish(new FrameworkError(ErrorKind.Canceled,"host.closed"));
                var retries=failedDiscards.Count;
                for(var i=0;i<retries;i++)Discard(failedDiscards.Dequeue());
                while(true)
                {
                    Work work;lock(queueGate){if(queue.Count==0)break;work=queue.Dequeue();}
                    Discard(work);
                }
            }
            finally {closing=false;if(!pumping)Compact();}
            if(failedDiscards.Count>0)throw new InvalidOperationException("Host discard failed; explicit Dispose retry required.");
        }
        private void Discard(Work work)
        {
            try{work.Discard();}catch(Exception){Record("host.discard");failedDiscards.Enqueue(work);}
        }
    }
}
