using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mimic
{
    public static class PathUtils
    {
        const string MESSAGE_DIRECTORYNOTFOUND = "Directory Not Found: ";
        const string MESSAGE_FILENOTFOUND = "File Not Found: ";

        public static void AssertExists(string path)
        {
            var attributes = File.GetAttributes(path);
            if(attributes.HasFlag(FileAttributes.Directory)) {
                Debug.Assert(Directory.Exists(path), MESSAGE_DIRECTORYNOTFOUND + path);
            } else {
                Debug.Assert(File.Exists(path), MESSAGE_FILENOTFOUND + path);
            }      
        }
        public static void AssertExists(IEnumerable<string> paths)
        {
            foreach(var path in paths) {
                AssertExists(path);
            }
        }

        public static void CopyDirectoryRecursive(string fullSrcPath, string fullDestinationPath)
        {
            DirectoryInfo srcDir = new DirectoryInfo(fullSrcPath);
            if (!srcDir.Exists) {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + fullSrcPath);
            }

            // Create destination directory structure
            if (!Directory.Exists(fullDestinationPath)) {
                Directory.CreateDirectory(fullDestinationPath);
            }

            // Copy all files
            FileInfo[] srcFiles = srcDir.GetFiles();
            foreach (FileInfo file in srcFiles) {
                string temppath = Path.Combine(fullDestinationPath, file.Name);
                file.CopyTo(temppath, true);
            }

            // Copy all directories
            DirectoryInfo[] srcDirs = srcDir.GetDirectories();
            foreach (DirectoryInfo subdir in srcDirs) {
                string temppath = Path.Combine(fullDestinationPath, subdir.Name);
                CopyDirectoryRecursive(subdir.FullName, temppath);
            }
        }
        public static void CopyPathRecursiveAndOverwrite(string fullSrcPath, string fullDestinationPath)
        {
            // Is File?
            if (File.Exists(fullSrcPath))
            {
                // Create directory if doesn't exist
                FileInfo fileInfo = new FileInfo(fullDestinationPath);
                         fileInfo.Directory.Create(); 
                File.Copy(fullSrcPath, fullDestinationPath, true);
            } else {
                CopyDirectoryRecursive(fullSrcPath, fullDestinationPath);
            }
        }
        public static void DeletePath(string fullPath)
        {
            if(File.Exists(fullPath)) {
                File.Delete(fullPath);
            } else if(Directory.Exists(fullPath)) {
                Directory.Delete(fullPath);
            }
        }
        public static void RenamePath(string fullOldPath, string fullNewPath)
        {
            if(File.Exists(fullOldPath)) {
                File.Move(fullOldPath, fullNewPath);
            } else if(Directory.Exists(fullOldPath)) {
                Directory.Move(fullOldPath, fullNewPath);
            }
        }

        public static string PathsToString(IEnumerable<string> paths)
        {
            return paths.Aggregate("\t", (accum, path) => { return accum + path.Trim() + "\n\t"; });
        }
    }
}
