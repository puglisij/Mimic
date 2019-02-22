using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Globbing;

namespace Mimic
{
    public struct FileWatcherConfiguration
    {
        public string watchPath;
        public string destinationPath;
        public Glob[] excludedPathsGlob;

        public FileWatcherConfiguration(string watchPath, string destinationPath, string[] excludedPathsGlob)
        {
            this.watchPath = watchPath.Trim();
            this.destinationPath = destinationPath.Trim();
            this.excludedPathsGlob = new Glob[excludedPathsGlob.Length];

            for(int i = 0; i < excludedPathsGlob.Length; ++i)
            {
                GlobOptions options = new GlobOptions();
                            options.Evaluation.CaseInsensitive = true;
                this.excludedPathsGlob[i] = Glob.Parse(excludedPathsGlob[i], options);
            }
        }
    }

    /// <summary>
    /// Handles copying files from here to there, and mirroring changes. 
    /// </summary>
    internal class FileMimic : IDisposable
    {
        ConcurrentQueue<FileSystemEventArgs> fileEvents = new ConcurrentQueue<FileSystemEventArgs>();

        FileWatcherConfiguration config;
        FileSystemWatcher watcher;

        bool keepConsuming = true;
        Thread fileEventConsumer;

        public FileMimic(FileWatcherConfiguration config)
        {
            this.config = config;

            // NOTE: Events are called on a separate Thread 
            var watcher = this.watcher = new FileSystemWatcher();
            watcher.Path = config.watchPath;
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            watcher.Changed += new FileSystemEventHandler(OnFileEvent);
            watcher.Created += new FileSystemEventHandler(OnFileEvent);
            watcher.Deleted += new FileSystemEventHandler(OnFileEvent);
            watcher.Renamed += new RenamedEventHandler(OnFileEvent);
            watcher.Error += new ErrorEventHandler(OnError);
            //watcher.Filter = ""
            // Begin watching.
            watcher.EnableRaisingEvents = true;

            RunEventConsumerThread();

            ConsoleWriter.WriteLine("FileMimic() Started.\nMapping Src:\t" + config.watchPath + "\nTo:\t" + config.destinationPath + "\n");
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
            fileEvents.Enqueue(e);
        }

        void RunEventConsumerThread()
        {
            fileEventConsumer = new Thread(MonitorEvents);
            fileEventConsumer.Start();         
        }

        void MonitorEvents()
        {
            try
            {
                while(Volatile.Read(ref keepConsuming))
                {
                    if(fileEvents.Count == 0)
                    {
                        Thread.Yield();
                        Thread.Sleep(200);
                        continue;
                    }

                    while (fileEvents.TryDequeue(out FileSystemEventArgs e))
                    {
                        if(IsExcludedPath(e.FullPath)) {
                            ConsoleWriter.WriteLine("Ignored()", ConsoleColor.DarkGray);
                            ConsoleWriter.WriteLine("Path: " + e.FullPath, ConsoleColor.DarkGray);
                        }
                        else if (e is RenamedEventArgs)
                        {
                            OnRenamed(null, e as RenamedEventArgs);
                        }
                        else
                        {
                            // Ignore all but most recent of consecutive events for the same file
                            if(fileEvents.TryPeek(out FileSystemEventArgs nextEvent) && e.FullPath == nextEvent.FullPath) {
                                continue;
                            }

                            switch (e.ChangeType)
                            {
                                case WatcherChangeTypes.Changed:
                                    OnChanged(null, e);
                                    break;
                                case WatcherChangeTypes.Created:
                                    OnCreated(null, e);
                                    break;
                                case WatcherChangeTypes.Deleted:
                                    OnDeleted(null, e);
                                    break;
                                default: break;
                            }
                        }
                        Console.Out.FlushAsync();
                    }
                    ConsoleWriter.WriteLine("\nQueue Completed @ " + DateTime.Now, ConsoleColor.Cyan);
                }
            }
            catch (Exception e)
            {
                ConsoleWriter.WriteLine("Exception in File Event Consumer Thread. " + DateTime.Now + "\r\n", ConsoleColor.Red);
                ConsoleWriter.WriteLine(e.StackTrace);
            }
        }

        bool IsExcludedPath(string fullPath)
        {
            return config.excludedPathsGlob.Any(glob => glob.IsMatch(fullPath));
        }

        void OnChanged(object source, FileSystemEventArgs e)
        {
            string path = e.FullPath;
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
            watcher.EnableRaisingEvents = false;
            int maxAttempts = 120;
            int timeOut = 30000;
            int i = 0;
            while (watcher.EnableRaisingEvents == false && i < maxAttempts)
            {
                i += 1;
                try {
                    watcher.EnableRaisingEvents = true;
                } catch {
                    watcher.EnableRaisingEvents = false;
                    Thread.Sleep(timeOut);
                }
            }
        }

        string MapDestinationPath(string watchedPath)
        {
            var path = watchedPath.Replace(config.watchPath, string.Empty);
                path = path.TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(config.destinationPath, path);
        }

        void CopyPathToDestination(string fullSrcPath)
        {
            try {
                var fullDestPath = MapDestinationPath(fullSrcPath);
                PathUtils.CopyPathRecursiveAndOverwrite(fullSrcPath, fullDestPath);
                ConsoleWriter.WriteLine("Path Copied to: " + fullDestPath);
            } catch(Exception e) {
                ConsoleWriter.WriteLine("CopyPathToDestination() " + e.GetType().Name + " Exception: " + e.Message + "\n" + e.StackTrace , ConsoleColor.Red);
            }
        }

        void DeletePathAtDestination(string fullSrcPath)
        {
            try {
                var fullDestPath = MapDestinationPath(fullSrcPath);
                PathUtils.DeletePath(fullDestPath);
                ConsoleWriter.WriteLine("Path Deleted at: " + fullDestPath);
            } catch(Exception e) {
                ConsoleWriter.WriteLine("DeletePathAtDestination() " + e.GetType().Name + " Exception: " + e.Message + "\n" + e.StackTrace, ConsoleColor.Red);
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
                ConsoleWriter.WriteLine("RenamePathAtDestination() " + e.GetType().Name + " Exception: " + e.Message + "\n" + e.StackTrace, ConsoleColor.Red);
            }
        }

        public void Dispose()
        {
            // Exit File Event Consumer Thread 
            Volatile.Write(ref keepConsuming, false);
            fileEventConsumer.Join();
        }
    }
}