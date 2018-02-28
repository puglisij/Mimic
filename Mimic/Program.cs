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
    public class Program
    {
        string m_appRoot;

        static Program m_instance = null;
        public static void Main(string[] args)
        {
            if(m_instance == null) {
                m_instance = new Program();
            }
            m_instance.Run();
        }

        struct Configuration
        {
            public string watchRoot;
            public string devRoot;
            public IEnumerable<string> watchPaths;
            public IEnumerable<string> devPaths;
        }
        Configuration ReadConfiguration()
        {
            var c = new Configuration();
                c.watchRoot = ConfigurationManager.AppSettings["watchRoot"];
                c.watchPaths = ConfigurationManager.AppSettings["watchPaths"].Split(',').Select(path => Path.GetFullPath(Path.Combine(c.watchRoot, path)));
                c.devRoot = ConfigurationManager.AppSettings["devRoot"];
                c.devPaths = ConfigurationManager.AppSettings["devPaths"].Split(',').Select(path => Path.GetFullPath(Path.Combine(c.devRoot, path)));

            return c;
        }

        void Quit()
        {
            Console.WriteLine("Enter \'q\' to quit.\n");
            while (Console.Read() != 'q') ;
        }

        void Run()
        {
            m_appRoot = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var config = ReadConfiguration();

            Console.WriteLine(  "Watch Paths:\n" + PathUtils.PathsToString(config.watchPaths) + "\n"
                              + "Dev Paths:\n" + PathUtils.PathsToString(config.devPaths)
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

            for(var i = 0; i < watchPaths.Length; ++i)
            {
                var watcher = new FileWatchAndCopy(new FileWatcherConfiguration(
                    watchPaths[i], 
                    devPaths[i],
                    1000
                ));
            }
        }


        /// <summary>
        /// Find and read configuration file /mimic.json
        /// </summary>
        //static Dictionary<string, object> ReadConfiguration()
        //{
        //    var configPath = Path.Combine(System.Reflection.Assembly.GetExecutingAssembly().Location, "mimic.json");
        //    Dictionary<string, object> values;

        //    using (var reader = new StreamReader(configPath, Encoding.UTF8))
        //    {
        //        var json = reader.ReadToEnd();
        //        JavaScriptSerializer serializer = new JavaScriptSerializer(); // System.Web.Extensions
        //        values = serializer.Deserialize<Dictionary<string, object>>(json);
        //    }
        //    return values;
        //}
    }
}
