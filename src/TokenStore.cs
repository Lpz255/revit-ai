using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RevitAI
{
    internal static class TokenStore
    {
        public static string TokenPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitAI");
                return Path.Combine(dir, "token.txt");
            }
        }

        public static string LoadOrCreate()
        {
            string path = TokenPath;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (existing.Length >= 16) return existing;
            }

            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(bytes); }
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));

            string token = sb.ToString();
            File.WriteAllText(path, token, new UTF8Encoding(false));
            return token;
        }
    }
}
