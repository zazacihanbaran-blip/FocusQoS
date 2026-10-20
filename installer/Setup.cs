using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AdaptiveForegroundOptimizerSetup
{
    internal static class Program
    {
        internal const string ProductName = "FocusQoS";
        internal const string Version = "2.2.0";
        internal static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusQoS");
        internal static readonly string LegacyInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdaptiveForegroundOptimizer");
        internal static readonly string AppPath = Path.Combine(InstallDir, "FocusQoS.exe");
        internal static readonly string BenchmarkPath = Path.Combine(InstallDir, "FocusQoSBenchmark.exe");
        internal static readonly string WorkerPath = Path.Combine(InstallDir, "FocusQoSBenchmarkWorker.exe");
        internal static readonly string UninstallPath = Path.Combine(InstallDir, "Uninstall.exe");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = false;
            bool silent = false;
            foreach (string arg in args)
            {
                if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (arg.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
            }
            if (uninstall)
            {
                if (!silent && MessageBox.Show(ProductName + " kaldırılsın mı?", ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { Installer.Uninstall(); if (!silent) MessageBox.Show("Uygulama kaldırıldı.", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { if (!silent) MessageBox.Show("Kaldırma tamamlanamadı.\n\n" + ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            if (silent)
            {
                try { Installer.Install(true); } catch { Environment.ExitCode = 1; }
                return;
            }
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly Button install;
        private readonly CheckBox startup;
        private readonly Label state;

        internal SetupForm()
        {
            Text = Program.ProductName + " Kurulum";
            ClientSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(246, 248, 251);

            Label title = new Label { Text = Program.ProductName, Location = new Point(32, 28), Size = new Size(490, 38), Font = new Font("Segoe UI", 19F, FontStyle.Bold) };
            Label description = new Label { Text = "Aktif ChatGPT, Codex ve diğer AI uygulamalarının süreç ailesine öncelik verir; güvenli arka plan yükünü azaltır.", Location = new Point(34, 78), Size = new Size(490, 52) };
            Panel box = new Panel { Location = new Point(32, 145), Size = new Size(496, 82), BackColor = Color.White };
            state = new Label { Text = File.Exists(Program.AppPath) ? "Sürüm yüklü — onarabilir veya güncelleyebilirsiniz." : "Kuruluma hazır", Location = new Point(18, 16), Size = new Size(450, 25), Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            Label detail = new Label { Text = "Yönetici yetkisi, sürücü, ağ bağlantısı veya hesap gerekmez.", Location = new Point(18, 48), Size = new Size(450, 22) };
            box.Controls.Add(state); box.Controls.Add(detail);
            startup = new CheckBox { Text = "Windows ile otomatik başlat", Checked = true, Location = new Point(36, 246), Size = new Size(260, 26) };
            install = new Button { Text = File.Exists(Program.AppPath) ? "Onar / Güncelle" : "Kur", Location = new Point(32, 292), Size = new Size(150, 40) };
            install.Click += InstallClick;
            Button close = new Button { Text = "Kapat", Location = new Point(195, 292), Size = new Size(120, 40) };
            close.Click += delegate { Close(); };
            Controls.Add(title); Controls.Add(description); Controls.Add(box); Controls.Add(startup); Controls.Add(install); Controls.Add(close);
        }

        private void InstallClick(object sender, EventArgs e)
        {
            install.Enabled = false;
            state.Text = "Kuruluyor…";
            Refresh();
            try
            {
                Installer.Install(startup.Checked);
                state.Text = "Kurulum tamamlandı";
                install.Text = "Onar / Güncelle";
                MessageBox.Show("Kurulum tamamlandı. Uygulama sistem tepsisinde çalışıyor.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                state.Text = "Kurulum başarısız";
                MessageBox.Show("Kurulum tamamlanamadı.\n\n" + ex.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { install.Enabled = true; }
        }
    }

    internal static class Installer
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FocusQoS";
        private const string LegacyUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AdaptiveForegroundOptimizer";

        internal static void Install(bool startup)
        {
            if (Environment.OSVersion.Version.Major < 10) throw new NotSupportedException("Windows 10 veya Windows 11 gereklidir.");
            StopNative();
            RemovePrototypeTask();
            Directory.CreateDirectory(Program.InstallDir);
            MigrateLegacySettings();
            string temporary = Program.AppPath + ".new";
            ExtractResource("OptimizerPayload", temporary);
            if (File.Exists(Program.AppPath)) File.Delete(Program.AppPath);
            File.Move(temporary, Program.AppPath);
            ExtractResource("BenchmarkPayload", Program.BenchmarkPath);
            ExtractResource("WorkerPayload", Program.WorkerPath);
            if (!string.Equals(Application.ExecutablePath, Program.UninstallPath, StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, Program.UninstallPath, true);

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                key.DeleteValue("AdaptiveForegroundOptimizer", false);
                if (startup) key.SetValue("FocusQoS", "\"" + Program.AppPath + "\" --background");
                else key.DeleteValue("FocusQoS", false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKey, false);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", Program.ProductName);
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", "FocusQoS Project");
                key.SetValue("InstallLocation", Program.InstallDir);
                key.SetValue("DisplayIcon", Program.AppPath);
                key.SetValue("UninstallString", "\"" + Program.UninstallPath + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            CreateShortcut();
            RemoveLegacyShortcuts();
            Process.Start(new ProcessStartInfo(Program.AppPath, "--background") { UseShellExecute = true });
        }

        internal static void Uninstall()
        {
            StopNative();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey)) key.DeleteValue("FocusQoS", false);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            string shortcut = GetShortcutPath();
            if (File.Exists(shortcut)) File.Delete(shortcut);
            string benchmarkShortcut = GetBenchmarkShortcutPath();
            if (File.Exists(benchmarkShortcut)) File.Delete(benchmarkShortcut);
            foreach (string file in Directory.Exists(Program.InstallDir) ? Directory.GetFiles(Program.InstallDir) : new string[0])
            {
                if (!string.Equals(file, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(file); } catch { }
            }
            ScheduleSelfDelete();
        }

        private static void StopNative()
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\AdaptiveForegroundOptimizer.Shutdown")) signal.Set();
                Thread.Sleep(1500);
            }
            catch { }
        }

        private static void RemovePrototypeTask()
        {
            try
            {
                RunHidden(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/End /TN \"Adaptive Foreground Optimizer\"");
                RunHidden(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Delete /TN \"Adaptive Foreground Optimizer\" /F");
            }
            catch { }
        }

        private static void MigrateLegacySettings()
        {
            try
            {
                string oldSettings = Path.Combine(Program.LegacyInstallDir, "settings.ini");
                string newSettings = Path.Combine(Program.InstallDir, "settings.ini");
                if (!File.Exists(newSettings) && File.Exists(oldSettings)) File.Copy(oldSettings, newSettings, false);
            }
            catch { }
        }

        private static void RemoveLegacyShortcuts()
        {
            try
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string oldApp = Path.Combine(programs, "Adaptive Foreground Optimizer.lnk");
                string oldTest = Path.Combine(programs, "Adaptive Foreground Optimizer Etki Testi.lnk");
                if (File.Exists(oldApp)) File.Delete(oldApp);
                if (File.Exists(oldTest)) File.Delete(oldTest);
            }
            catch { }
        }

        private static void RunHidden(string file, string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments);
            info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(info)) process.WaitForExit(5000);
        }

        private static void ExtractResource(string name, string target)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (source == null) throw new InvalidOperationException("Kurulum paketi bozuk: uygulama kaynağı bulunamadı.");
                using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write)) source.CopyTo(output);
            }
        }

        private static string GetShortcutPath()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(programs, Program.ProductName + ".lnk");
        }

        private static string GetBenchmarkShortcutPath()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Path.Combine(programs, Program.ProductName + " Etki Testi.lnk");
        }

        private static void CreateShortcut()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                CreateOneShortcut(shellType, shell, GetShortcutPath(), Program.AppPath, Program.ProductName);
                CreateOneShortcut(shellType, shell, GetBenchmarkShortcutPath(), Program.BenchmarkPath, Program.ProductName + " Etki Testi");
                Marshal.FinalReleaseComObject(shell);
            }
            catch { }
        }

        private static void CreateOneShortcut(Type shellType, object shell, string shortcutPath, string targetPath, string description)
        {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Program.InstallDir });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Marshal.FinalReleaseComObject(shortcut);
        }

        private static void ScheduleSelfDelete()
        {
            try
            {
                string command = "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + Application.ExecutablePath + "\" & rmdir /q \"" + Program.InstallDir + "\"";
                ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command);
                info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(info);
            }
            catch { }
        }
    }
}

