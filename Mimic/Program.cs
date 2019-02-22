using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;

using System.Configuration;

namespace Mimic
{
    public class Program : IDisposable
    {
        string m_appRoot;
        FileMimic[] mimics;

        static Program m_instance = null;

        public static void Main(string[] args)
        {
            Console.Title = "Mimic - File Watcher (Version 1.03)";
            // Don't allow more than one instance
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).Count() > 1)
            {
                ConsoleWriter.WriteLine("You already have an instance running.");
                Quit();
                return;
            }

            if (m_instance == null) {
                m_instance = new Program();
            }
            m_instance.Run();
            m_instance.Dispose();
        }

        struct Configuration
        {
            public string watchRoot;
            public string devRoot;
            public IEnumerable<string> watchPaths;
            public IEnumerable<string> devPaths;
            public IEnumerable<string> excludedPaths;
        }
        Configuration ReadConfiguration()
        {
            var c = new Configuration();
                c.watchRoot = ConfigurationManager.AppSettings["watchRoot"];
                c.watchPaths = ConfigurationManager.AppSettings["watchPaths"].Split(',').Select(path => Path.GetFullPath(Path.Combine(c.watchRoot, path)));
                c.devRoot = ConfigurationManager.AppSettings["devRoot"];
                c.devPaths = ConfigurationManager.AppSettings["devPaths"].Split(',').Select(path => Path.GetFullPath(Path.Combine(c.devRoot, path)));
                c.excludedPaths = ConfigurationManager.AppSettings["excludedPaths"].Split(',').Select(pathGlob => pathGlob.Trim());

            return c;
        }

        static void Quit()
        {
            ConsoleWriter.WriteLine("Enter \'q\' to quit.\n");
            while (Console.Read() != 'q') ;
        }

        void Run()
        {
            m_appRoot = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var config = ReadConfiguration();

            Console.WriteLine(  "Watch Paths:\n" + PathUtils.PathsToString(config.watchPaths) + "\n"
                              + "Dev Paths:\n" + PathUtils.PathsToString(config.devPaths) + "\n"
                              + "Excluded Paths Glob:\n" + PathUtils.PathsToString(config.excludedPaths)
                              + "\n"
                              );

            try {
                PathUtils.AssertExists(config.watchRoot);
                PathUtils.AssertExists(config.watchPaths);
                PathUtils.AssertExists(config.devRoot);

                Debug.Assert(config.devPaths.Count() == config.watchPaths.Count(), "Mismatching path count. For each watch path there should be a corresponding dev (destination) path.");
            } catch(Exception e) {
                Quit();
            }

            CreateAndRunWatchers(config);
            Quit();
        }

        void CreateAndRunWatchers(Configuration config)
        {
            var watchPaths = config.watchPaths.ToArray();
            var devPaths = config.devPaths.ToArray();
            var excludedPaths = config.excludedPaths.ToArray();

            mimics = new FileMimic[watchPaths.Length];
            for (var i = 0; i < watchPaths.Length; ++i)
            {
                mimics[i] = new FileMimic(new FileWatcherConfiguration(
                    watchPaths[i], 
                    devPaths[i],
                    excludedPaths
                ));
            }
        }

        public void Dispose()
        {
            ConsoleWriter.WriteLine("Exiting Application...");
            foreach(var mimic in mimics) {
                mimic.Dispose();
            }
        }
    }
}
