using System.IO;
using System.Collections.Generic;

namespace stream350client
{
    public static class Log
    {
        public static void Msg(string str)
        {
            File.AppendAllText("./log.txt", str + '\n');
        }

        public static void Init()
        {
            using var fs = File.CreateText("./log.txt");
            fs.WriteLine("[stream350client log - written by @EliseZeroTwo]");
            
        }
    }
}