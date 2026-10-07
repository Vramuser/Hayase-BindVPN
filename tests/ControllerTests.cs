using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using HayaseBindVPN;

internal static class ControllerTests
{
    private sealed class FakePolicy : BindingPolicy
    {
        public bool block;
        public int PermitCount { get; private set; }
        public int installCalls;
        public int removeCalls;
        public int failures;
        public bool HasBlock() { return block; }
        public bool HasBlockForExecutable(string executable) { return block; }
        public bool PermitsHealthy() { return PermitCount > 0; }
        public void InstallBlock(string executable) { installCalls++; if (failures-- > 0) throw new IOException("Temporary startup failure"); block = true; }
        public void RestrictTo(string executable, Adapter adapter) { PermitCount = adapter != null && adapter.up && adapter.addresses.Length > 0 ? 2 : 0; }
        public void ClearPermits() { PermitCount = 0; }
        public void RemoveBlock() { block = false; PermitCount = 0; removeCalls++; }
        public void Dispose() { }
    }
    private static readonly string exe = "C:\\Fixture\\Hayase.exe";
    private static readonly Adapter vpn = new Adapter { id = "f62a1fc4-98ee-4a4e-872f-22141f1e0720", name = "Fixture VPN", up = true, addresses = new[] { "10.0.0.2" } };
    private static int Main(string[] args)
    {
        try
        {
            string root = args[0]; Directory.CreateDirectory(root);
            var json = new JavaScriptSerializer();
            string folder = NewFixture(root, new Settings { executable = exe, adapterId = vpn.id, enabled = false });
            var leftover = new FakePolicy { block = true };
            using (var controller = new Controller(folder, leftover, () => new[] { vpn }, () => "", path => path == exe))
            {
                var status = Data(controller.Status());
                Require(!leftover.block && (string)status["state"] == "unbound", "saved off state automatically removes leftover rules at startup");
                Require(leftover.removeCalls == 1, "startup cleanup is idempotent across status refreshes");
            }
            folder = NewFixture(root, new Settings { executable = exe, adapterId = vpn.id, enabled = true });
            var existing = new FakePolicy { block = true };
            using (var controller = new Controller(folder, existing, () => new[] { vpn }, () => "", path => path == exe))
            {
                Require((bool)Data(controller.Status())["ready"], "saved binding restores without an off/on toggle");
                Require(existing.installCalls == 0, "healthy persisted rules are retained at startup");
            }
            folder = NewFixture(root, new Settings { executable = exe, adapterId = vpn.id, enabled = true });
            var transient = new FakePolicy { failures = 1 };
            using (var controller = new Controller(folder, transient, () => new[] { vpn }, () => "", path => path == exe))
            {
                Require((bool)Data(controller.Status())["ready"] && transient.installCalls == 2, "a transient restore failure is retried automatically");
            }
            folder = NewFixture(root, new Settings { executable = exe, adapterId = vpn.id, enabled = true });
            bool found = true;
            var missing = new FakePolicy { block = true };
            using (var controller = new Controller(folder, missing, () => new[] { vpn }, () => "", path => found && path == exe))
            {
                found = false;
                var status = Data(controller.Status());
                Require(!(bool)status["appAvailable"] && !(bool)status["ready"] && missing.PermitCount == 0 && missing.block,
                    "a disappearing application clears allows and reports a warning while retaining blocks");
                bool refused = false;
                try { controller.Enable(vpn.id); } catch (ArgumentException) { refused = true; }
                Require(refused, "backend refuses binding a stale application path");
                refused = false;
                try { controller.TestProtection(); } catch (ArgumentException) { refused = true; }
                Require(refused, "backend refuses testing a missing application");
                controller.Disable(); Require(!missing.block, "turning off remains possible for recovery when the application is missing");
            }
            folder = NewFixture(root, new Settings());
            using (var controller = new Controller(folder, new FakePolicy(), () => new[] { vpn }, () => exe, path => path == exe))
                Require((bool)Data(controller.Status())["appAvailable"] && controller.Executable == exe, "fresh startup detects and saves the application");
            Console.WriteLine("All controller startup and missing-application checks passed. No real settings or filters were used."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static string NewFixture(string root, Settings settings)
    {
        string folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), new JavaScriptSerializer().Serialize(settings)); return folder;
    }
    private static Dictionary<string, object> Data(object value)
    {
        var json = new JavaScriptSerializer(); return json.Deserialize<Dictionary<string, object>>(json.Serialize(value));
    }
    private static void Require(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
}
