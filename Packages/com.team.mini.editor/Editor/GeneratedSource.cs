using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
namespace MiniFramework.EditorFoundation
{
    public static class GeneratedSource
    {
        private static readonly HashSet<string> Keywords=new HashSet<string>(("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias ascending async await by descending dynamic equals from get global group into join let nameof notnull on orderby partial remove select set unmanaged value var when where yield record init required file").Split(' '));
        public static bool Identifier(string text)=>text!=null&&Regex.IsMatch(text,@"^[A-Za-z_][A-Za-z0-9_]*\z")&&!Keywords.Contains(text);
        public static bool Namespace(string text)=>!string.IsNullOrEmpty(text)&&Array.TrueForAll(text.Split('.'),Identifier);
        public static bool Write(string path,string content,string marker)
        {
            if(File.Exists(path))
            {
                var old=File.ReadAllText(path);if(old==content)return false;
                if(!old.StartsWith(marker+"\n",StringComparison.Ordinal))throw new InvalidOperationException("ui.generate.foreign_file: "+path);
                if(!File.Exists(path+".sha256")||File.ReadAllText(path+".sha256")!=Hash(old))throw new InvalidOperationException("ui.generate.edited_file: "+path);
            }
            var temporary=path+".tmp";
            try{File.WriteAllText(temporary,content,new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);File.WriteAllText(path+".sha256",Hash(content));}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return true;
        }
        public static string Hash(string text){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
