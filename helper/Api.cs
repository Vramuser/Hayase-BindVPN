using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HayaseBindVPN
{
    internal interface HelperControl
    {
        object Status();
        void Enable(string adapterId);
        void Disable();
        object TestProtection();
    }
    internal sealed class Api : IDisposable
    {
        internal const int Port = 49736;
        private readonly HttpListener listener = new HttpListener();
        private readonly HelperControl controller;
        private readonly int listenPort;
        private readonly object pairingGate = new object();
        private string pairedClient = "";
        public string Token { get; private set; }
        public Api(HelperControl c, int port = Port)
        {
            controller = c; listenPort = port;
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            Token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            listener.Prefixes.Add("http://127.0.0.1:" + listenPort + "/");
            listener.Start();
            var thread = new Thread(Run) { IsBackground = true }; thread.Start();
        }
        internal static bool ValidOrigin(string origin)
        {
            const string prefix = "chrome-extension://";
            return origin != null && origin.StartsWith(prefix, StringComparison.Ordinal) && ValidClientId(origin.Substring(prefix.Length));
        }
        internal static bool ValidClientId(string id)
        {
            return id != null && Regex.IsMatch(id, "\\A[a-p]{32}\\z", RegexOptions.CultureInvariant);
        }
        internal static bool AllowedOrigin(string origin)
        {
            // Privileged extension requests can omit Origin; a sandboxed popup
            // can serialize it as "null". The secret and client ID are still required.
            return string.IsNullOrEmpty(origin) || origin == "null" || ValidOrigin(origin);
        }
        internal static bool OriginMatchesClient(string origin, string client)
        {
            return ValidClientId(client) && AllowedOrigin(origin)
                && (!ValidOrigin(origin) || origin == "chrome-extension://" + client);
        }
        internal static bool TokenEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != 64 || b.Length != 64) return false;
            int difference = 0;
            for (int i = 0; i < 64; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
        private void Run()
        {
            while (listener.IsListening)
            {
                try { var context = listener.GetContext(); ThreadPool.QueueUserWorkItem(delegate { Handle(context); }); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }
        private static void Reply(HttpListenerContext context, int code, object value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
            context.Response.StatusCode = code;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        private void Handle(HttpListenerContext context)
        {
            try
            {
                string origin = context.Request.Headers["Origin"];
                string path = context.Request.Url.AbsolutePath;
                if (context.Request.Url.Host != "127.0.0.1" || context.Request.Url.Port != listenPort
                    || context.Request.RemoteEndPoint == null || !IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address) || !AllowedOrigin(origin))
                { Reply(context, 403, new { error = "Open this plugin through Hayase's Plugins settings." }); return; }
                if (!string.IsNullOrEmpty(origin)) context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Vary"] = "Origin";
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                    context.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, X-Hayase-BindVPN-Client";
                    context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
                    context.Response.StatusCode = 204; return;
                }
                string client = context.Request.Headers["X-Hayase-BindVPN-Client"];
                if (!OriginMatchesClient(origin, client))
                { Reply(context, 403, new { error = "Update both the helper and the Hayase plugin, then connect again." }); return; }
                // The pairing secret never appears in a URL or logs.
                string auth = context.Request.Headers["Authorization"];
                if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal) || !TokenEquals(auth.Substring(7), Token))
                { Reply(context, 401, new { error = "Paste the current pairing code from the helper window." }); return; }
                if (context.Request.Url.Query != "") { Reply(context, 400, new { error = "Query strings are not accepted." }); return; }
                lock (pairingGate)
                {
                    if (pairedClient != "" && pairedClient != client)
                    { Reply(context, 403, new { error = "Another plugin is paired. Restart the helper to pair again." }); return; }
                    if (pairedClient == "" && (path != "/v1/pair" || context.Request.HttpMethod != "POST"))
                    { Reply(context, 409, new { error = "Pair the plugin first." }); return; }
                }
                if (context.Request.HttpMethod == "GET" && path == "/v1/status")
                { Reply(context, 200, controller.Status()); return; }
                if (context.Request.HttpMethod != "POST") { Reply(context, 405, new { error = "Unsupported request." }); return; }
                if (context.Request.ContentLength64 < 0 || context.Request.ContentLength64 > 4096
                    || context.Request.ContentType == null || !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                { Reply(context, 400, new { error = "A small JSON request body is required." }); return; }
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                var data = new JavaScriptSerializer { MaxJsonLength = 4096 }.Deserialize<Dictionary<string, object>>(body);
                if (data == null) throw new ArgumentException("Invalid request.");
                if (path == "/v1/pair")
                {
                    // Claim the client only after the authenticated pairing body is valid.
                    lock (pairingGate)
                    {
                        if (pairedClient != "" && pairedClient != client)
                        { Reply(context, 403, new { error = "Another plugin is paired. Restart the helper to pair again." }); return; }
                        pairedClient = client;
                    }
                    Reply(context, 200, controller.Status()); return;
                }
                if (path == "/v1/bind")
                {
                    object id;
                    if (!data.TryGetValue("adapterId", out id) || !(id is string)) throw new ArgumentException("Choose an adapter.");
                    controller.Enable((string)id); Reply(context, 200, controller.Status()); return;
                }
                if (path == "/v1/unbind") { controller.Disable(); Reply(context, 200, controller.Status()); return; }
                if (path == "/v1/test") { Reply(context, 200, controller.TestProtection()); return; }
                Reply(context, 404, new { error = "Unknown helper operation." });
            }
            catch (Exception ex)
            {
                try { Reply(context, 400, new { error = ex.Message }); } catch { }
            }
            finally { try { context.Response.Close(); } catch { } }
        }
        public void Dispose() { listener.Close(); }
    }
}
