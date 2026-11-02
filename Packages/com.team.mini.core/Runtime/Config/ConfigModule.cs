using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine;
namespace MiniFramework.Config
{
    public sealed class ConfigDefinition
    {
        public string Provider {get;} public string Package {get;} public string Version {get;} public string Schema {get;} public string CatalogAddress {get;}
        public IReadOnlyList<string> Tables {get;} public int MaxBytes {get;}
        public ConfigDefinition(string provider,string package,string version,string schema,string catalog,IEnumerable<string> tables,int maxBytes=16*1024*1024)
        {
            if(new[]{provider,package,version,schema,catalog}.Any(string.IsNullOrWhiteSpace)||tables==null||maxBytes<=0)throw new ArgumentException("Config identity required.");
            var list=tables.ToArray();if(list.Length==0||list.Length>32||list.Any(string.IsNullOrWhiteSpace)||list.Distinct(StringComparer.Ordinal).Count()!=list.Length)throw new ArgumentException("Unique configuration tables required.");
            Provider=provider;Package=package;Version=version;Schema=schema;CatalogAddress=catalog;Tables=Array.AsReadOnly(list);MaxBytes=maxBytes;
        }
    }
    [Serializable] public sealed class ConfigCatalog
    {
        public int format;public string package,contentVersion,configVersion,schema;public ConfigFile[] files;
    }
    [Serializable] public sealed class ConfigFile {public string name,address,sha256;public int size;}
    // One typed snapshot per GameContext. Generated tables and decoding stay in the consuming assembly.
    public sealed class ConfigModule<T>:IModule,IAsyncCleanup,IConfigService<T> where T:class
    {
        readonly IAssetsService assets;readonly IConfigPackageReadiness package;readonly ConfigDefinition definition;
        readonly Func<IReadOnlyDictionary<string,byte[]>,T> decode;readonly CleanupContext cleanup;
        readonly Scope resources;readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        UniTaskCompletionSource<Result> load;T value;bool stopped,cleaned,cleanupAttempted;
        public string Id {get;} public IReadOnlyList<string> Dependencies {get;} public ConfigState State {get;private set;}
        public ConfigModule(IAssetsService assets,IConfigPackageReadiness package,ConfigDefinition definition,Func<IReadOnlyDictionary<string,byte[]>,T> decode,CleanupContext cleanup,string id="config",string assetsId="assets",string packageId="package")
        {
            this.assets=assets??throw new ArgumentNullException(nameof(assets));this.package=package??throw new ArgumentNullException(nameof(package));this.definition=definition??throw new ArgumentNullException(nameof(definition));this.decode=decode??throw new ArgumentNullException(nameof(decode));this.cleanup=cleanup;
            if(new[]{id,assetsId,packageId}.Any(string.IsNullOrWhiteSpace)||id==assetsId||id==packageId||assetsId==packageId)throw new ArgumentException("Distinct module ids required.");
            Id=id;Dependencies=Array.AsReadOnly(new[]{assetsId,packageId});resources=new Scope(id+"/data/"+Guid.NewGuid().ToString("N"));
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Configuration lifecycle requires main thread.");}
        Result Compatible()
        {
            var ready=package.CheckReady(definition.Package,definition.Version);if(!ready.IsSuccess)return ready;
            return package.Schema==definition.Schema&&package.ConfigVersion==definition.Version?Result.Success():Fail(ErrorKind.Integrity,"config.package.compatibility");
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var start=Stopwatch.GetTimestamp();var interrupted=Interrupted(operation,start);if(interrupted.HasValue)return UniTask.FromResult(interrupted.Value);
            if(State!=ConfigState.Uninitialized||stopped)return UniTask.FromResult(Fail(ErrorKind.InvalidState,"config.initialize.state"));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            State=ConfigState.Loading;load=new UniTaskCompletionSource<Result>();Run().Forget();return Observe(load.Task,operation,start);
        }
        public Result<T> Get()
        {
            Main();if(stopped||State!=ConfigState.Ready)return Result<T>.Failure(new FrameworkError(ErrorKind.NotReady,"config.not_ready"));
            var compatible=Compatible();return compatible.IsSuccess?Result<T>.Success(value):Result<T>.Failure(compatible.Error);
        }
        async UniTask Run()
        {
            Result result;
            try
            {
                result=Compatible();if(!result.IsSuccess){Finish(result);return;}
                var catalogResult=await Read(definition.CatalogAddress);if(!catalogResult.IsSuccess){Finish(Result.Failure(catalogResult.Error));return;}
                var bytes=catalogResult.Value;if(bytes.Length>65536)throw new FormatException("catalog too large");
                var catalog=JsonUtility.FromJson<ConfigCatalog>(System.Text.Encoding.UTF8.GetString(bytes));
                Validate(catalog);
                var tables=new Dictionary<string,byte[]>(StringComparer.Ordinal);
                foreach(var file in catalog.files)
                {
                    var loaded=await Read(file.address);if(!loaded.IsSuccess){Finish(Result.Failure(loaded.Error));return;}
                    if(loaded.Value.Length!=file.size||Hash(loaded.Value)!=file.sha256)throw new FormatException("file integrity: "+file.name);
                    tables.Add(file.name,loaded.Value);
                }
                if(stopped){Finish(Fail(ErrorKind.Canceled,"config.stopped"));return;}
                var snapshot=decode(tables);if(snapshot==null)throw new FormatException("decoder returned null");
                result=Compatible();if(stopped)result=Fail(ErrorKind.Canceled,"config.stopped");
                if(result.IsSuccess)value=snapshot;
            }
            catch(Exception e){result=Fail(ErrorKind.Integrity,"config.data.invalid",e.Message);}
            Finish(result);
        }
        async UniTask<Result<byte[]>> Read(string address)
        {
            if(stopped)return Result<byte[]>.Failure(new FrameworkError(ErrorKind.Canceled,"config.stopped"));
            var ready=Compatible();if(!ready.IsSuccess)return Result<byte[]>.Failure(ready.Error);
            var request=assets.Load<TextAsset>(new ResourceKey(definition.Provider,definition.Package,definition.Version,address,typeof(TextAsset)),resources);
            if(!request.IsSuccess)return Result<byte[]>.Failure(request.Error);
            var result=await request.Value.WaitAsync(OperationContext.Unlimited(cancellation.Token));await UniTask.SwitchToMainThread();
            if(!result.IsSuccess)return Result<byte[]>.Failure(result.Error);
            if(stopped)return Result<byte[]>.Failure(new FrameworkError(ErrorKind.Canceled,"config.stopped"));
            var data=result.Value.Value.bytes;if(data.Length>definition.MaxBytes)return Result<byte[]>.Failure(new FrameworkError(ErrorKind.Integrity,"config.file.limit"));
            return Result<byte[]>.Success(data);
        }
        void Validate(ConfigCatalog catalog)
        {
            if(catalog==null||catalog.format!=1||catalog.package!=definition.Package||catalog.contentVersion!=definition.Version||catalog.configVersion!=definition.Version||catalog.schema!=definition.Schema||catalog.files==null||catalog.files.Length!=definition.Tables.Count)throw new FormatException("catalog compatibility");
            var names=new HashSet<string>(StringComparer.Ordinal);var addresses=new HashSet<string>(StringComparer.Ordinal);long size=0;
            foreach(var file in catalog.files)
            {
                if(file==null||!definition.Tables.Contains(file.name)||!names.Add(file.name)||string.IsNullOrWhiteSpace(file.address)||file.address==definition.CatalogAddress||!addresses.Add(file.address)||file.size<0||file.sha256==null||file.sha256.Length!=64||file.sha256.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new FormatException("catalog file entry");
                size+=file.size;if(size>definition.MaxBytes)throw new FormatException("catalog total limit");
            }
        }
        void Finish(Result result){State=stopped?ConfigState.Closed:result.IsSuccess?ConfigState.Ready:ConfigState.Failed;load.TrySetResult(result);}
        public UniTask<Result> StopAsync(CleanupContext context){Main();stopped=true;value=null;State=ConfigState.Closed;if(!cleaned)cancellation.Cancel();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext context)
        {
            Main();await StopAsync(context);if(load!=null)await load.Task;
            var retry=cleanupAttempted;cleanupAttempted=true;
            var report=retry?await resources.RetryFailedAsync(cleanup):await resources.CloseAsync(cleanup);
            while(report.PendingCount>0){await UniTask.Yield();report=resources.Snapshot;}
            if(!report.IsFullyDrained)return Fail(ErrorKind.CleanupFailed,"config.cleanup.retained");
            if(!cleaned){cleaned=true;cancellation.Dispose();}return Result.Success();
        }
        public static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static Result? Interrupted(OperationContext wait,long start)
        {if(wait.Cancellation.IsCancellationRequested)return Fail(ErrorKind.Canceled,"config.wait.canceled");if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Fail(ErrorKind.Timeout,"config.wait.timeout");return null;}
        static async UniTask<Result> Observe(UniTask<Result> task,OperationContext wait,long start)
        {while(true){var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;if(task.Status!=UniTaskStatus.Pending)return await task;await UniTask.Yield();}}
        static Result Fail(ErrorKind kind,string code,string message="")=>Result.Failure(new FrameworkError(kind,code,message));
    }
}
