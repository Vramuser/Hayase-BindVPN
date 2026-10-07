using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HayaseBindVPN;

internal static class ApiTests
{
    private sealed class FakeControl : HelperControl
    {
        public int changes;
        public object Status() { return new { state = "unbound", adapters = new object[0] }; }
        public void Enable(string adapterId) { changes++; }
        public void Disable() { changes++; }
        public object TestProtection() { return new { passed = true, message = "Test fixture", checks = new object[0] }; }
    }
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--log")
        {
            var log = new StreamWriter(args[1], false) { AutoFlush = true };
            Console.SetOut(log); Console.SetError(log);
        }
        try
        {
            // A separate port and a fake controller: no real settings or filters.
            var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
            int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
            var controller = new FakeControl();
            using (var api = new Api(controller, port))
            {
                string client = new string('a', 32);
                string other = new string('b', 32);
                string root = "http://127.0.0.1:" + port;
                Require(Call(root, "POST", "/v1/pair", null, client, new string('0', 64), "{}") == 401, "missing Origin does not bypass pairing-code authentication");
                Require(Call(root, "POST", "/v1/pair", "null", client, null, "{}") == 401, "opaque Origin does not bypass pairing-code authentication");
                Require(Call(root, "POST", "/v1/pair", "https://evil.example", client, api.Token, "{}") == 403, "an ordinary website is rejected even with a valid code");
                Require(Call(root, "GET", "/v1/status", null, client, api.Token, null) == 409, "status is not exposed before pairing");
                Require(Call(root, "POST", "/v1/pair", null, null, api.Token, "{}") == 403, "missing plugin identity is rejected");
                Require(Call(root, "POST", "/v1/pair", "chrome-extension://" + other, client, api.Token, "{}") == 403, "mismatched extension origin is rejected");
                Require(Call(root, "POST", "/v1/pair", null, client, api.Token, "invalid") == 400, "malformed pairing does not claim the connection");
                Require(Call(root, "POST", "/v1/pair", null, other, api.Token, "{}") == 200, "the real HTTP handler accepts privileged requests without Origin");
                Require(Call(root, "GET", "/v1/status", "null", other, api.Token, null) == 200, "the paired client can use an opaque popup origin");
                Require(Call(root, "GET", "/v1/status", "chrome-extension://" + other, other, api.Token, null) == 200, "the paired client can also use its extension origin");
                Require(Call(root, "POST", "/v1/pair", null, client, api.Token, "{}") == 403, "a second plugin cannot replace the paired client");
                Require(Call(root, "POST", "/v1/bind", "null", client, api.Token, "{\"adapterId\":\"fake\"}") == 403, "a second plugin cannot change binding");
                Require(Call(root, "POST", "/v1/test", null, client, api.Token, "{}") == 403, "a second plugin cannot run a protection test");
                Require(controller.changes == 0, "rejected callers caused no settings or filter changes");
                Require(Call(root, "POST", "/v1/bind", null, other, api.Token, "{\"adapterId\":\"fake\"}") == 200, "paired client reaches the binding operation");
                Require(Call(root, "POST", "/v1/unbind", null, other, api.Token, "{}") == 200 && controller.changes == 2, "paired client reaches recovery");
                Require(Call(root, "POST", "/v1/test", null, other, api.Token, "{}") == 200, "paired client reaches the protection test");
                Require(Preflight(root, "null") == 204, "opaque popup preflight permits the identity header");
                Require(Preflight(root, "https://evil.example") == 403, "website preflight remains blocked");
            }
            Console.WriteLine("All real helper HTTP pairing checks passed; no live helper or binding was changed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static int Call(string root, string method, string path, string origin, string client, string token, string body)
    {
        var request = (HttpWebRequest)WebRequest.Create(root + path);
        request.Proxy = null; request.Method = method; request.Timeout = 5000;
        if (origin != null) request.Headers["Origin"] = origin;
        if (client != null) request.Headers["X-Hayase-BindVPN-Client"] = client;
        if (token != null) request.Headers["Authorization"] = "Bearer " + token;
        if (body != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.ContentType = "application/json"; request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
        }
        return ResponseCode(request);
    }
    private static int Preflight(string root, string origin)
    {
        var request = (HttpWebRequest)WebRequest.Create(root + "/v1/pair");
        request.Proxy = null; request.Method = "OPTIONS"; request.Timeout = 5000;
        request.Headers["Origin"] = origin;
        request.Headers["Access-Control-Request-Method"] = "POST";
        request.Headers["Access-Control-Request-Headers"] = "authorization, content-type, x-hayase-bindvpn-client";
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                Require(response.Headers["Access-Control-Allow-Origin"] == origin, "preflight origin is echoed exactly");
                Require(response.Headers["Access-Control-Allow-Headers"].Contains("X-Hayase-BindVPN-Client"), "plugin identity header is allowed in preflight");
                return (int)response.StatusCode;
            }
        }
        catch (WebException ex) { using (var response = (HttpWebResponse)ex.Response) { if (response == null) throw; return (int)response.StatusCode; } }
    }
    private static int ResponseCode(HttpWebRequest request)
    {
        try { using (var response = (HttpWebResponse)request.GetResponse()) return (int)response.StatusCode; }
        catch (WebException ex) { using (var response = (HttpWebResponse)ex.Response) { if (response == null) throw; return (int)response.StatusCode; } }
    }
    private static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
