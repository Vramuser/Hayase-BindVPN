using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace HayaseBindVPN
{
    internal sealed class TestCheck
    {
        public string name;
        public string state;
        public string detail;
    }
    internal sealed class TestResult
    {
        public bool passed;
        public string message;
        public TestCheck[] checks;
        public string testedAt;
    }
    internal static class ProtectionTest
    {
        public static TestResult Run(Adapter adapter)
        {
            // Run only the probe embedded in this helper, from a newly created
            // administrator-only folder. Do not elevate a replaceable companion EXE.
            string folder = Path.Combine(Controller.SettingsFolder(), "test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string probe = Path.Combine(folder, "probe.exe");
            try
            {
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("HayaseBindVPN.NetworkProbe"))
                {
                    if (resource == null) throw new IOException("The embedded test component is missing. Use the complete updated helper.");
                    using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) resource.CopyTo(file);
                }
                return Run(adapter, probe);
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); Directory.Delete(folder); } catch { }
            }
        }
        internal static TestResult Run(Adapter adapter, string probe)
        {
            if (!File.Exists(probe)) throw new FileNotFoundException("The test component is missing. Extract the complete Windows package beside the helper.");
            var checks = new List<TestCheck>();
            bool passed = true;
            // A random sublayer scoped to the probe. Never remove or modify Hayase's
            // production rules, adapters, or VPN connection to simulate a failure.
            using (var testPolicy = new Policy(Guid.NewGuid(), false, true))
            {
                testPolicy.InstallBlock(probe);
                Check(checks, ref passed, "Windows rules", testPolicy.HasBlockForExecutable(probe), "IPv4 and IPv6 application blocks and localhost exceptions were inspected.");
                testPolicy.RestrictTo(probe, adapter);
                string tcp = Probe(probe, "tcp", "1.1.1.1", 443);
                Check(checks, ref passed, "Selected interface", tcp == "success", tcp == "success" ? "A real TCP connection succeeded through the selected interface." : "The selected interface could not connect. Check the VPN and its routing.");

                NetworkInterface nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => {
                    Guid id; return Guid.TryParse(n.Id, out id) && id.ToString() == adapter.id;
                });
                IPAddress dnsAddress = nic == null ? null : nic.GetIPProperties().DnsAddresses
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && Adapter.Usable(ip));
                string dns = dnsAddress == null ? null : dnsAddress.ToString();
                if (dns != null)
                {
                    string udp = Probe(probe, "udp", dns, 53);
                    Check(checks, ref passed, "UDP traffic", udp == "success", udp == "success" ? "A DNS query and response succeeded through the selected interface." : "UDP delivery could not be verified using this interface's DNS server.");
                    testPolicy.ClearPermits();
                    string blockedUdp = Probe(probe, "udp", dns, 53);
                    Check(checks, ref passed, "UDP without interface access", blockedUdp == "blocked" || (udp == "success" && blockedUdp.StartsWith("unreachable:", StringComparison.Ordinal)), "The probe could not receive a UDP response after its interface access was removed.");
                }
                else
                {
                    passed = false;
                    checks.Add(new TestCheck { name = "UDP traffic", state = "skipped", detail = "No IPv4 DNS server is configured on this interface; UDP protection was not verified." });
                }
                testPolicy.ClearPermits();
                string blockedTcp = Probe(probe, "tcp", "1.1.1.1", 443);
                Check(checks, ref passed, "Fallback blocked", blockedTcp == "blocked", "Windows denied a new TCP connection after the probe's interface access was removed.");
                if (adapter.addresses.Any(text => IPAddress.Parse(text).AddressFamily == AddressFamily.InterNetworkV6))
                {
                    testPolicy.RestrictTo(probe, adapter);
                    string v6 = Probe(probe, "tcp", "2606:4700:4700::1111", 443);
                    Check(checks, ref passed, "IPv6 interface", v6 == "success", v6 == "success" ? "A real IPv6 connection succeeded through the selected interface." : "IPv6 internet delivery could not be verified.");
                    testPolicy.ClearPermits();
                    Check(checks, ref passed, "IPv6 fallback blocked", Probe(probe, "tcp", "2606:4700:4700::1111", 443) == "blocked", "Windows denied an IPv6 connection without the selected interface exception.");
                }
                else checks.Add(new TestCheck { name = "IPv6 internet", state = "skipped", detail = "No usable IPv6 address on this interface. IPv6 block rules are installed; external IPv6 delivery was not tested." });
            }
            return new TestResult {
                passed = passed,
                message = passed ? "Protection test passed. Selected-interface traffic worked and fallback was blocked." : "Protection could not be fully verified. Review the checks below.",
                checks = checks.ToArray(), testedAt = DateTimeOffset.UtcNow.ToString("o")
            };
        }
        private static void Check(List<TestCheck> checks, ref bool passed, string name, bool success, string detail)
        {
            if (!success) passed = false;
            checks.Add(new TestCheck { name = name, state = success ? "passed" : "failed", detail = detail });
        }
        private static string Probe(string executable, string protocol, string ip, int port)
        {
            using (var process = Process.Start(new ProcessStartInfo(executable, protocol + " " + ip + " " + port) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                if (!process.WaitForExit(8000)) { process.Kill(); throw new TimeoutException("The test connection timed out."); }
                string result = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode > 2 || result.StartsWith("error:", StringComparison.Ordinal)) throw new IOException("The test component could not complete a connection check.");
                return result;
            }
        }
    }
}
