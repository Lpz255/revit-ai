using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RevitAI
{
    internal sealed class BridgeServer
    {
        private readonly IBridgeHandler _handler;
        private readonly int _port;
        private readonly string _token;
        private readonly DateTime _startedAt = DateTime.Now;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public BridgeServer(IBridgeHandler handler, int port, string token)
        {
            _handler = handler;
            _port = port;
            _token = token ?? "";
        }

        public int Port { get { return _port; } }
        public bool IsRunning { get { return _running; } }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "RevitAI-Bridge";
            _thread.Start();
            Log.Info("bridge listening on 127.0.0.1:" + _port);
        }

        public void Stop()
        {
            _running = false;
            try
            {
                if (_listener != null) _listener.Stop();
            }
            catch { }
        }

        private void Loop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (!_running) return;
                    continue;
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }

                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 30000;
                    client.SendTimeout = 30000;
                    NetworkStream stream = client.GetStream();

                    HttpRequest req;
                    if (!MiniHttp.TryReadRequest(stream, out req)) return;

                    int status;
                    string body;
                    try
                    {
                        HandleRequest(req, out status, out body);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("handler failed", ex);
                        status = 500;
                        body = Json.Serialize(new Dictionary<string, object>
                        {
                            { "ok", false }, { "error", ex.Message }, { "kind", ex.GetType().Name }
                        });
                    }

                    MiniHttp.WriteResponse(stream, status, body);
                }
            }
            catch (Exception ex)
            {
                Log.Error("serve failed: " + ex.Message);
            }
        }

        private void HandleRequest(HttpRequest req, out int status, out string body)
        {
            var sw = Stopwatch.StartNew();

            if (!string.IsNullOrEmpty(_token))
            {
                string supplied = req.Header("X-Auth-Token");
                if (supplied != _token)
                {
                    status = 401;
                    body = Json.Serialize(new Dictionary<string, object>
                    {
                        { "ok", false }, { "error", "unauthorized: missing or invalid X-Auth-Token" }
                    });
                    return;
                }
            }

            if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                (req.Path == "/health" || req.Path == "/"))
            {
                status = 200;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", true },
                    { "cmd", "health" },
                    { "data", new Dictionary<string, object>
                        {
                            { "server", "revit-ai" },
                            { "version", BuildInfo.Version },
                            { "port", _port },
                            { "pid", Process.GetCurrentProcess().Id },
                            { "uptime_s", (int)(DateTime.Now - _startedAt).TotalSeconds }
                        }
                    }
                });
                return;
            }

            if (!string.Equals(req.Path, "/rpc", StringComparison.OrdinalIgnoreCase))
            {
                status = 400;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", false }, { "error", "unknown path " + req.Path + " (use POST /rpc or GET /health)" }
                });
                return;
            }

            if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                status = 400;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", false }, { "error", "use POST /rpc" }
                });
                return;
            }

            Dictionary<string, object> payload;
            try
            {
                payload = Json.DeserializeObject(req.BodyText);
            }
            catch (Exception ex)
            {
                status = 400;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", false }, { "error", "invalid JSON: " + ex.Message }
                });
                return;
            }

            object cmdValue;
            string cmd = payload.TryGetValue("cmd", out cmdValue) && cmdValue != null
                ? Convert.ToString(cmdValue)
                : null;

            if (string.IsNullOrEmpty(cmd))
            {
                status = 400;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", false }, { "error", "missing 'cmd'" }
                });
                return;
            }

            IDictionary<string, object> args = null;
            object argsValue;
            if (payload.TryGetValue("args", out argsValue))
            {
                args = argsValue as IDictionary<string, object>;
            }

            try
            {
                object data = _handler.Handle(cmd, args ?? new Dictionary<string, object>());
                status = 200;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", true },
                    { "cmd", cmd },
                    { "data", data },
                    { "ms", sw.ElapsedMilliseconds }
                });
            }
            catch (Exception ex)
            {
                status = 200;
                body = Json.Serialize(new Dictionary<string, object>
                {
                    { "ok", false },
                    { "cmd", cmd },
                    { "error", ex.Message },
                    { "kind", ex.GetType().Name },
                    { "ms", sw.ElapsedMilliseconds }
                });
            }
        }
    }
}
