using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // Explicit, instance-owned sampling. No static registry, frame hook or hidden source discovery.
    public sealed class DiagnosticSession : IDisposable
    {
        private readonly Dictionary<string,IDiagnosticSource> sources=new Dictionary<string,IDiagnosticSource>();
        public bool Enabled { get; set; }
        public void Add(string id,IDiagnosticSource source)
        {
            CleanupWait.MainThread();
            if(string.IsNullOrWhiteSpace(id)||source==null)throw new ArgumentException("Diagnostic source required.");
            foreach(var item in sources.Values)if(ReferenceEquals(item,source))throw new ArgumentException("Source already registered.");
            sources.Add(id,source);
        }
        public bool Remove(string id){CleanupWait.MainThread();return sources.Remove(id);}
        public DiagnosticCounters Capture()
        {
            CleanupWait.MainThread();var counters=new DiagnosticCounters();if(!Enabled)return counters;
            foreach(var source in sources.Values)counters.Add(source.ReadDiagnostics());
            return counters;
        }
        public void Dispose(){CleanupWait.MainThread();Enabled=false;sources.Clear();}
    }
}
