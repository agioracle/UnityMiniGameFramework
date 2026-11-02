#if UNITY_EDITOR || !UNITY_WEBGL
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MiniFramework.Contracts;

namespace MiniFramework.Network.Unity
{
    /// <summary>Exclusive application-owned directory. Local disk backend; never substitutes for a mini-game SDK.</summary>
    public sealed class LocalHttpDownloadStore:IHttpDownloadStore
    {
        readonly string root;readonly long quota;long reserved;
        readonly HashSet<string> stages=new HashSet<string>(StringComparer.Ordinal);
        public LocalHttpDownloadStore(string root,long maxStoredBytes)
        {if(string.IsNullOrWhiteSpace(root)||maxStoredBytes<1)throw new ArgumentException();this.root=Path.GetFullPath(root);quota=maxStoredBytes;}
        public string GetPublishedPath(string targetId)
        {if(!StorageIdentifier.IsValid(targetId))throw new ArgumentException();return Path.Combine(root,targetId+".download");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,"network.download.local."+code);
        public Result<IHttpDownloadFile> Create(HttpDownloadRequest request)
        {
            if(request==null)return Result<IHttpDownloadFile>.Failure(Error("argument",ErrorKind.InvalidArgument));
            try
            {
                Directory.CreateDirectory(root);
                // Count final files; reservations cover active stages, including zero-byte stages.
                long used=0;foreach(var path in Directory.EnumerateFiles(root))if(!stages.Contains(path)){var size=new FileInfo(path).Length;if(size>quota-used)return Result<IHttpDownloadFile>.Failure(Error("quota"));used+=size;}
                if(reserved>quota-used||request.ExpectedBytes>quota-used-reserved)return Result<IHttpDownloadFile>.Failure(Error("quota"));
                var target=GetPublishedPath(request.TargetId);if(File.Exists(target))return Result<IHttpDownloadFile>.Failure(Error("exists",ErrorKind.InvalidState));
                var stage=Path.Combine(root,Guid.NewGuid().ToString("N")+".part");stages.Add(stage);reserved+=request.ExpectedBytes;
                return Result<IHttpDownloadFile>.Success(new DownloadFile(this,stage,target,request.ExpectedBytes));
            }
            catch(Exception){return Result<IHttpDownloadFile>.Failure(Error("create",ErrorKind.Provider));}
        }
        sealed class DownloadFile:IHttpDownloadFile
        {
            readonly LocalHttpDownloadStore owner;readonly string stage,target;readonly long expected;
            FileStream stream;long count;bool sealedFile,published,cleaned,released;
            public DownloadFile(LocalHttpDownloadStore owner,string stage,string target,long expected){this.owner=owner;this.stage=stage;this.target=target;this.expected=expected;}
            void Open(){if(stream==null)stream=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096);}
            public Result Append(byte[] buffer,int length)
            {
                if(cleaned||sealedFile||buffer==null||length<0||length>buffer.Length||length>expected-count)return Result.Failure(Error("write_state",ErrorKind.InvalidState));
                try{Open();stream.Write(buffer,0,length);count+=length;return Result.Success();}
                catch(Exception){return Result.Failure(Error("write",ErrorKind.Provider));}
            }
            public Result Seal()
            {
                if(cleaned||sealedFile||count!=expected)return Result.Failure(Error("seal_state",ErrorKind.Integrity));
                try{Open();stream.Flush();stream.Dispose();stream=null;sealedFile=true;return Result.Success();}
                catch(Exception){return Result.Failure(Error("seal",ErrorKind.Provider));}
            }
            public Result Publish()
            {
                if(cleaned||!sealedFile)return Result.Failure(Error("publish_state",ErrorKind.InvalidState));
                if(published)return Result.Success();
                try{File.Move(stage,target);published=true;ReleaseReservation();return Result.Success();}
                catch(Exception){return Result.Failure(Error("publish",ErrorKind.Provider));}
            }
            void ReleaseReservation(){if(!released){owner.reserved-=expected;owner.stages.Remove(stage);released=true;}}
            public Result Cleanup()
            {
                if(cleaned)return Result.Success();
                try{stream?.Dispose();stream=null;if(!published&&File.Exists(stage))File.Delete(stage);ReleaseReservation();cleaned=true;return Result.Success();}
                catch(Exception){return Result.Failure(Error("cleanup",ErrorKind.CleanupFailed));}
            }
        }
    }
}
#endif
