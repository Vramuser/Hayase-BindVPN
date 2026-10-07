using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using HayaseBindVPN;

internal static class NativeTests
{
    [DllImport("iphlpapi.dll")] private static extern uint GetBestInterface(uint destination, out uint index);
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--log")
        {
            var log = new StreamWriter(args[1], false) { AutoFlush = true };
            Console.SetOut(log); Console.SetError(log); args = args.Skip(2).ToArray();
        }
        try
        {
            Policy.VerifyAbi(); Pass("native structures match the Windows x64 ABI");
            Require(Api.ValidOrigin("chrome-extension://" + new string('a', 32)), "extension origin accepted");
            Require(!Api.ValidOrigin("https://evil.example") && !Api.ValidOrigin("null") && !Api.ValidOrigin("chrome-extension://" + new string('a', 32) + "/path"), "web, null, and malformed origins rejected");
            Require(Api.OriginMatchesClient(null, new string('a', 32)) && Api.OriginMatchesClient("null", new string('a', 32)), "sandboxed extension origins require an explicit valid client identity");
            Require(!Api.OriginMatchesClient(null, null) && !Api.OriginMatchesClient("https://evil.example", new string('a', 32))
                && !Api.OriginMatchesClient("chrome-extension://" + new string('b', 32), new string('a', 32)), "web callers, missing IDs, and mismatched extension IDs rejected");
            Require(Api.TokenEquals(new string('a', 64), new string('a', 64)) && !Api.TokenEquals(new string('a', 64), new string('b', 64)) && !Api.TokenEquals("", ""), "pairing authentication validates the complete secret");
            Adapter[] adapters = Adapter.List();
            Require(adapters.All(a => a.id != "" && Guid.Parse(a.id) != Guid.Empty), "adapter enumeration returns usable persistent identities");
            if (!args.Contains("--integration")) return 0;
            Require(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), "integration test is elevated");
            string probe = args.Last();
            string baseline = Probe(probe, "tcp", "1.1.1.1", 443);
            Console.WriteLine("Baseline external TCP: " + baseline);
            // Some VPNs allow DNS only through their own resolver.
            string udpTarget = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => (n.Name + n.Description).IndexOf("Mullvad", StringComparison.OrdinalIgnoreCase) >= 0)
                .SelectMany(n => n.GetIPProperties().DnsAddresses).Where(a => a.AddressFamily == AddressFamily.InterNetwork && Adapter.Usable(a))
                .Select(a => a.ToString()).Concat(new[] { "1.1.1.1" }).Distinct()
                .FirstOrDefault(ip => Probe(probe, "udp", ip, 53) == "success");
            Require(udpTarget != null, "found a reachable non-loopback DNS resolver for UDP testing");
            string baselineUdp = Probe(probe, "udp", udpTarget, 53);
            Require(baselineUdp == "success", "external UDP DNS response received before restrictions");
            var listener4 = new TcpListener(IPAddress.Loopback, 0);
            var listener6 = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener4.Start(); listener6.Start();
            try
            {
                // Unique sublayer and dynamic sessions: this test cannot change Hayase rules.
                using (var policy = new Policy(Guid.NewGuid(), false, true))
                {
                    policy.InstallBlock(probe);
                    Require(policy.HasBlock(), "IPv4/IPv6 outbound and inbound blocks installed");
                    Require(policy.HasBlockForExecutable(probe), "installed rules match the exact probe executable");
                    Require(Probe(probe, "tcp", "127.0.0.1", ((IPEndPoint)listener4.LocalEndpoint).Port) == "success", "loopback IPv4 playback/helper traffic remains allowed");
                    Require(Probe(probe, "tcp", "::1", ((IPEndPoint)listener6.LocalEndpoint).Port) == "success", "loopback IPv6 remains allowed");
                    Require(Probe(probe, "tcp", "1.1.1.1", 443) == "blocked", "unselected external IPv4 TCP is denied by Windows");
                    string blockedUdp = Probe(probe, "udp", udpTarget, 53);
                    Console.WriteLine("Restricted UDP result: " + blockedUdp);
                    Require(blockedUdp != "success", "unselected external IPv4 UDP cannot deliver a DNS query");
                    string v6 = Probe(probe, "udp", "2606:4700:4700::1111", 53);
                    Require(v6 != "success", "external IPv6 UDP cannot escape the restriction");
                    if (v6 != "blocked") Console.WriteLine("IPv6 has no route on this host; filter installation was verified, packet denial cannot be independently confirmed.");
                    policy.RestrictTo(probe, null);
                    Require(policy.PermitCount == 0 && Probe(probe, "tcp", "1.1.1.1", 443) == "blocked", "a missing adapter stays blocked");
                    uint best;
                    Require(GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0), out best) == 0, "resolved the current test route");
                    var routed = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(a => { try { return a.GetIPProperties().GetIPv4Properties().Index == best; } catch { return false; } });
                    Adapter selected = routed == null ? null : adapters.FirstOrDefault(a => new Guid(a.id) == new Guid(routed.Id));
                    Require(selected != null && selected.up && selected.addresses.Length > 0, "selected the adapter for the current test route");
                    policy.RestrictTo(probe, selected);
                    Require(policy.PermitCount > 0, "selected adapter exceptions installed atomically");
                    Require(policy.PermitsHealthy(), "all interface exceptions are present in Windows");
                    string allowed = Probe(probe, "tcp", "1.1.1.1", 443);
                    Require(baseline == "success" ? allowed == "success" : allowed != "blocked", "selected adapter can carry the same external TCP as baseline");
                    uint udpBest;
                    Native.Check(GetBestInterface(BitConverter.ToUInt32(IPAddress.Parse(udpTarget).GetAddressBytes(), 0), out udpBest), "Resolve UDP test route");
                    var udpRouted = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(a => { try { return a.GetIPProperties().GetIPv4Properties().Index == udpBest; } catch { return false; } });
                    Adapter udpSelected = udpRouted == null ? null : adapters.FirstOrDefault(a => new Guid(a.id) == new Guid(udpRouted.Id));
                    Require(udpSelected != null && udpSelected.up, "resolved the UDP test adapter");
                    policy.RestrictTo(probe, udpSelected);
                    Require(Probe(probe, "udp", udpTarget, 53) == "success", "selected adapter can deliver and receive external UDP");
                    // Stale source addresses and stale identities must fail closed.
                    var stale = new Adapter { id = selected.id, up = true, addresses = new[] { "192.0.2.123" } };
                    policy.RestrictTo(probe, stale);
                    Require(Probe(probe, "tcp", "1.1.1.1", 443) == "blocked", "an address change cannot open fallback access");
                    var wrong = adapters.FirstOrDefault(a => a.id != selected.id && a.up && a.addresses.Length > 0);
                    if (wrong != null)
                    {
                        policy.RestrictTo(probe, wrong);
                        Require(Probe(probe, "tcp", "1.1.1.1", 443) == "blocked", "choosing another active interface does not permit the default route");
                    }
                    else Console.WriteLine("SKIP: no second active adapter for cross-interface test.");
                    policy.RestrictTo(probe, selected);
                    policy.CloseAllowSessionForTest();
                    Require(Probe(probe, "tcp", "1.1.1.1", 443) == "blocked", "removing the helper's exceptions leaves application blocking intact");
                    Require(Probe(probe, "udp", udpTarget, 53) != "success", "helper session loss cannot deliver external UDP");
                    // The harness executable is not the protected probe.
                    Require(SelfTcp() != "blocked", "another application retains its normal network access");
                    policy.RemoveBlock();
                    Require(!policy.HasBlock(), "recovery removes the owned blocks");
                    Require(baseline == "success" ? Probe(probe, "tcp", "1.1.1.1", 443) == "success" : Probe(probe, "tcp", "1.1.1.1", 443) != "blocked", "probe network access returns after recovery");
                }
            }
            finally { listener4.Stop(); listener6.Stop(); }
            var runtimeAdapter = Adapter.List().FirstOrDefault(a => a.suggested && a.up && a.addresses.Length > 0);
            if (runtimeAdapter != null)
            {
                TestResult result = ProtectionTest.Run(runtimeAdapter, probe);
                foreach (var check in result.checks) Console.WriteLine("Runtime test " + check.name + ": " + check.state);
                Require(result.passed, "the new protection test verifies real connections and fallback blocking");
            }
            Console.WriteLine("All native integration checks passed; temporary rules are gone."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static string SelfTcp()
    {
        try { using (var socket = new TcpClient()) { var pending = socket.BeginConnect("1.1.1.1", 443, null, null); if (!pending.AsyncWaitHandle.WaitOne(5000)) return "timeout"; socket.EndConnect(pending); return "success"; } }
        catch (SocketException ex) { return ex.SocketErrorCode == SocketError.AccessDenied ? "blocked" : "unreachable"; }
    }
    private static string Probe(string executable, string protocol, string ip, int port)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, protocol + " " + ip + " " + port) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Network probe hung."); }
            return process.StandardOutput.ReadToEnd().Trim();
        }
    }
    private static void Require(bool condition, string label) { if (!condition) throw new Exception(label); Pass(label); }
    private static void Pass(string label) { Console.WriteLine("PASS: " + label); }
}
