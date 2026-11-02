using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine;
namespace MiniFramework.UI
{
    public sealed class UiService : IUiService,IModule,IAsyncCleanup,IDiagnosticSource
    {
        private readonly string instance=Guid.NewGuid().ToString("N");private readonly IPanelSource source;private readonly CleanupContext cleanup;
        private readonly Dictionary<Type,PanelRegistration> definitions;
        private readonly Dictionary<int,UiLayerHost> layers;
        private readonly List<Session> sessions=new List<Session>();private readonly List<Slot> slots=new List<Slot>();
        private readonly int maxCached;private int sequence;private bool ready,stopped,cleanupAttempted;
        public string Id {get;} public IReadOnlyList<string> Dependencies {get;}
        public int SessionCount=>sessions.Count;public int CachedCount=>slots.Count(s=>s.Cached);public int RetainedSlotCount=>slots.Count;
        // Distinct slot trees include RentScope; ViewScope lifetime is measured separately.
        public DiagnosticCounters ReadDiagnostics()
        {
            Main();var result=new DiagnosticCounters();foreach(var slot in slots)result.Add(slot.Scope.ReadDiagnostics());
            foreach(var session in sessions)result.Add(session.View.ReadDiagnostics());return result;
        }
        public int FailedCleanupCount=>sessions.Count(s=>s.State==UiSessionState.CleanupFailed)+slots.Count(s=>s.OwnerSession==null&&s.Failed);
        public UiService(IPanelSource source,IEnumerable<PanelRegistration> registrations,IEnumerable<UiLayerHost> layers,CleanupContext cleanup,int maxCached=8,string id="ui",string poolModuleId="pool")
        {
            Main();this.source=source??throw new ArgumentNullException(nameof(source));this.cleanup=cleanup;
            if(registrations==null||layers==null||maxCached<0||string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(poolModuleId))throw new ArgumentException("UI composition required.");
            definitions=registrations.ToDictionary(x=>x.PanelType);this.layers=layers.ToDictionary(x=>x.Order);
            if(this.layers.Values.Select(l=>l.Root).Distinct().Count()!=this.layers.Count||this.layers.Values.Any(a=>this.layers.Values.Any(b=>a!=b&&a.Root.IsChildOf(b.Root))))throw new ArgumentException("Layer roots must be distinct and non-nested.");
            foreach(var definition in definitions.Values)if(!this.layers.TryGetValue(definition.Layer,out var host)||definition.Modal&&host.ModalMask==null)throw new ArgumentException("Every panel needs an authored host; modal panels need a mask.");
            this.maxCached=maxCached;Id=id;Dependencies=Array.AsReadOnly(new[]{poolModuleId});
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();if(ready||stopped)return UniTask.FromResult(Fail(ErrorKind.InvalidState,"ui.initialize.state"));
            var interrupted=Interrupted(operation,Now);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            foreach(var host in layers.Values)if(host.Root==null)return UniTask.FromResult(Fail(ErrorKind.Unavailable,"ui.root.destroyed"));
            var ticket=context.Cleanup.Register(this);if(!ticket.IsSuccess)return UniTask.FromResult(Result.Failure(ticket.Error));ready=true;UpdateMasks();return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> StopAsync(CleanupContext context){Main();stopped=true;foreach(var session in sessions.ToArray())session.RequestClose();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext context)
        {
            Main();await StopAsync(context);var retry=cleanupAttempted;cleanupAttempted=true;
            Result result=Result.Success();
            foreach(var session in sessions.ToArray()){var closed=await session.CloseActual(retry);if(!closed.IsSuccess)result=closed;}
            foreach(var slot in slots.ToArray())if(slot.OwnerSession==null){var closed=await slot.Close(retry);if(!closed.IsSuccess)result=closed;}
            return result;
        }
        public async UniTask<Result<IUiSession<T>>> OpenAsync<T>(IScope owner,OperationContext wait) where T:class,IUiPanel
        {
            Main();var start=Now;var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return Reject<T>(interrupted.Value);
            if(!ready||stopped)return Reject<T>(Fail(ErrorKind.NotReady,"ui.not_ready"));
            if(owner==null||owner.State!=ScopeState.Open)return Reject<T>(Fail(ErrorKind.InvalidState,"ui.owner"));
            if(!definitions.TryGetValue(typeof(T),out var definition))return Reject<T>(Fail(ErrorKind.NotSupported,"ui.panel.unregistered"));
            var session=definition.Multiplicity==PanelMultiplicity.Single?sessions.FirstOrDefault(s=>s.Definition.PanelType==typeof(T)):null;
            if(session!=null&&session.State==UiSessionState.Open&&session.Panel==null){session.RequestClose();return Reject<T>(Fail(ErrorKind.Unavailable,"ui.panel.destroyed"));}
            if(session!=null&&(session.Owner!=owner||session.State==UiSessionState.Closing||session.State==UiSessionState.CleanupFailed))return Reject<T>(Fail(ErrorKind.InvalidState,"ui.singleton.owned_or_closing"));
            if(session==null)
            {
                session=new Session(this,definition,owner,++sequence);
                var ticket=owner.Cleanup.Register(session);if(!ticket.IsSuccess)return Reject<T>(Result.Failure(ticket.Error));
                sessions.Add(session);session.Waiters++;session.Start();
            }
            else session.Waiters++;
            try
            {
                while(true)
                {
                    interrupted=Interrupted(wait,start);
                    if(interrupted.HasValue)return Reject<T>(interrupted.Value);
                    if(owner.State!=ScopeState.Open)return Reject<T>(Fail(ErrorKind.Canceled,"ui.owner.closed"));
                    if(session.Opened.Task.Status!=UniTaskStatus.Pending)
                    {
                        var opened=await session.Opened.Task;if(!opened.IsSuccess)return Reject<T>(opened);
                        if(session.State!=UiSessionState.Open)return Reject<T>(Fail(ErrorKind.Canceled,"ui.open.closed"));
                        session.Delivered=true;return Result<IUiSession<T>>.Success(new Handle<T>(session));
                    }
                    await UniTask.Yield();
                }
            }
            finally {session.Waiters--;if(session.Waiters==0&&!session.Delivered)session.RequestClose();}
        }
        public async UniTask<Result> CloseAsync<T>(IScope owner,OperationContext wait) where T:class,IUiPanel
        {
            Main();var start=Now;var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
            var targets=sessions.Where(s=>s.Definition.PanelType==typeof(T)&&s.Owner==owner).ToArray();
            foreach(var target in targets)target.RequestClose();
            foreach(var target in targets){var result=await Observe(target.CloseActual(false),wait,start);if(!result.IsSuccess)return result;}return Result.Success();
        }
        public UniTask<Result> BackAsync(IScope owner,OperationContext wait)
        {
            Main();var interrupted=Interrupted(wait,Now);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            var target=sessions.Where(s=>s.Owner==owner&&s.State!=UiSessionState.Closed).OrderByDescending(s=>s.Definition.Layer).ThenByDescending(s=>s.Sequence).FirstOrDefault();
            return target==null?UniTask.FromResult(Result.Success()):Observe(target.CloseActual(false),wait,Now);
        }
        public async UniTask<Result> TrimCacheAsync(OperationContext wait)
        {
            Main();var start=Now;var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
            var targets=slots.Where(s=>s.Cached).ToArray();var tasks=new List<UniTask<Result>>();foreach(var slot in targets)tasks.Add(slot.Close(false));
            foreach(var task in tasks){var result=await Observe(task,wait,start);if(!result.IsSuccess)return result;}return Result.Success();
        }
        public async UniTask<Result> RetryCleanupAsync(OperationContext wait)
        {
            Main();var start=Now;var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
            foreach(var session in sessions.ToArray())if(session.State==UiSessionState.CleanupFailed){var result=await Observe(session.CloseActual(true),wait,start);if(!result.IsSuccess)return result;}
            foreach(var slot in slots.ToArray())if(slot.OwnerSession==null&&slot.Failed){var result=await Observe(slot.Close(true),wait,start);if(!result.IsSuccess)return result;}return Result.Success();
        }
        private Slot Take(PanelRegistration definition)
        {
            var slot=slots.FirstOrDefault(s=>s.Cached&&s.Definition==definition);
            if(slot!=null){slot.Cached=false;return slot;}
            slot=new Slot(this,definition,++sequence);slots.Add(slot);return slot;
        }
        private void UpdateMasks()
        {
            foreach(var host in layers.Values)
            {
                if(host.ModalMask==null||host.Root==null)continue;
                var modal=sessions.Where(s=>s.Definition.Layer==host.Order&&s.Definition.Modal&&s.Slot?.Object!=null&&s.Slot.Object.activeSelf&&s.State!=UiSessionState.Closed).OrderByDescending(s=>s.Sequence).FirstOrDefault();
                host.ModalMask.SetActive(modal!=null);
                if(modal!=null){host.ModalMask.transform.SetAsLastSibling();host.ModalMask.transform.SetSiblingIndex(modal.Slot.Object.transform.GetSiblingIndex());}
            }
        }
        private sealed class Slot
        {
            internal readonly UiService Ui;internal readonly PanelRegistration Definition;internal readonly Scope Scope;
            internal IPoolRent<GameObject> Rent;internal GameObject Object;internal Session OwnerSession;internal bool Cached,Failed;private UniTaskCompletionSource<Result> close;
            internal Slot(UiService ui,PanelRegistration definition,int id){Ui=ui;Definition=definition;Scope=new Scope(ui.Id+"/"+ui.instance+"/slot/"+id);}
            internal UniTask<Result> Close(bool retry)
            {
                Cached=false;if(close==null||retry&&Failed){Failed=false;close=new UniTaskCompletionSource<Result>();RunClose(close,retry).Forget();}return close.Task;
            }
            private async UniTask RunClose(UniTaskCompletionSource<Result> completion,bool retry)
            {
                Result result;
                try{var report=retry&&Scope.State!=ScopeState.Open?await Scope.RetryFailedAsync(Ui.cleanup):await Scope.CloseAsync(Ui.cleanup);while(report.PendingCount>0){await UniTask.Yield();report=Scope.Snapshot;}result=report.IsFullyDrained?Result.Success():Fail(ErrorKind.CleanupFailed,"ui.slot.retained");}
                catch(Exception e){result=Fail(ErrorKind.CleanupFailed,"ui.slot.exception",e.Message);}
                await UniTask.SwitchToMainThread();Failed=!result.IsSuccess;
                if(result.IsSuccess){Rent=null;Object=null;Ui.slots.Remove(this);}completion.TrySetResult(result);
            }
        }
        private sealed class Session:IAsyncCleanup
        {
            internal readonly UiService Ui;internal readonly PanelRegistration Definition;internal readonly IScope Owner;internal readonly int Sequence;
            internal readonly Scope View;internal readonly CancellationTokenSource cancellation=new CancellationTokenSource();
            internal readonly UniTaskCompletionSource<Result> Opened=new UniTaskCompletionSource<Result>();
            internal Slot Slot;internal UIPanel Panel;internal int Waiters;internal bool Delivered;
            internal UiSessionState State=UiSessionState.Opening;private bool closing,hookClosed,ownerCleanupObserved,disposed;private FrameworkError? cancellationError;
            private UniTaskCompletionSource<Result> closed;
            public string Id {get;}
            internal Session(UiService ui,PanelRegistration definition,IScope owner,int sequence){Ui=ui;Definition=definition;Owner=owner;Sequence=sequence;Id=ui.Id+"/"+ui.instance+"/view/"+sequence;View=new Scope(Id,ScopeKind.View);}
            internal void Start()=>RunOpen().Forget();
            private async UniTask RunOpen()
            {
                Result result;
                try
                {
                    Slot=Ui.Take(Definition);Slot.OwnerSession=this;
                    if(Slot.Rent==null)
                    {
                        var requested=Ui.source.Rent(Definition.Prefab,Slot.Scope);
                        if(!requested.IsSuccess){FinishOpen(Result.Failure(requested.Error));return;}
                        var rented=await requested.Value.WaitAsync(OperationContext.Unlimited(cancellation.Token));await UniTask.SwitchToMainThread();
                        if(!rented.IsSuccess){FinishOpen(Result.Failure(rented.Error));return;}Slot.Rent=rented.Value;Slot.Object=Slot.Rent.Value;
                    }
                    if(closing||Owner.State!=ScopeState.Open){FinishOpen(Fail(ErrorKind.Canceled,"ui.open.closed"));return;}
                    var go=Slot.Object;var host=Ui.layers[Definition.Layer];
                    if(go==null||host.Root==null){FinishOpen(Fail(ErrorKind.Unavailable,"ui.target.destroyed"));return;}
                    Panel=go.GetComponent(Definition.PanelType) as UIPanel;
                    if(Panel==null){FinishOpen(Fail(ErrorKind.Integrity,"ui.panel.component"));return;}
                    if(Panel is IUiGeneratedBindings bindings){var valid=bindings.ValidateGeneratedBindings();if(!valid.IsSuccess){FinishOpen(valid);return;}}
                    var group=go.GetComponent<CanvasGroup>();
                    if(group==null){FinishOpen(Fail(ErrorKind.Integrity,"ui.panel.canvas_group"));return;}
                    group.interactable=false;group.blocksRaycasts=false;
                    go.transform.SetParent(host.Root,false);go.transform.SetAsLastSibling();go.SetActive(true);
                    // Reset after activation and before OnOpen; no old animation state reaches the new session.
                    foreach(var animator in go.GetComponentsInChildren<Animator>(true))
                    {animator.enabled=true;if(animator.runtimeAnimatorController!=null){animator.Rebind();animator.Update(0);}}
                    Ui.UpdateMasks();
                    result=await Panel.OnOpenAsync(View,OperationContext.Unlimited(cancellation.Token));await UniTask.SwitchToMainThread();
                    if(result.IsSuccess&&(closing||Owner.State!=ScopeState.Open||Waiters==0||go==null))result=Fail(ErrorKind.Canceled,"ui.open.abandoned");
                }
                catch(Exception e){await UniTask.SwitchToMainThread();result=Fail(e is OperationCanceledException&&cancellation.IsCancellationRequested?ErrorKind.Canceled:ErrorKind.Unexpected,"ui.open.exception",e.Message);}
                FinishOpen(result);
            }
            private void FinishOpen(Result result)
            {
                if(result.IsSuccess){State=UiSessionState.Open;var group=Panel.GetComponent<CanvasGroup>();group.interactable=true;group.blocksRaycasts=true;}
                Opened.TrySetResult(result);if(!result.IsSuccess)RequestClose();Ui.UpdateMasks();
            }
            internal void RequestClose(){CloseActual(false).Forget();}
            internal UniTask<Result> CloseActual(bool retry)
            {
                Main();if(State==UiSessionState.Closed)return UniTask.FromResult(Result.Success());
                if(closed!=null&&(!retry||State!=UiSessionState.CleanupFailed))return closed.Task;
                closing=true;State=UiSessionState.Closing;closed=new UniTaskCompletionSource<Result>();
                if(retry)cancellationError=null;
                try{cancellation.Cancel();}catch(Exception e){cancellationError=new FrameworkError(ErrorKind.CleanupFailed,"ui.cancel.callback",e.Message);}
                RunClose(closed,retry).Forget();return closed.Task;
            }
            private async UniTask RunClose(UniTaskCompletionSource<Result> completion,bool retry)
            {
                Result result=Result.Success();
                try
                {
                    await Opened.Task;
                    if(Panel!=null)
                    {
                        var group=Panel.GetComponent<CanvasGroup>();if(group!=null){group.interactable=false;group.blocksRaycasts=false;}
                        if(!hookClosed){result=await Panel.OnCloseAsync(Ui.cleanup);await UniTask.SwitchToMainThread();hookClosed=result.IsSuccess;}
                    }
                    if(result.IsSuccess)
                    {
                        var report=retry&&View.State!=ScopeState.Open?await View.RetryFailedAsync(Ui.cleanup):await View.CloseAsync(Ui.cleanup);
                        while(report.PendingCount>0){await UniTask.Yield();report=View.Snapshot;}
                        if(!report.IsFullyDrained)result=Fail(ErrorKind.CleanupFailed,"ui.view.retained");
                    }
                    if(result.IsSuccess&&cancellationError.HasValue)result=Result.Failure(cancellationError.Value);
                    if(result.IsSuccess&&Slot!=null)
                    {
                        if(Slot.Object!=null)Slot.Object.SetActive(false);
                        if(Delivered&&!Ui.stopped&&Definition.Cache==PanelCachePolicy.RetainInstance&&Ui.CachedCount<Ui.maxCached&&Slot.Object!=null&&Slot.Rent.State==PoolRentState.Active)
                        {Slot.Cached=true;Slot.OwnerSession=null;Slot=null;}
                        else {result=await Slot.Close(retry);if(result.IsSuccess){Slot.OwnerSession=null;Slot=null;}}
                    }
                }
                catch(Exception e){await UniTask.SwitchToMainThread();result=Fail(ErrorKind.CleanupFailed,"ui.close.exception",e.Message);}
                if(result.IsSuccess&&cancellationError.HasValue)result=Result.Failure(cancellationError.Value);
                State=result.IsSuccess?UiSessionState.Closed:UiSessionState.CleanupFailed;
                if(result.IsSuccess){Panel=null;Ui.sessions.Remove(this);if(!disposed){disposed=true;cancellation.Dispose();}}
                Ui.UpdateMasks();completion.TrySetResult(result);
            }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup){var retry=ownerCleanupObserved;ownerCleanupObserved=true;return CloseActual(retry);}
        }
        private sealed class Handle<T>:IUiSession<T> where T:class,IUiPanel
        {
            private readonly Session session;internal Handle(Session session){this.session=session;}
            public string Id=>session.Id;public T Panel=>session.State==UiSessionState.Open?session.Panel as T:null;
            public IScope ViewScope=>session.View;public UiSessionState State=>session.State;
            public void RequestClose()=>session.RequestClose();
            public UniTask<Result> CloseAsync(OperationContext wait){Main();var start=Now;var interrupted=Interrupted(wait,start);return interrupted.HasValue?UniTask.FromResult(interrupted.Value):Observe(session.CloseActual(false),wait,start);}
            public UniTask<Result> RetryCloseAsync(OperationContext wait){Main();var start=Now;var interrupted=Interrupted(wait,start);return interrupted.HasValue?UniTask.FromResult(interrupted.Value):Observe(session.CloseActual(true),wait,start);}
        }
        private static long Now=>Stopwatch.GetTimestamp();
        private static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("UI lifecycle requires main thread.");}
        private static Result? Interrupted(OperationContext wait,long start)
        {if(wait.Cancellation.IsCancellationRequested)return Fail(ErrorKind.Canceled,"ui.wait.canceled");if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Now-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Fail(ErrorKind.Timeout,"ui.wait.timeout");return null;}
        private static async UniTask<Result> Observe(UniTask<Result> task,OperationContext wait,long start)
        {while(true){var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;if(task.Status!=UniTaskStatus.Pending)return await task;await UniTask.Yield();}}
        private static Result Fail(ErrorKind kind,string code,string message="")=>Result.Failure(new FrameworkError(kind,code,message));
        private static Result<IUiSession<T>> Reject<T>(Result result)where T:class,IUiPanel=>Result<IUiSession<T>>.Failure(result.Error);
    }
}
