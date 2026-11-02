using System;
using MiniFramework.Contracts;
namespace MiniFramework.UI
{
    [AttributeUsage(AttributeTargets.Field, Inherited=false)]
    public sealed class UiBindingPathAttribute:Attribute
    {
        public string Path {get;}
        public UiBindingPathAttribute(string path){Path=path;}
    }
    public interface IUiGeneratedBindings { Result ValidateGeneratedBindings(); }
}
