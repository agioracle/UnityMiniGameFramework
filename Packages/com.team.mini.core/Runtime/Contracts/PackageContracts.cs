using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum PackageState { Uninitialized, Initializing, Ready, Failed }
    public enum PackageSource { BuiltIn, Remote, CompatibleCache }
    // Ordered, immutable, version-specific candidates. No mixing files across candidates.
    public sealed class PackageCandidate
    {
        public PackageSource Source {get;}
        public string Version {get;}
        public string Location {get;}
        public PackageCandidate(PackageSource source,string version,string location)
        {
            if(source<PackageSource.BuiltIn||source>PackageSource.CompatibleCache||string.IsNullOrWhiteSpace(version)||string.IsNullOrWhiteSpace(location))throw new ArgumentException("Candidate requires source, version and location.");
            Source=source;Version=version;Location=location;
        }
    }
    public sealed class PackageProfile
    {
        public string Package {get;} public string ProgramVersion {get;} public string Schema {get;}
        public IReadOnlyList<string> FirstPlay {get;}
        public IReadOnlyList<PackageCandidate> Candidates {get;}
        public PackageProfile(string package,string programVersion,string schema,IEnumerable<string> firstPlay,IEnumerable<PackageCandidate> candidates)
        {
            if(string.IsNullOrWhiteSpace(package)||string.IsNullOrWhiteSpace(programVersion)||string.IsNullOrWhiteSpace(schema)||firstPlay==null||candidates==null)throw new ArgumentException("Package compatibility identity required.");
            var required=new List<string>(firstPlay);var choices=new List<PackageCandidate>(candidates);
            if(required.Count==0||required.Exists(string.IsNullOrWhiteSpace)||new HashSet<string>(required,StringComparer.Ordinal).Count!=required.Count||choices.Count==0||choices.Exists(x=>x==null))throw new ArgumentException("Nonempty unique first-play set and candidates required.");
            Package=package;ProgramVersion=programVersion;Schema=schema;FirstPlay=required.AsReadOnly();Candidates=choices.AsReadOnly();
        }
    }
    // Provider attests integrity and local availability after actual initialization/download completion.
    // CompatiblePrograms is exact allow-list, not lexical version comparison.
    public sealed class PackageManifest
    {
        public string Package {get;} public string ContentVersion {get;} public string ConfigVersion {get;} public string Schema {get;}
        public bool IntegrityVerified {get;}
        public IReadOnlyList<string> CompatiblePrograms {get;} public IReadOnlyList<string> AvailableFirstPlay {get;}
        public PackageManifest(string package,string version,string configVersion,string schema,bool integrity,IEnumerable<string> programs,IEnumerable<string> available)
        {Package=package;ContentVersion=version;ConfigVersion=configVersion;Schema=schema;IntegrityVerified=integrity;CompatiblePrograms=new List<string>(programs??Array.Empty<string>()).AsReadOnly();AvailableFirstPlay=new List<string>(available??Array.Empty<string>()).AsReadOnly();}
    }
    public interface IPackageReadiness
    {
        string Package {get;} PackageState State {get;} string ContentVersion {get;}
        Result CheckReady(string package,string contentVersion);
    }
    public interface IPackageBackend
    {
        // Create must be cold; responsibility transfers before Prepare starts or can throw.
        IPackageSession Create(PackageProfile profile,PackageCandidate candidate);
    }
    public interface IPackageSession
    {
        // Actual completion, never a canceled observer. Failure may still retain partial state.
        UniTask<Result<PackageManifest>> PrepareAsync();
        // Actual release confirmation; repeat only by explicit retry after failure.
        UniTask<Result> CleanupAsync();
    }
}
