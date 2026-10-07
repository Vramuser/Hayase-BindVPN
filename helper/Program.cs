using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace HayaseBindVPN
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Policy.VerifyAbi();
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Run the helper as administrator to manage interface restrictions.");
                bool created;
                using (var mutex = new Mutex(true, "Global\\Hayase-BindVPN-Helper", out created))
                {
                    if (!created)
                    {
                        // A second launch opens the existing window instead of displaying
                        // an error. The first instance keeps ownership of all rules.
                        try { using (var show = EventWaitHandle.OpenExisting("Global\\Hayase-BindVPN-Show")) show.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("The helper is starting. Open it from the system tray.", "Hayase BindVPN"); }
                        return 0;
                    }
                    if (args.Contains("--remove-filters"))
                    {
                        using (var policy = new Policy()) policy.RemoveBlock();
                        Controller.ResetSavedBinding();
                        MessageBox.Show("Hayase binding rules have been removed. Hayase can now use normal Windows network routing.", "Hayase BindVPN recovery");
                        return 0;
                    }
                    using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\Hayase-BindVPN-Show"))
                    using (var controller = new Controller())
                    using (var api = new Api(controller))
                    using (var form = new HelperForm(controller, api))
                    {
                        // Polling this local event on the UI thread also handles requests
                        // received before the window handle is created.
                        using (var activation = new System.Windows.Forms.Timer { Interval = 150 })
                        {
                            activation.Tick += delegate { if (showRequest.WaitOne(0)) form.OpenWindow(); };
                            activation.Start(); Application.Run(form);
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\nIf a binding was enabled, its blocking rules remain in place. Use Recover.cmd to remove them.", "Hayase BindVPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
    internal sealed class HelperForm : Form
    {
        private readonly Controller controller;
        private readonly Api api;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly TextBox executable;
        private readonly ComboBox adapters;
        private readonly Label status;
        private readonly Label info;
        private readonly CheckBox bindingToggle;
        private readonly Button remove;
        private readonly Button test;
        private readonly Button browse;
        private bool exiting;
        private string adapterSignature = "";
        private bool updating;
        private bool testRunning;
        private sealed class Choice
        {
            public Adapter adapter;
            public override string ToString() { return adapter.name + (adapter.up ? " (connected)" : " (down)"); }
        }
        public HelperForm(Controller c, Api a)
        {
            controller = c; api = a;
            Text = "Hayase BindVPN helper " + ReleaseInfo.Version; ClientSize = new Size(520, 585);
            Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Shield;
            var heading = new Label { Text = "Bind Hayase to your VPN", Font = new Font(Font, FontStyle.Bold), Bounds = new Rectangle(20, 18, 480, 30) };
            var description = new Label { Text = "Only the selected interface can carry Hayase traffic.\nKeep this helper running while using Hayase.", Bounds = new Rectangle(20, 54, 480, 50) };
            var pathLabel = new Label { Text = "Hayase application", Bounds = new Rectangle(20, 113, 470, 24) };
            executable = new TextBox { ReadOnly = true, Bounds = new Rectangle(20, 141, 375, 29), Text = controller.Executable };
            browse = new Button { Text = "Browse...", Bounds = new Rectangle(404, 140, 96, 31) };
            browse.Click += delegate {
                using (var dialog = new OpenFileDialog { Filter = "Hayase application|Hayase.exe", Title = "Choose Hayase.exe", CheckFileExists = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) Act(delegate { controller.SetExecutable(dialog.FileName); });
            };
            var adapterLabel = new Label { Text = "Network interface", Bounds = new Rectangle(20, 188, 470, 24) };
            adapters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(20, 216, 375, 29) };
            var refresh = new Button { Text = "Refresh", Bounds = new Rectangle(404, 215, 96, 31) };
            refresh.Click += delegate { adapterSignature = ""; UpdateStatus(); };
            bindingToggle = new CheckBox { Text = "Bind Hayase", Appearance = Appearance.Button, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(20, 260, 228, 36), AccessibleName = "Enable or disable Hayase binding" };
            bindingToggle.CheckedChanged += delegate {
                if (updating) return;
                var choice = adapters.SelectedItem as Choice;
                Act(delegate { if (bindingToggle.Checked && choice != null) controller.Enable(choice.adapter.id); else controller.Disable(); });
            };
            remove = new Button { Text = "Remove binding", Bounds = new Rectangle(260, 260, 240, 36) };
            remove.Click += delegate {
                var choice = adapters.SelectedItem as Choice;
                Act(delegate { if (controller.Enabled && choice != null && choice.adapter.id != controller.AdapterId) controller.Enable(choice.adapter.id); else controller.Disable(); });
            };
            adapters.SelectedIndexChanged += delegate { UpdateStatus(); };
            status = new Label { Bounds = new Rectangle(20, 310, 480, 24), Font = new Font(Font, FontStyle.Bold) };
            info = new Label { Bounds = new Rectangle(20, 340, 480, 48) };
            test = new Button { Text = "Test protection", Bounds = new Rectangle(20, 398, 480, 32) };
            test.Click += async delegate {
                if (testRunning) return;
                testRunning = true; test.Text = "Testing protection..."; UpdateStatus();
                try
                {
                    var result = (TestResult)await Task.Run(() => controller.TestProtection());
                    MessageBox.Show(this, result.message + "\n\n" + string.Join("\n\n", result.checks.Select(check => check.name + ": " + check.state + "\n" + check.detail)), "Protection test", MessageBoxButtons.OK, result.passed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Protection test", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { testRunning = false; test.Text = "Test protection"; UpdateStatus(); }
            };
            var tokenLabel = new Label { Text = "Pairing code — paste this into the Hayase plugin", Bounds = new Rectangle(20, 449, 480, 24) };
            var token = new TextBox { ReadOnly = true, Text = api.Token, Bounds = new Rectangle(20, 477, 375, 28), Font = new Font("Consolas", 9) };
            var copy = new Button { Text = "Copy code", Bounds = new Rectangle(404, 476, 96, 31) };
            copy.Click += delegate { Clipboard.SetText(api.Token); };
            var close = new Label { Text = "Click the tray icon to reopen this window.\nExiting the helper leaves an enabled binding blocked.", Bounds = new Rectangle(20, 525, 480, 44) };
            Controls.AddRange(new Control[] { heading, description, pathLabel, executable, browse, adapterLabel, adapters, refresh, bindingToggle, remove, status, info, test, tokenLabel, token, copy, close });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open helper", null, delegate { OpenWindow(); });
            menu.Items.Add("Exit (keep blocking rules)", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Icon = SystemIcons.Shield, Text = "Hayase BindVPN", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenWindow(); };
            tray.DoubleClick += delegate { OpenWindow(); };
            timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += delegate { UpdateStatus(); };
            timer.Start(); UpdateStatus();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }
        private void Act(System.Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Hayase BindVPN", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            if (updating) return;
            updating = true;
            try
            {
                var json = new JavaScriptSerializer();
                var data = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json.Serialize(controller.Status()));
                status.Text = ((string)data["state"]).ToUpperInvariant(); info.Text = (string)data["message"];
                bool found = (bool)data["appAvailable"];
                bool checking = testRunning || (bool)data["testRunning"];
                status.ForeColor = !found ? Color.DarkOrange : (bool)data["ready"] ? Color.DarkGreen : (bool)data["blocked"] ? Color.DarkOrange : Color.DimGray;
                executable.Text = controller.Executable; browse.Enabled = !controller.Enabled && !(bool)data["blocked"];
                remove.Enabled = !checking && (controller.Enabled || (bool)data["blocked"]);
                Adapter[] list = Adapter.List();
                string key = string.Join("|", list.Select(n => n.id + n.name + n.up));
                string selected = (adapters.SelectedItem as Choice) != null ? ((Choice)adapters.SelectedItem).adapter.id : controller.AdapterId;
                if (key != adapterSignature)
                {
                    adapters.Items.Clear(); foreach (Adapter adapter in list) adapters.Items.Add(new Choice { adapter = adapter });
                    int index = Array.FindIndex(list, n => n.id == selected);
                    if (index < 0 && controller.AdapterId == "") index = Array.FindIndex(list, n => n.suggested);
                    adapters.SelectedIndex = index; adapterSignature = key;
                }
                bindingToggle.Checked = controller.Enabled;
                bindingToggle.Text = controller.Enabled ? "Binding on" : "Binding off";
                bindingToggle.Enabled = !checking && (controller.Enabled || (found && adapters.SelectedIndex >= 0));
                adapters.Enabled = !checking;
                var choice = adapters.SelectedItem as Choice;
                remove.Text = controller.Enabled && choice != null && choice.adapter.id != controller.AdapterId ? "Apply selected interface" : "Remove binding";
                test.Enabled = !checking && found && (bool)data["ready"];
            }
            catch (Exception ex) { status.Text = "ERROR"; info.Text = ex.Message; }
            finally { updating = false; }
        }
        public void OpenWindow()
        {
            Show(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; BringToFront(); Activate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tray.Visible = false; tray.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
