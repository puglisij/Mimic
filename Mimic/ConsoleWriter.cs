using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mimic
{
    static class ConsoleWriter
    {
        private static object _MessageLock = new object();

        public static void WriteLine(string message, ConsoleColor color = ConsoleColor.White)
        {
            lock (_MessageLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }
    }
}
