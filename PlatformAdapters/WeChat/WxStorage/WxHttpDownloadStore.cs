using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.WxSdk;
using WeChatWASM;

namespace MiniFramework.Providers.WeChat.WxStorage
{
    /// <summary>Dedicated exclusive download root, separate from transactional save slots. C# SDK only.</summary>
    public sealed class WxHttpDownloadStore:IHttpDownloadStore
    {
        internal interface IFiles
        {
            void EnsureRoot(string root);string[] List(string root);long? Size(string path);
            void Create(string path);void Append(string path,byte[] chunk);void Move(string source,string target);void Delete(string path);
        }
        readonly IFiles files;readonly string root;readonly long quota,maxFile;long reserved;
        readonly HashSet<string> stages=new HashSet<string>(StringComparer.Ordinal);
        internal WxHttpDownloadStore(IFiles files,string root,long maxStoredBytes,long maxFileBytes)
        {
            if(files==null||string.IsNullOrEmpty(root)||maxStoredBytes<1||maxFileBytes<1||maxFileBytes>int.MaxValue||maxFileBytes>maxStoredBytes)throw new ArgumentException();
            this.files=files;this.root=root;quota=maxStoredBytes;maxFile=maxFileBytes;
        }
        public static Result<IHttpDownloadStore> CreateNative(string rootId,long maxStoredBytes,long maxFileBytes)
        {
            if(!PlayerLoopHelper.IsMainThread||!StorageIdentifier.IsValid(rootId)||rootId.Contains(".")||maxStoredBytes<1||maxFileBytes<1||maxFileBytes>int.MaxValue||maxFileBytes>maxStoredBytes)
                return Result<IHttpDownloadStore>.Failure(Error("argument",ErrorKind.InvalidArgument));
#if UNITY_WEBGL && !UNITY_EDITOR
            var sdk=WxSdkApi.CreateNativeApi();if(!sdk.IsSuccess)return Result<IHttpDownloadStore>.Failure(sdk.Error);
            try
            {
                var path=WX.env.USER_DATA_PATH;
                if(!string.IsNullOrEmpty(path)&&path.Length<=1024&&path.IndexOf('\0')<0)
                    return Result<IHttpDownloadStore>.Success(new WxHttpDownloadStore(new SdkFiles(WX.GetFileSystemManager()),path.TrimEnd('/')+"/miniframework-download-"+rootId,maxStoredBytes,maxFileBytes));
            }
            catch(Exception) { }
#endif
            return Result<IHttpDownloadStore>.Failure(Error("native_host_required",ErrorKind.NotSupported));
        }
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,"wechat.download."+code);
        public string GetPublishedPath(string targetId)
        {if(!StorageIdentifier.IsValid(targetId))throw new ArgumentException(nameof(targetId));return root+"/"+targetId+".download";}
        public Result<IHttpDownloadFile> Create(HttpDownloadRequest request)
        {
            if(!PlayerLoopHelper.IsMainThread)return Result<IHttpDownloadFile>.Failure(Error("thread",ErrorKind.InvalidState));
            if(request==null||request.ExpectedBytes>maxFile)return Result<IHttpDownloadFile>.Failure(Error("file_budget",ErrorKind.InvalidArgument));
            try
            {
                files.EnsureRoot(root);long used=0;
                foreach(var name in files.List(root))
                {
                    // Never follow paths outside this root, including unexpected SDK listings.
                    if(string.IsNullOrEmpty(name)||name.Contains("/")||name.Contains("\\")||name=="."||name=="..")return Result<IHttpDownloadFile>.Failure(Error("listing",ErrorKind.Integrity));
                    var path=root+"/"+name;if(stages.Contains(path))continue;
                    var size=files.Size(path);if(!size.HasValue||size.Value<0||size.Value>quota-used)return Result<IHttpDownloadFile>.Failure(Error("quota"));used+=size.Value;
                }
                if(reserved>quota-used||request.ExpectedBytes>quota-used-reserved)return Result<IHttpDownloadFile>.Failure(Error("quota"));
                var target=GetPublishedPath(request.TargetId);if(files.Size(target).HasValue)return Result<IHttpDownloadFile>.Failure(Error("exists",ErrorKind.InvalidState));
                var stage=root+"/"+Guid.NewGuid().ToString("N")+".part";
                stages.Add(stage);reserved+=request.ExpectedBytes;
                return Result<IHttpDownloadFile>.Success(new DownloadFile(this,stage,target,request.ExpectedBytes,request.BufferBytes));
            }
            catch(Exception){return Result<IHttpDownloadFile>.Failure(Error("create",ErrorKind.Provider));}
        }
        sealed class DownloadFile:IHttpDownloadFile
        {
            readonly WxHttpDownloadStore owner;readonly string stage,target;readonly long expected;readonly int bufferBytes;
            long count;bool attempted,sealedFile,published,cleaned;
            public DownloadFile(WxHttpDownloadStore owner,string stage,string target,long expected,int bufferBytes)
            {this.owner=owner;this.stage=stage;this.target=target;this.expected=expected;this.bufferBytes=bufferBytes;}
            void Open(){if(!attempted){attempted=true;owner.files.Create(stage);}}
            public Result Append(byte[] buffer,int length)
            {
                if(!PlayerLoopHelper.IsMainThread||cleaned||sealedFile||buffer==null||length<0||length>buffer.Length||length>bufferBytes||length>expected-count)return Result.Failure(Error("write_state",ErrorKind.InvalidState));
                try{Open();var chunk=new byte[length];Buffer.BlockCopy(buffer,0,chunk,0,length);owner.files.Append(stage,chunk);count+=length;return Result.Success();}
                catch(Exception){return Result.Failure(Error("write",ErrorKind.Provider));}
            }
            public Result Seal()
            {
                if(!PlayerLoopHelper.IsMainThread||cleaned||sealedFile||count!=expected)return Result.Failure(Error("seal_state",ErrorKind.Integrity));
                try{Open();if(owner.files.Size(stage)!=expected)return Result.Failure(Error("length",ErrorKind.Integrity));sealedFile=true;return Result.Success();}
                catch(Exception){return Result.Failure(Error("seal",ErrorKind.Provider));}
            }
            public Result Publish()
            {
                if(!PlayerLoopHelper.IsMainThread||cleaned||!sealedFile)return Result.Failure(Error("publish_state",ErrorKind.InvalidState));
                if(published)return Result.Success();
                try
                {
                    if(owner.files.Size(target).HasValue)return Result.Failure(Error("exists",ErrorKind.InvalidState));
                    owner.files.Move(stage,target);
                    // SDK RenameSync returns void. Observe the outcome before reporting publication.
                    if(owner.files.Size(stage).HasValue||owner.files.Size(target)!=expected)return Result.Failure(Error("publish_unknown",ErrorKind.Provider));
                    published=true;ReleaseReservation();return Result.Success();
                }
                catch(Exception){return Result.Failure(Error("publish_unknown",ErrorKind.Provider));}
            }
            void ReleaseReservation(){if(owner.stages.Remove(stage))owner.reserved-=expected;}
            public Result Cleanup()
            {
                if(!PlayerLoopHelper.IsMainThread)return Result.Failure(Error("cleanup_thread",ErrorKind.CleanupFailed));
                if(cleaned)return Result.Success();
                try{if(!published&&attempted)owner.files.Delete(stage);ReleaseReservation();cleaned=true;return Result.Success();}
                catch(Exception){return Result.Failure(Error("cleanup",ErrorKind.CleanupFailed));}
            }
        }
        internal static long ReadRecursiveFileSize(WXStat[] rows,string fileName)
        {
            if(rows==null||string.IsNullOrEmpty(fileName)||fileName=="."||fileName==".."||fileName.Contains("/")||fileName.Contains("\\"))throw new InvalidOperationException();
            var expected="/"+fileName;Stats found=null;
            foreach(var row in rows)
            {
                if(row==null)throw new InvalidOperationException();
                if(!string.Equals(row.path,expected,StringComparison.Ordinal))continue;
                if(found!=null||row.stats==null||!row.stats.isFile()||row.stats.size<0)throw new InvalidOperationException();
                found=row.stats;
            }
            if(found==null)throw new InvalidOperationException();
            return found.size;
        }
        sealed class SdkFiles:IFiles
        {
            readonly WXFileSystemManager fs;
            public SdkFiles(WXFileSystemManager fs){this.fs=fs;}
            bool Exists(string path)
            {var r=fs.AccessSync(path);if(r=="access:ok")return true;if(WxStorageFiles.Classify(r)==-1)return false;throw new InvalidOperationException();}
            public void EnsureRoot(string root){if(!Exists(root)&&fs.MkdirSync(root,false)!="mkdir:ok")throw new InvalidOperationException();}
            public string[] List(string root)=>fs.ReaddirSync(root);
            public long? Size(string path)
            {
                if(!Exists(path))return null;
                // The pinned SDK's single-file StatSync wraps a raw Stats object as WXStat,
                // leaving stats null. Recursive directory results preserve path + stats.
                // Match the exact relative entry; do not open FDs or follow returned paths.
                var slash=path.LastIndexOf('/');if(slash<1)throw new InvalidOperationException();
                var rows=fs.StatSync(path.Substring(0,slash),true);
                return ReadRecursiveFileSize(rows,path.Substring(path.LastIndexOf('/')+1));
            }
            public void Create(string path){if(Exists(path)||fs.WriteFileSync(path,Array.Empty<byte>(),null)!="ok")throw new InvalidOperationException();}
            public void Append(string path,byte[] bytes)=>fs.AppendFileSync(path,bytes,null);
            public void Move(string source,string target)=>fs.RenameSync(source,target);
            public void Delete(string path){if(Exists(path)&&fs.UnlinkSync(path)!="unlink:ok")throw new InvalidOperationException();}
        }
    }
}
