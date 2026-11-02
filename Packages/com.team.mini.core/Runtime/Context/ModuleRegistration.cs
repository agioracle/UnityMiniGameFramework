using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class ModuleRegistration
    {
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public IReadOnlyList<Type> Services { get; }
        internal Func<IModule> Factory { get; }
        public ModuleRegistration(string id, IEnumerable<string> dependencies, Func<IModule> factory, params Type[] services)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module id required.", nameof(id));
            Id = id;
            Dependencies = Array.AsReadOnly((dependencies ?? throw new ArgumentNullException(nameof(dependencies))).ToArray());
            Services = Array.AsReadOnly((services ?? throw new ArgumentNullException(nameof(services))).ToArray());
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }
}
