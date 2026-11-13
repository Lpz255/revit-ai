using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RevitAI
{
    internal sealed class FakeHandler : IBridgeHandler
    {
        public object Handle(string cmd, IDictionary<string, object> args)
        {
            if (cmd == "ping") return new Dictionary<string, object> { { "pong", true } };
            if (cmd == "echo")
            {
                var copy = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in args) copy[kv.Key] = kv.Value;
                return copy;
            }
            throw new ArgumentException("unknown command '" + cmd + "'");
        }
    }

    internal sealed class Reply
    {
        public int Status;
        public string Body = "";
    }

    internal static class Program
    {
        private const int Port = 8799;
        private static int _failures;

        private static int Main(string[] args)
        {
            var server = new BridgeServer(new FakeHandler(), Port, "test-token");
            server.Start();
            Thread.Sleep(300);

            Console.WriteLine("== RevitAI bridge self test (port " + Port + ") ==");

            Check("GET /health returns 200 ok", delegate
            {
                Reply r = Fetch("GET", "/health", null, "test-token");
                Console.WriteLine("   " + r.Body);
                return r.Status == 200 && r.Body.Contains("\"ok\":true") && r.Body.Contains("\"server\":\"revit-ai\"");
            });

            Check("POST /rpc ping", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{\"cmd\":\"ping\"}", "test-token");
                Console.WriteLine("   " + r.Body);
                return r.Status == 200 && r.Body.Contains("\"pong\":true");
            });

            Check("args round-trip", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{\"cmd\":\"echo\",\"args\":{\"a\":1,\"b\":\"two\"}}", "test-token");
                Console.WriteLine("   " + r.Body);
                return r.Body.Contains("\"a\":1") && r.Body.Contains("\"b\":\"two\"");
            });

            Check("bad token rejected with 401", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{\"cmd\":\"ping\"}", "nope");
                return r.Status == 401;
            });

            Check("missing token rejected with 401", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{\"cmd\":\"ping\"}", null);
                return r.Status == 401;
            });

            Check("unknown cmd -> ok:false", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{\"cmd\":\"nope\"}", "test-token");
                Console.WriteLine("   " + r.Body);
                return r.Body.Contains("\"ok\":false") && r.Body.Contains("unknown command");
            });

            Check("unknown path -> 400", delegate
            {
                Reply r = Fetch("POST", "/bogus", "{}", "test-token");
                return r.Status == 400;
            });

            Check("malformed json -> 400", delegate
            {
                Reply r = Fetch("POST", "/rpc", "{not json", "test-token");
                return r.Status == 400;
            });

            server.Stop();

            Console.WriteLine(_failures == 0 ? "ALL PASS" : (_failures + " FAILURE(S)"));
            return _failures == 0 ? 0 : 1;
        }

        private static void Check(string name, Func<bool> test)
        {
            try
            {
                bool ok = test();
                Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
                if (!ok) _failures++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
                _failures++;
            }
        }

        private static Reply Fetch(string method, string path, string json, string token)
        {
            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", Port);
                using (Stream stream = client.GetStream())
                {
                    string body = json ?? "";
                    var sb = new StringBuilder();
                    sb.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
                    sb.Append("Host: 127.0.0.1\r\n");
                    sb.Append("Content-Type: application/json\r\n");
                    if (token != null) sb.Append("X-Auth-Token: ").Append(token).Append("\r\n");
                    sb.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n");
                    sb.Append("Connection: close\r\n\r\n");
                    sb.Append(body);

                    byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                    stream.Flush();

                    var ms = new MemoryStream();
                    var buffer = new byte[8192];
                    int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, n);

                    string all = Encoding.UTF8.GetString(ms.ToArray());
                    int split = all.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    string head = split > 0 ? all.Substring(0, split) : all;
                    string responseBody = split > 0 ? all.Substring(split + 4) : "";

                    int status = 0;
                    string[] parts = head.Split(' ');
                    if (parts.Length > 1) int.TryParse(parts[1], out status);

                    return new Reply { Status = status, Body = responseBody };
                }
            }
        }
    }
}
