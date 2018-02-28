using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Mimic
{
    public struct FileWatcherConfiguration
    {
        public string watchPath;
        public string destinationPath;
        public int fileChangeIgnoreTimeoutMs;

        public FileWatcherConfiguration(string watchPath, string destinationPath, int fileChangeIgnoreTimeoutMs = 1000)
        {
            this.watchPath = watchPath.Trim();
            this.destinationPath = destinationPath.Trim();
            this.fileChangeIgnoreTimeoutMs = fileChangeIgnoreTimeoutMs;
        }
    }

    /// <summary>
    /// Handles copying files from here to there, and mirroring changes. 
    /// </summary>
    internal class FileWatchAndCopy
    {
        ConcurrentDictionary<string, DateTime> m_fileWriteTimes = new ConcurrentDictionary<string, DateTime>();
        ConcurrentQueue<FileSystemEventArgs> m_fileEvents = new ConcurrentQueue<FileSystemEventArgs>();

        Timer m_clearFileWriteTimes;
        FileWatcherConfiguration m_config;
        FileSystemWatcher m_watcher;

        public FileWatchAndCopy(FileWatcherConfiguration config)
        {
            m_config = config;
            m_clearFileWriteTimes = new Timer(OnClearTimer);

            // NOTE: Events are called on a separate Thread 
            var watcher = m_watcher = new FileSystemWatcher();
            watcher.Path = config.watchPath;
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            watcher.Changed += new FileSystemEventHandler(OnFileEvent);
            watcher.Created += new FileSystemEventHandler(OnFileEvent);
            watcher.Deleted += new FileSystemEventHandler(OnFileEvent);
            watcher.Renamed += new RenamedEventHandler(OnFileEvent);
            watcher.Error += new ErrorEventHandler(OnError);

            // Begin watching.
            watcher.EnableRaisingEvents = true;

            RunEventConsumerThread();

            ConsoleWriter.WriteLine("FileWatchAndCopy() Started.\nMapping Src:\t" + config.watchPath + "\nTo:\t" + config.destinationPath + "\n");
        }

        void OnError(object source, ErrorEventArgs e)
        {
            if (e.GetException().GetType() == typeof(InternalBufferOverflowException)) {
                ConsoleWriter.WriteLine("Error: File System Watcher internal buffer overflow at " + DateTime.Now + "\r\n", ConsoleColor.Red);
            } else {
                ConsoleWriter.WriteLine("Error: Watched directory not accessible at " + DateTime.Now + "\r\n", ConsoleColor.Red);
            }
        }

        void OnFileEvent(object source, FileSystemEventArgs e)
        {
            // FileSystemWatcher event buffer size is limited. We queue this for handling later to avoid missed events.
            m_fileEvents.Enqueue(e);
        }

        void RunEventConsumerThread()
        {
            Thread t = new Thread(MonitorEvents);
            t.Start();
        }
        void MonitorEvents()
        {

        }


        void OnChanged(object source, FileSystemEventArgs e)
        {
            ResetTimer();

            string path = e.FullPath;
            // NOTE: FileSystemWatcher often fires two 'Changed' events per file, 
            //       so we ignore those close together
            if (WasRecentlyWritten(path)) {
                return;
            }
            // Changed fires for directories when a child file changes, 
            // so we ignore directories.
            if (Directory.Exists(path)) {
                return;
            }

            ConsoleWriter.WriteLine("\nChange()");
            CopyPathToDestination(path);
        }
        void OnCreated(object source, FileSystemEventArgs e)
        {
            ConsoleWriter.WriteLine("\nCreate()");
            string path = e.FullPath;
            CopyPathToDestination(path);
        }
        void OnDeleted(object source, FileSystemEventArgs e)
        {
            ConsoleWriter.WriteLine("\nDelete()");
            string path = e.FullPath;
            DeletePathAtDestination(path);
        }
        void OnRenamed(object source, RenamedEventArgs e)
        {
            ConsoleWriter.WriteLine("\nRename()");
            RenamePathAtDestination(e.OldFullPath, e.FullPath);
        }

        void ResetWatcher()
        {
            m_watcher.EnableRaisingEvents = false;
            int maxAttempts = 120;
            int timeOut = 30000;
            int i = 0;
            while (m_watcher.EnableRaisingEvents == false && i < maxAttempts)
            {
                i += 1;
                try {
                    m_watcher.EnableRaisingEvents = true;
                } catch {
                    m_watcher.EnableRaisingEvents = false;
                    Thread.Sleep(timeOut);
                }
            }
        }
        void ResetTimer()
        {
            m_clearFileWriteTimes.Change(m_config.fileChangeIgnoreTimeoutMs, Timeout.Infinite);
        }
        /// <summary>
        /// Clears the File Write Times mapping, to keep memory footprint low. 
        /// </summary>
        void OnClearTimer(object state)
        {
            m_fileWriteTimes.Clear();
            ConsoleWriter.WriteLine("\nCleared file 'change' ignore time stamps.\n", ConsoleColor.Blue);
        }
        bool WasRecentlyWritten(string fullPath)
        {
            DateTime currentWriteTime = File.GetLastWriteTime(fullPath);
            if(m_fileWriteTimes.TryGetValue(fullPath, out DateTime lastWriteTime)) {
                return true;
            }

            m_fileWriteTimes[fullPath] = currentWriteTime;
            return false;
        }


        string MapDestinationPath(string watchedPath)
        {
            var path = watchedPath.Replace(m_config.watchPath, string.Empty);
                path = path.TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(m_config.destinationPath, path);
        }

        void CopyPathToDestination(string fullSrcPath)
        {
            try {
                var fullDestPath = MapDestinationPath(fullSrcPath);
                PathUtils.CopyPathRecursiveAndOverwrite(fullSrcPath, fullDestPath);
                ConsoleWriter.WriteLine("Path Copied to: " + fullDestPath);
            } catch(Exception e) {
                ConsoleWriter.WriteLine("CopyPathToDestination() An Exception occurred:\n" + e.StackTrace, ConsoleColor.Red);
            }
        }

        void DeletePathAtDestination(string fullSrcPath)
        {
            try {
                var fullDestPath = MapDestinationPath(fullSrcPath);
                PathUtils.DeletePath(fullDestPath);
                ConsoleWriter.WriteLine("Path Deleted at: " + fullDestPath);
            } catch(Exception e) {
                ConsoleWriter.WriteLine("DeletePathAtDestination() An Exception occurred:\n" + e.StackTrace, ConsoleColor.Red);
            }
        }

        void RenamePathAtDestination(string oldFullSrcPath, string newFullSrcPath)
        {
            try {
                var oldFullDestPath = MapDestinationPath(oldFullSrcPath);
                var newFullDestPath = MapDestinationPath(newFullSrcPath);
                PathUtils.RenamePath(oldFullDestPath, newFullDestPath);
                ConsoleWriter.WriteLine("Path Renamed From: " + oldFullDestPath + "\nTo: " + newFullDestPath);
            }
            catch (Exception e) {
                ConsoleWriter.WriteLine("RenamePathAtDestination() An Exception occurred:\n" + e.StackTrace, ConsoleColor.Red);
            }
        }


    }
}