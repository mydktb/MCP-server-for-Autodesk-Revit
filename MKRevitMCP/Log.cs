using System;
using System.IO;

namespace MKRevitMCP
{
    public static class Log
    {
        private const string LogPath = @"C:\Temp\mcp.log";
        private static readonly object _lock = new object();

        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(@"C:\Temp");
                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}