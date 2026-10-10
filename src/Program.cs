using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ZhongWenSnap
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                SelfTest.Run();
                return;
            }
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\ZhongWenSnap", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("ZhongWen Snap is already running. Use the tray icon near the clock.",
                        "ZhongWen Snap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    try
                    {
                        if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
                    }
                    catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayContext());
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not start ZhongWen Snap: " + ex.Message,
                        "ZhongWen Snap", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal static class HotkeyParser
    {
        public static bool TryParse(string input, out uint modifiers, out uint key, out string error)
        {
            modifiers = 0;
            key = 0;
            error = "";
            if (String.IsNullOrWhiteSpace(input)) { error = "Enter a shortcut such as Ctrl+Alt+T."; return false; }
            var parts = input.ToUpperInvariant().Split('+');
            if (parts.Length < 2) { error = "Use at least one modifier and one key, such as Ctrl+Alt+T."; return false; }
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i].Trim();
                uint flag = part == "CTRL" || part == "CONTROL" ? 2u : part == "ALT" ? 1u :
                    part == "SHIFT" ? 4u : part == "WIN" ? 8u : 0u;
                if (flag == 0 || (modifiers & flag) != 0)
                { error = "Unknown or repeated shortcut modifier: " + part; return false; }
                modifiers |= flag;
            }
            var last = parts[parts.Length - 1].Trim();
            if (last.Length == 1 && ((last[0] >= 'A' && last[0] <= 'Z') || (last[0] >= '0' && last[0] <= '9')))
                key = (uint)last[0];
            else if (last == "SPACE") key = 0x20;
            else if (last.StartsWith("F"))
            {
                int number;
                if (Int32.TryParse(last.Substring(1), out number) && number >= 1 && number <= 24)
                    key = (uint)(0x70 + number - 1);
            }
            if (key == 0) { error = "Use a letter, number, Space, or F1–F24 as the shortcut key."; return false; }
            return true;
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 1;
        private string activeShortcut;
        public event Action Pressed;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr handle, int id);

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "ZhongWenSnapHotkey" });
        }

        public bool Change(string shortcut, out string error)
        {
            uint modifiers, key;
            if (!HotkeyParser.TryParse(shortcut, out modifiers, out key, out error)) return false;
            var previous = activeShortcut;
            if (previous != null) UnregisterHotKey(Handle, HOTKEY_ID);
            if (!RegisterHotKey(Handle, HOTKEY_ID, modifiers | 0x4000u, key))
            {
                if (previous != null)
                {
                    uint oldMods, oldKey;
                    string ignored;
                    if (HotkeyParser.TryParse(previous, out oldMods, out oldKey, out ignored))
                        RegisterHotKey(Handle, HOTKEY_ID, oldMods | 0x4000u, oldKey);
                }
                error = "Windows could not register " + shortcut + ". Choose another shortcut.";
                return false;
            }
            activeShortcut = shortcut;
            error = "";
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_HOTKEY && message.WParam.ToInt32() == HOTKEY_ID && Pressed != null)
                Pressed();
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (activeShortcut != null) UnregisterHotKey(Handle, HOTKEY_ID);
            DestroyHandle();
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly HotkeyWindow hotkey;
        private readonly Control dispatcher;
        private readonly IImageInterpreter interpreter = new OpenAiImageInterpreter();
        private AppSettings settings;
        private ResultPopup popup;
        private SettingsForm settingsForm;
        private HistoryForm historyForm;
        private bool busy;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon CreateTrayIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(Color.FromArgb(32, 103, 98)))
            using (var foreground = new SolidBrush(Color.White))
            using (var font = new Font("Microsoft JhengHei", 17, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.FillEllipse(background, 0, 0, 31, 31);
                graphics.DrawString("中", font, foreground, new RectangleF(0, -1, 32, 32), format);
                var handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        public TrayContext()
        {
            SettingsStore.MigrateLegacyFiles();
            settings = SettingsStore.Load();
            dispatcher = new Control();
            var handle = dispatcher.Handle;
            hotkey = new HotkeyWindow();
            hotkey.Pressed += BeginCapture;
            var shortcutError = "";
            if (!hotkey.Change(settings.Hotkey, out shortcutError))
                MessageBox.Show(shortcutError, "Shortcut unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Capture region", null, (s, e) => BeginCapture());
            menu.Items.Add("History", null, (s, e) => ShowHistory());
            menu.Items.Add("Settings", null, (s, e) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            trayIcon = CreateTrayIcon();
            tray = new NotifyIcon { Text = "ZhongWen Snap", Icon = trayIcon,
                ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => BeginCapture();
            try { ConfigureStartup(settings.StartWithWindows); }
            catch (Exception) { /* Saving Settings will report a startup configuration error. */ }
            if (String.IsNullOrEmpty(TryLoadKey())) ShowSettings();
        }

        private static void ConfigureStartup(bool enabled)
        {
            using (var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run == null) throw new InvalidOperationException("Cannot open Windows startup settings.");
                if (enabled)
                    run.SetValue("ZhongWenSnap", "\"" + Application.ExecutablePath + "\"");
                else run.DeleteValue("ZhongWenSnap", false);
                run.DeleteValue("TaiwanSubtitleLens", false);
            }
        }

        private string TryLoadKey()
        {
            try { return SettingsStore.LoadKey(); }
            catch (CryptographicException) { return ""; }
            catch (IOException) { return ""; }
        }

        private void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(settings, TryLoadKey(), ApplySettings);
            settingsForm.Show();
            settingsForm.Activate();
        }

        private string ApplySettings(AppSettings candidate, string key)
        {
            string error;
            if (!hotkey.Change(candidate.Hotkey, out error)) return error;
            try
            {
                SettingsStore.SaveKey(key);
                SettingsStore.Save(candidate);
                ConfigureStartup(candidate.StartWithWindows);
                settings = candidate;
                return "";
            }
            catch (Exception ex)
            {
                string ignored;
                hotkey.Change(settings.Hotkey, out ignored);
                return "Could not save settings: " + ex.Message;
            }
        }

        private void ShowHistory()
        {
            if (historyForm != null && !historyForm.IsDisposed)
            { historyForm.Activate(); return; }
            try
            {
                historyForm = new HistoryForm();
                historyForm.Show();
                historyForm.Activate();
            }
            catch (Exception ex) { MessageBox.Show("Could not open history: " + ex.Message); }
        }

        private void BeginCapture()
        {
            if (busy) return;
            string key = TryLoadKey();
            if (String.IsNullOrWhiteSpace(key)) { ShowSettings(); return; }
            ClosePopup();
            try
            {
                var bounds = SystemInformation.VirtualScreen;
                if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("No display is available.");
                using (var screen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(screen))
                        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                    using (var overlay = new CaptureOverlay(screen, bounds))
                    {
                        if (overlay.ShowDialog() != DialogResult.OK) return;
                        var selected = overlay.SelectedScreenBounds;
                        var local = new Rectangle(selected.Left - bounds.Left, selected.Top - bounds.Top,
                            selected.Width, selected.Height);
                        var crop = screen.Clone(local, PixelFormat.Format32bppArgb);
                        Translate(crop, selected, key, settings);
                    }
                }
            }
            catch (Exception ex) { ShowPopup(Rectangle.Empty, "Capture failed", ex.Message, null); }
        }

        private void Translate(Bitmap crop, Rectangle selected, string key, AppSettings currentSettings)
        {
            busy = true;
            ShowPopup(selected, "Reading text…", "", null);
            Task.Run(() =>
            {
                TranslationResult result = null;
                string error = null;
                try { result = interpreter.Interpret(crop, key, currentSettings); }
                catch (Exception ex) { error = ex.Message; }
                finally { crop.Dispose(); }
                if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated)
                {
                    try { dispatcher.BeginInvoke((Action)(() => TranslationFinished(selected, result, error))); }
                    catch (InvalidOperationException) { /* App closed while the request was finishing. */ }
                }
            });
        }

        private void TranslationFinished(Rectangle selected, TranslationResult result, string error)
        {
            busy = false;
            if (!String.IsNullOrEmpty(error))
            { ShowPopup(selected, "Could not translate", error, null); return; }
            if (result == null || String.IsNullOrWhiteSpace(result.Text))
            { ShowPopup(selected, "No Chinese text found", "Try a tighter or clearer selection.", null); return; }
            try { HistoryStore.Add(result); }
            catch (Exception ex) { tray.ShowBalloonTip(4000, "History was not saved", ex.Message, ToolTipIcon.Warning); }
            ShowPopup(selected, "ZHONGWEN SNAP", "", result);
        }

        private void ShowPopup(Rectangle selected, string title, string message, TranslationResult result)
        {
            ClosePopup();
            if (selected == Rectangle.Empty)
                selected = new Rectangle(Cursor.Position.X, Cursor.Position.Y, 1, 1);
            var conversation = result == null ? null :
                new FollowUpConversation(result, new OpenAiTutor(TryLoadKey(), settings.Model));
            popup = new ResultPopup(selected, title, message, result, ShowHistory, conversation);
            popup.Show();
        }

        private void ClosePopup()
        {
            if (popup != null && !popup.IsDisposed) popup.Close();
            popup = null;
        }

        protected override void ExitThreadCore()
        {
            ClosePopup();
            tray.Visible = false;
            tray.Dispose();
            trayIcon.Dispose();
            hotkey.Dispose();
            dispatcher.Dispose();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            if (historyForm != null && !historyForm.IsDisposed) historyForm.Close();
            base.ExitThreadCore();
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            uint mods, key;
            string error;
            if (!HotkeyParser.TryParse("Ctrl+Alt+T", out mods, out key, out error) || mods != 3 || key != 84)
                throw new Exception("Hotkey parse failed");
            using (var window = new HotkeyWindow())
                if (!window.Change("Ctrl+Alt+F24", out error)) throw new Exception("Hotkey registration failed: " + error);
            var request = new JavaScriptSerializer().DeserializeObject(
                OpenAiImageInterpreter.BuildRequest("AAAA", new AppSettings())) as Dictionary<string, object>;
            if (request == null || !request.ContainsKey("store") || (bool)request["store"] ||
                Convert.ToString(request["model"]) != "gpt-6-luna")
                throw new Exception("API request failed");
            var reasoning = request["reasoning"] as Dictionary<string, object>;
            if (reasoning == null || Convert.ToString(reasoning["effort"]) != "low")
                throw new Exception("Reasoning configuration failed");
            var sample = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"text\\\":\\\"捷運\\\",\\\"pinyin\\\":\\\"jié yùn\\\",\\\"english\\\":\\\"metro\\\",\\\"note\\\":\\\"Taiwan usage\\\"}\"}]}]}";
            var result = OpenAiImageInterpreter.ParseResponse(sample);
            if (result.Text != "捷運" || result.English != "metro") throw new Exception("API response parse failed");
            TestMigration();
            var entropy = Encoding.UTF8.GetBytes("test");
            var secret = Encoding.UTF8.GetBytes("sample-secret");
            try
            {
                var encrypted = ProtectedData.Protect(secret, entropy, DataProtectionScope.CurrentUser);
                var decrypted = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
                if (Encoding.UTF8.GetString(decrypted) != "sample-secret") throw new Exception("Key protection failed");
                Console.WriteLine("Self-test passed: hotkey, API format, response parsing, migration, and key protection.");
            }
            catch (CryptographicException)
            {
                Console.WriteLine("Self-test passed: hotkey, API format, response parsing, and migration. Key protection unavailable in this session.");
            }
        }

        private static void TestMigration()
        {
            var root = Path.Combine(Path.GetTempPath(), "ZhongWenSnap-test-" + Guid.NewGuid().ToString("N"));
            var oldDirectory = Path.Combine(root, "old");
            var newDirectory = Path.Combine(root, "new");
            Directory.CreateDirectory(oldDirectory);
            Directory.CreateDirectory(newDirectory);
            try
            {
                foreach (var name in new[] { "settings.json", "key.bin", "history.jsonl" })
                    File.WriteAllText(Path.Combine(oldDirectory, name), "old");
                File.WriteAllText(Path.Combine(newDirectory, "settings.json"), "new");
                SettingsStore.CopyMissingFiles(oldDirectory, newDirectory);
                if (File.ReadAllText(Path.Combine(newDirectory, "settings.json")) != "new" ||
                    File.ReadAllText(Path.Combine(newDirectory, "key.bin")) != "old" ||
                    File.ReadAllText(Path.Combine(newDirectory, "history.jsonl")) != "old")
                    throw new Exception("Data migration failed");
            }
            finally
            {
                foreach (var name in new[] { "settings.json", "key.bin", "history.jsonl" })
                {
                    var oldFile = Path.Combine(oldDirectory, name);
                    var newFile = Path.Combine(newDirectory, name);
                    if (File.Exists(oldFile)) File.Delete(oldFile);
                    if (File.Exists(newFile)) File.Delete(newFile);
                }
                Directory.Delete(oldDirectory);
                Directory.Delete(newDirectory);
                Directory.Delete(root);
            }
        }
    }
}
