using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
namespace MiniFramework.Providers.StorageEditor
{
    public enum EditorStorageFault { None, BeforeReplace, AfterReplace }
    // Desktop Editor only. Sync bounded I/O is deliberate here; portable runtime never uses files.
    public sealed class EditorStorageProvider:IStorageProvider
    {
        public string Id=>"editor-storage";public StorageCapabilities Capabilities {get;}
        public string Root {get;}public long QuotaBytes {get;}
        public EditorStorageFault NextFault {get;set;}
        readonly StorageLimits limits;
        public EditorStorageProvider(string root,int maxContainerBytes=1048576,long quotaBytes=4194304)
        {
            if(string.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root)||quotaBytes<0)throw new ArgumentException("Explicit absolute private storage root required.");
            Root=Path.GetFullPath(root);QuotaBytes=quotaBytes;limits=new StorageLimits(1024,maxContainerBytes,maxContainerBytes);
            Capabilities=new StorageCapabilities(maxContainerBytes,StoragePersistence.RestartRecoverable,true);
        }
        static string Key(string value)=>string.Concat(Encoding.ASCII.GetBytes(value).Select(b=>b.ToString("x2")));
        public string PathFor(StorageAddress address){if(!address.IsValid)throw new ArgumentException();return Path.Combine(Root,Key(address.Namespace),Key(address.Slot)+".save");}
        public IStorageSession Create(StorageAddress address)=>new Session(this,address,PathFor(address));
        byte[] Read(string path)
        {
            if(!File.Exists(path))return null;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                if(stream.Length>limits.MaxContainerBytes)throw new InvalidDataException("Container exceeds limit.");
                var bytes=new byte[(int)stream.Length];var offset=0;
                while(offset<bytes.Length){var read=stream.Read(bytes,offset,bytes.Length-offset);if(read==0)throw new EndOfStreamException();offset+=read;}
                return bytes;
            }
        }
        StorageCommitReceipt Commit(StorageWriteRequest request,StorageAddress address,string path)
        {
            string temporary=null;FileStream gate=null;var lockPath=Path.Combine(Root,".storage.lock");bool replacing=false;
            try
            {
                Directory.CreateDirectory(Root);
                try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
                catch(IOException){return Rejected("editor.storage.busy");}
                var old=Read(path);var valid=StorageWriteValidation.Check(request,address,old,limits);if(!valid.IsSuccess)return StorageCommitReceipt.Rejected(valid.Error);
                long used=0;foreach(var name in Directory.EnumerateFiles(Root,"*.save",SearchOption.AllDirectories))used=checked(used+new FileInfo(name).Length);
                if(used-(old?.Length??0)+request.ByteCount>QuotaBytes)return Rejected("editor.storage.quota");
                var fault=NextFault;NextFault=EditorStorageFault.None;
                Directory.CreateDirectory(Path.GetDirectoryName(path));temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {var bytes=request.CopyBytes();stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(fault==EditorStorageFault.BeforeReplace)throw new IOException("Injected before replacement.");
                // From this point, any exceptional outcome is indeterminate. Never claim old bytes survived.
                replacing=true;
                if(old==null)File.Move(temporary,path);else File.Replace(temporary,path,null);
                temporary=null;
                if(fault==EditorStorageFault.AfterReplace)throw new IOException("Injected after replacement.");
                return StorageCommitReceipt.Committed(request.Sequence,request.Digest,Capabilities.Persistence);
            }
            catch(Exception e) when(e is InvalidDataException||e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException||e is OverflowException)
            {return replacing?StorageCommitReceipt.Indeterminate(Error("editor.storage.replace_unknown")):Rejected(e is InvalidDataException?"editor.storage.corrupt":"editor.storage.io");}
            finally
            {
                // Only delete files created by this operation; a foreign/stale lock is never removed.
                if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
                if(gate!=null){gate.Dispose();try{File.Delete(lockPath);}catch(IOException){}catch(UnauthorizedAccessException){}}
            }
        }
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Storage)=>new FrameworkError(kind,code);
        static StorageCommitReceipt Rejected(string code)=>StorageCommitReceipt.Rejected(Error(code));
        sealed class Session:IStorageSession
        {
            readonly EditorStorageProvider owner;readonly StorageAddress address;readonly string path;bool closed;
            internal Session(EditorStorageProvider owner,StorageAddress address,string path){this.owner=owner;this.address=address;this.path=path;}
            public UniTask<Result<StorageReadResult>> ReadAsync()
            {
                if(closed)return UniTask.FromResult(Result<StorageReadResult>.Failure(Error("editor.storage.closed",ErrorKind.InvalidState)));
                try{var bytes=owner.Read(path);return UniTask.FromResult(Result<StorageReadResult>.Success(new StorageReadResult(bytes!=null,bytes)));}
                catch(Exception e) when(e is InvalidDataException||e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException)
                {return UniTask.FromResult(Result<StorageReadResult>.Failure(Error(e is InvalidDataException?"editor.storage.size":"editor.storage.read")));}
            }
            public UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request)=>UniTask.FromResult(closed?Rejected("editor.storage.closed"):owner.Commit(request,address,path));
            public UniTask<Result> CleanupAsync(CleanupContext cleanup){closed=true;return UniTask.FromResult(Result.Success());}
        }
    }
}
