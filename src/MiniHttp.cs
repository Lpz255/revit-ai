using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace RevitAI
{
    internal sealed class HttpRequest
    {
        public string Method = "";
        public string Path = "";
        public string Version = "";
        public readonly Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = new byte[0];

        public string BodyText
        {
            get { return Body.Length == 0 ? "" : Encoding.UTF8.GetString(Body); }
        }

        public string Header(string name)
        {
            string value;
            return Headers.TryGetValue(name, out value) ? value : null;
        }
    }

    internal static class MiniHttp
    {
        private const int MaxHead = 64 * 1024;
        private const int MaxBody = 8 * 1024 * 1024;

        public static bool TryReadRequest(NetworkStream stream, out HttpRequest request)
        {
            request = null;
            var acc = new MemoryStream();
            var buf = new byte[8192];
            int headEnd = -1;

            while (headEnd < 0)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) return false;
                acc.Write(buf, 0, n);
                if (acc.Length > MaxHead) return false;
                headEnd = FindHeadEnd(acc.GetBuffer(), (int)acc.Length);
            }

            byte[] all = acc.GetBuffer();
            int total = (int)acc.Length;
            string headText = Encoding.ASCII.GetString(all, 0, headEnd);

            var req = new HttpRequest();
            string[] lines = headText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return false;

            req.Method = requestLine[0].ToUpperInvariant();
            req.Path = requestLine[1];
            req.Version = requestLine.Length > 2 ? requestLine[2] : "HTTP/1.1";

            for (int i = 1; i < lines.Length; i++)
            {
                int idx = lines[i].IndexOf(':');
                if (idx <= 0) continue;
                string name = lines[i].Substring(0, idx).Trim();
                string value = lines[i].Substring(idx + 1).Trim();
                req.Headers[name] = value;
            }

            int contentLength = 0;
            string cl = req.Header("Content-Length");
            if (!string.IsNullOrEmpty(cl)) int.TryParse(cl, out contentLength);
            if (contentLength < 0 || contentLength > MaxBody) return false;

            var body = new byte[contentLength];
            int bodyStart = headEnd + 4;
            int available = total - bodyStart;
            int copied = 0;

            if (available > 0 && contentLength > 0)
            {
                copied = Math.Min(available, contentLength);
                Buffer.BlockCopy(all, bodyStart, body, 0, copied);
            }

            while (copied < contentLength)
            {
                int n = stream.Read(body, copied, contentLength - copied);
                if (n <= 0) return false;
                copied += n;
            }

            req.Body = body;
            request = req;
            return true;
        }

        private static int FindHeadEnd(byte[] data, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (data[i] == 10 && data[i - 1] == 13 && data[i - 2] == 10 && data[i - 3] == 13)
                    return i - 3;
            }
            return -1;
        }

        public static void WriteResponse(Stream stream, int status, string textBody)
        {
            byte[] body = Encoding.UTF8.GetBytes(textBody ?? "");
            string reason = status == 200 ? "OK"
                          : status == 400 ? "Bad Request"
                          : status == 401 ? "Unauthorized"
                          : "Internal Server Error";

            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            head.Append("Content-Type: application/json; charset=utf-8\r\n");
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");

            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
