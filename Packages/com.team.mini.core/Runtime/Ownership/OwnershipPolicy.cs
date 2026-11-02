using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MiniFramework.Contracts;
namespace MiniFramework.Ownership
{
    public sealed class OwnershipPolicy
    {
        public ClaimOwnerMode Mode {get;} public string LocalOwnerId {get;} public string LocalNamespace {get;} public bool AllowSimulatedIdentity {get;}
        OwnershipPolicy(ClaimOwnerMode mode,string owner,string ns,bool simulated){Mode=mode;LocalOwnerId=owner;LocalNamespace=ns;AllowSimulatedIdentity=simulated;}
        public static OwnershipPolicy LocalOnly(string installationOwnerId,string storageNamespace)
        {
            if(!ClaimOwnerSnapshot.IsToken(installationOwnerId)||!StorageIdentifier.IsValid(storageNamespace)||!(storageNamespace=="local"||storageNamespace.StartsWith("local.",StringComparison.Ordinal)))throw new ArgumentException("Explicit stable installation owner and local namespace required.");
            return new OwnershipPolicy(ClaimOwnerMode.LocalOnly,installationOwnerId,storageNamespace,false);
        }
        public static OwnershipPolicy AccountScoped(bool allowSimulatedIdentity=false)=>new OwnershipPolicy(ClaimOwnerMode.AccountScoped,null,null,allowSimulatedIdentity);
        public Result ValidateComposition(bool accountModuleEnabled,bool localOwnerModuleEnabled)
        {
            var valid=Mode==ClaimOwnerMode.LocalOnly?!accountModuleEnabled&&localOwnerModuleEnabled:accountModuleEnabled&&!localOwnerModuleEnabled;
            return valid?Result.Success():Result.Failure(new FrameworkError(ErrorKind.Dependency,"owner.composition.mode"));
        }
        public Result<string> NamespaceFor(BusinessIdentity identity)
        {
            if(Mode!=ClaimOwnerMode.AccountScoped||identity==null)return Result<string>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"owner.identity.required"));
            if(identity.Mode==IdentityMode.Simulated&&!AllowSimulatedIdentity)return Result<string>.Failure(new FrameworkError(ErrorKind.PermissionDenied,"owner.simulated.disabled"));
            // Full SHA-256 in Base32: stable portable key, issuer separated. This is not encryption.
            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Encoding.UTF8.GetBytes(identity.Issuer+"\n"+identity.OwnerId));
            const string alphabet="abcdefghijklmnopqrstuvwxyz234567";var text=new StringBuilder(52);int bits=0,buffer=0;
            foreach(var b in hash){buffer=(buffer<<8)|b;bits+=8;while(bits>=5){bits-=5;text.Append(alphabet[(buffer>>bits)&31]);}}
            if(bits>0)text.Append(alphabet[(buffer<<(5-bits))&31]);return Result<string>.Success("account."+text);
        }
    }
    // Caller-owned process sequence; retain across Context rebuilds. Persistence must be supplied separately.
    public sealed class OwnerGenerationSequence:IOwnerGenerationSource
    {
        long last;public OwnerGenerationSequence(long lastIssued=0){if(lastIssued<0)throw new ArgumentOutOfRangeException();last=lastIssued;}
        public Result<long> Reserve()
        {
            while(true){var value=Interlocked.Read(ref last);if(value==long.MaxValue)return Result<long>.Failure(new FrameworkError(ErrorKind.Integrity,"owner.generation.exhausted"));if(Interlocked.CompareExchange(ref last,value+1,value)==value)return Result<long>.Success(value+1);}
        }
    }
}
