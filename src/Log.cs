using System;
using System.IO;
using System.Text;

namespace RevitAI
{
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string LogPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitAI");
                return Path.Combine(dir, "bridge.log");
            }
        }

        public static void Info(string message) { Write("INFO", message, null); }
        public static void Error(string message) { Write("ERROR", message, null); }
        public static void Error(string message, Exception ex) { Write("ERROR", message, ex); }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    string path = LogPath;
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ").Append(message);
                    if (ex != null) sb.Append(Environment.NewLine).Append(ex);
                    sb.AppendLine();

                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // logging must never break the bridge
            }
        }
    }
}
