using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;

namespace HayaseBindVPN
{
    internal sealed class Settings
    {
        public string executable = "";
        public string adapterId = "";
        public bool enabled;
    }
    internal sealed class Controller : HelperControl, IDisposable
    {
        private readonly object gate = new object();
        private readonly string settingsPath;
        private readonly BindingPolicy policy;
        private readonly Func<Adapter[]> listAdapters;
        private readonly Func<string> detectExecutable;
        private readonly Func<string, bool> fileExists;
        private Settings settings;
        private string signature = "";
        private string error = "";
        private bool testing;
        public Controller() : this(SettingsFolder(), CreatePolicy(), Adapter.List, DetectExecutable, File.Exists) { }
        private static BindingPolicy CreatePolicy()
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return new Policy(); }
                catch { if (attempt >= 2) throw; System.Threading.Thread.Sleep(350 * (attempt + 1)); }
            }
        }
        internal static string SettingsFolder()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Hayase-BindVPN");
            // Elevated code never consumes a user-writable configuration file.
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The settings directory must not be a link.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(folder, security);
            Directory.SetAccessControl(folder, security);
            return folder;
        }
        internal Controller(string folder, BindingPolicy bindingPolicy, Func<Adapter[]> enumerate, Func<string> detect, Func<string, bool> exists)
        {
            policy = bindingPolicy; listAdapters = enumerate; detectExecutable = detect; fileExists = exists;
            settingsPath = Path.Combine(folder, "settings.json");
            if (File.Exists(settingsPath) && (File.GetAttributes(settingsPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The settings file must not be a link.");
            settings = File.Exists(settingsPath) ? new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) : new Settings();
            if (settings == null) throw new InvalidDataException("Invalid helper settings. Use recovery to remove binding rules.");
            Refresh();
        }
        private bool ApplicationAvailable(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path)
                && string.Equals(Path.GetFileName(path), "Hayase.exe", StringComparison.OrdinalIgnoreCase) && fileExists(path);
        }
        private void ValidateExecutable(string path)
        {
            if (!ApplicationAvailable(path)) throw new ArgumentException("Hayase was not found. Select your installed Hayase.exe in the helper before enabling binding.");
        }
        private static string DetectExecutable()
        {
            foreach (var process in Process.GetProcessesByName("Hayase"))
            {
                try { string path = process.MainModule.FileName; if (File.Exists(path) && string.Equals(Path.GetFileName(path), "Hayase.exe", StringComparison.OrdinalIgnoreCase)) return path; }
                catch { }
                finally { process.Dispose(); }
            }
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                foreach (string part in new[] { "Programs\\Hayase\\Hayase.exe", "Hayase\\Hayase.exe" })
                {
                    string path = Path.Combine(root, part);
                    if (File.Exists(path)) return path;
                }
            }
            return "";
        }
        private void Save()
        {
            string temp = settingsPath + ".tmp";
            if (File.Exists(temp) && (File.GetAttributes(temp) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The temporary settings file must not be a link.");
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(settings));
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null);
            else File.Move(temp, settingsPath);
        }
        public void SetExecutable(string path)
        {
            lock (gate)
            {
                if (settings.enabled || policy.HasBlock()) throw new InvalidOperationException("Remove the current binding before choosing another executable.");
                if (testing) throw new InvalidOperationException("Wait for the protection test to finish.");
                ValidateExecutable(path); settings.executable = Path.GetFullPath(path); Save(); error = "";
            }
        }
        public void Enable(string adapterId)
        {
            lock (gate)
            {
                ValidateExecutable(settings.executable);
                if (testing) throw new InvalidOperationException("Wait for the protection test to finish.");
                Guid guid;
                if (!Guid.TryParse(adapterId, out guid)) throw new ArgumentException("Choose a network interface.");
                Adapter adapter = listAdapters().FirstOrDefault(a => a.id == guid.ToString());
                if (adapter == null) throw new ArgumentException("This interface is missing. Connect the VPN and refresh.");
                // Fail closed while any configuration or filter update is in progress.
                policy.ClearPermits();
                policy.InstallBlock(settings.executable);
                settings.adapterId = adapter.id; settings.enabled = true; signature = "";
                try { Save(); Refresh(); }
                catch (Exception ex) { error = ex.Message; throw; }
            }
        }
        public void Disable()
        {
            lock (gate)
            {
                if (testing) throw new InvalidOperationException("Wait for the protection test to finish.");
                // Persist the intent before deleting rules. A crash leaves blocks in place.
                settings.enabled = false; Save();
                policy.RemoveBlock(); signature = ""; error = "";
            }
        }
        public void Refresh()
        {
            lock (gate)
            {
                try
                {
                    if (!settings.enabled)
                    {
                        // Finish an interrupted unbind automatically, instead of leaving
                        // the user with an error that requires toggling off and on again.
                        if (signature != "off") { policy.RemoveBlock(); signature = "off"; }
                        if (!ApplicationAvailable(settings.executable))
                        {
                            string found = detectExecutable();
                            if (ApplicationAvailable(found) && found != settings.executable) { settings.executable = found; Save(); }
                        }
                        error = ""; return;
                    }
                    if (!ApplicationAvailable(settings.executable))
                    {
                        policy.ClearPermits(); signature = ""; error = ""; return;
                    }
                    if (!policy.HasBlockForExecutable(settings.executable)) { policy.ClearPermits(); policy.InstallBlock(settings.executable); signature = ""; }
                    Adapter adapter = listAdapters().FirstOrDefault(a => a.id == settings.adapterId);
                    string current = adapter == null ? "missing" : adapter.id + ":" + adapter.up + ":" + string.Join(",", adapter.addresses);
                    if (adapter != null)
                    {
                        Guid key = new Guid(adapter.id); ulong luid;
                        uint result = Native.ConvertInterfaceGuidToLuid(ref key, out luid);
                        current += ":" + result + ":" + luid;
                    }
                    bool shouldPermit = adapter != null && adapter.up && adapter.addresses.Length > 0;
                    if (current != signature || (shouldPermit && !policy.PermitsHealthy()))
                    {
                        policy.RestrictTo(settings.executable, adapter); signature = current;
                    }
                    error = "";
                }
                catch (Exception ex)
                {
                    try { policy.ClearPermits(); } catch { }
                    signature = ""; error = ex.Message;
                }
            }
        }
        public object Status()
        {
            lock (gate)
            {
                Refresh();
                Adapter[] adapters = listAdapters();
                Adapter selected = adapters.FirstOrDefault(a => a.id == settings.adapterId);
                bool appAvailable = ApplicationAvailable(settings.executable);
                bool blocked = policy.HasBlock();
                bool ready = appAvailable && settings.enabled && blocked && policy.PermitsHealthy() && selected != null && selected.up && error == "";
                return new {
                    version = ReleaseInfo.Version, executable = settings.executable, adapterId = settings.adapterId,
                    appAvailable = appAvailable, testRunning = testing,
                    enabled = settings.enabled, blocked = blocked, ready = ready,
                    state = !appAvailable ? "app_missing" : error != "" ? "error" : ready ? "bound" : blocked ? "blocked" : "unbound",
                    message = !appAvailable ? "Hayase was not found. Select your installed Hayase.exe in the helper. Binding cannot be enabled until it is found." : error != "" ? "Retrying binding automatically: " + error : ready ? "Restricted to the selected interface" : blocked ? "Hayase is blocked until the selected VPN interface is available" : "Binding is off",
                    adapters = adapters, permitCount = policy.PermitCount
                };
            }
        }
        public string Executable { get { lock (gate) return settings.executable; } }
        public bool Enabled { get { lock (gate) return settings.enabled; } }
        public string AdapterId { get { lock (gate) return settings.adapterId; } }
        public object TestProtection()
        {
            string executable; Adapter adapter; string initialSignature;
            lock (gate)
            {
                Refresh(); ValidateExecutable(settings.executable);
                adapter = listAdapters().FirstOrDefault(a => a.id == settings.adapterId);
                if (!settings.enabled || !policy.HasBlockForExecutable(settings.executable) || !policy.PermitsHealthy() || adapter == null || !adapter.up)
                    throw new InvalidOperationException("Enable binding with the selected interface connected before running the protection test.");
                if (testing) throw new InvalidOperationException("A protection test is already running.");
                testing = true; executable = settings.executable; initialSignature = signature;
            }
            try
            {
                var result = ProtectionTest.Run(adapter);
                lock (gate)
                {
                    Refresh();
                    if (!ApplicationAvailable(executable) || signature != initialSignature || !policy.HasBlockForExecutable(executable) || !policy.PermitsHealthy())
                    {
                        result.passed = false; result.message = "The application or network changed during the test. Check binding and run the test again.";
                    }
                }
                return result;
            }
            finally { lock (gate) testing = false; }
        }
        public void Dispose() { lock (gate) policy.Dispose(); }
        internal static void ResetSavedBinding()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Hayase-BindVPN");
            string path = Path.Combine(folder, "settings.json");
            if (!Directory.Exists(folder) || !File.Exists(path)) return;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Rules were removed, but the linked settings file could not be reset.");
            Settings saved;
            try { saved = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings(); }
            catch { saved = new Settings(); }
            saved.enabled = false;
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(saved));
        }
    }
}
