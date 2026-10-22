using System;
using System.IO;

namespace MiniGame.Framework.Editor
{
    public static class BuildPathGuard
    {
        public static void RequireChild(string root, string destination)
        {
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(destination);
            if (!path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Destination must be a child of the declared root.", nameof(destination));
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Build paths cannot traverse symbolic links.");
            }
        }
    }
}
