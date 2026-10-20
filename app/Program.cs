using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AdaptiveForegroundOptimizer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool showWindow = true;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase)) showWindow = false;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\AdaptiveForegroundOptimizer.Native", out created))
            {
                if (!created)
                {
                    if (showWindow) SignalExisting(@"Local\AdaptiveForegroundOptimizer.Show");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Logger.Write("UI error: " + e.Exception);
                };

                try { Application.Run(new OptimizerContext(showWindow)); }
                catch (Exception ex)
                {
                    Logger.Write("Fatal error: " + ex);
                    MessageBox.Show("FocusQoS beklenmeyen bir hatayla durdu.\n\n" + ex.Message,
                        "FocusQoS", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void SignalExisting(string name)
        {
            try
            {
                EventWaitHandle handle = EventWaitHandle.OpenExisting(name);
                handle.Set();
                handle.Dispose();
            }
            catch { }
        }
    }

    internal sealed class OptimizerContext : ApplicationContext
    {
        private readonly object sync = new object();
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly EventWaitHandle showEvent;
        private readonly EventWaitHandle shutdownEvent;
        private readonly EventWaitHandle benchmarkPauseEvent;
        private readonly EventWaitHandle benchmarkResumeEvent;
        private readonly EventWaitHandle benchmarkOptimizedEvent;
        private readonly EventWaitHandle benchmarkEndEvent;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly System.Windows.Forms.Timer eventTimer;
        private readonly Thread worker;
        private Settings settings;
        private SettingsForm form;
        private StatusSnapshot snapshot = new StatusSnapshot();
        private volatile bool paused;
        private int previousForegroundPid;
        private readonly object tuningSync = new object();
        private readonly Dictionary<int, ProcessTuningState> activeStates = new Dictionary<int, ProcessTuningState>();
        private readonly Dictionary<int, ProcessTuningState> backgroundStates = new Dictionary<int, ProcessTuningState>();
        private readonly Dictionary<int, TimeSpan> cpuSamples = new Dictionary<int, TimeSpan>();
        private DateTime lastTrim = DateTime.MinValue;
        private bool pressureActive;
        private volatile bool benchmarkOverride;
        private DateTime pauseUntil = DateTime.MinValue;

        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "dwm", "explorer", "audiodg", "MsMpEng", "SecurityHealthService", "fontdrvhost"
        };

        private static readonly HashSet<string> AiHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ChatGPT", "ChatGPT Classic", "codex", "Claude", "Cursor", "Windsurf"
        };

        private static readonly HashSet<string> AiWorkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "codex", "codex-code-mode-host", "codex-computer-use-swift", "node", "node_repl"
        };

        private static readonly HashSet<string> BrowserHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera"
        };

        private static readonly string[] AiWindowKeywords =
        {
            "chatgpt", "codex", "claude", "gemini", "copilot", "perplexity", "grok"
        };

        internal OptimizerContext(bool showWindow)
        {
            Paths.Ensure();
            settings = Settings.Load();
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.Show");
            shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.Shutdown");
            benchmarkPauseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.BenchmarkPause");
            benchmarkResumeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.BenchmarkResume");
            benchmarkOptimizedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.BenchmarkOptimized");
            benchmarkEndEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AdaptiveForegroundOptimizer.BenchmarkEnd");

            statusItem = new ToolStripMenuItem("Başlatılıyor…");
            statusItem.Enabled = false;
            pauseItem = new ToolStripMenuItem("15 dakika duraklat");
            pauseItem.Click += delegate { TogglePause(); };
            startupItem = new ToolStripMenuItem("Windows ile başlat");
            startupItem.Checked = StartupManager.IsEnabled();
            startupItem.Click += delegate { SetStartup(!StartupManager.IsEnabled()); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem open = new ToolStripMenuItem("Kontrol merkezini aç");
            open.Click += delegate { ShowWindow(); };
            menu.Items.Add(open);
            menu.Items.Add(pauseItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Çıkış");
            exit.Click += delegate { Shutdown(); };
            menu.Items.Add(exit);

            tray = new NotifyIcon();
            tray.Icon = IconFactory.Create();
            tray.Text = "FocusQoS";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };

            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            worker = new Thread(MonitorLoop);
            worker.Name = "AFO Monitor";
            worker.IsBackground = true;
            worker.Start();

            eventTimer = new System.Windows.Forms.Timer();
            eventTimer.Interval = 500;
            eventTimer.Tick += delegate { CheckControlEvents(); };
            eventTimer.Start();
            if (showWindow) ShowWindow();
            Logger.Write("Native optimizer started, version " + Application.ProductVersion);
        }

        internal StatusSnapshot GetSnapshot()
        {
            lock (sync) return snapshot.Clone();
        }

        internal Settings GetSettings()
        {
            lock (sync) return settings.Clone();
        }

        internal void SaveSettings(Settings value)
        {
            lock (sync) settings = value;
            value.Save();
            Logger.Write("Settings updated");
        }

        internal bool IsPaused { get { return paused; } }

        internal void SetUiVisible(bool visible)
        {
            try { Process.GetCurrentProcess().PriorityClass = visible ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.BelowNormal; }
            catch { }
        }

        internal void SetPaused(bool value)
        {
            paused = value;
            pauseUntil = value ? DateTime.Now.AddMinutes(15) : DateTime.MinValue;
            pauseItem.Text = value ? "Devam et" : "15 dakika duraklat";
            if (value) RestoreAll();
            Logger.Write(value ? "Paused" : "Resumed");
        }

        private void TogglePause()
        {
            if (paused) SetPaused(false);
            else SetPaused(true);
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                StartupManager.SetEnabled(enabled);
                startupItem.Checked = enabled;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Otomatik başlatma ayarı değiştirilemedi.\n\n" + ex.Message,
                    "FocusQoS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CheckControlEvents()
        {
            if (benchmarkPauseEvent.WaitOne(0)) SetPaused(true);
            if (benchmarkResumeEvent.WaitOne(0)) SetPaused(false);
            if (benchmarkOptimizedEvent.WaitOne(0)) { benchmarkOverride = true; SetPaused(false); }
            if (benchmarkEndEvent.WaitOne(0)) { benchmarkOverride = false; RestoreAll(); }
            if (paused && pauseUntil != DateTime.MinValue && DateTime.Now >= pauseUntil) SetPaused(false);
            if (shutdownEvent.WaitOne(0))
            {
                Shutdown();
                return;
            }
            if (showEvent.WaitOne(0))
            {
                ShowWindow();
            }
            UpdateTray(GetSnapshot());
        }

        private void ShowWindow()
        {
            SetUiVisible(true);
            if (form == null || form.IsDisposed) form = new SettingsForm(this);
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }

        private void MonitorLoop()
        {
            while (!stopEvent.WaitOne(GetSettings().PollMilliseconds))
            {
                try { MonitorTick(); }
                catch (Exception ex) { Logger.Write("Monitor tick error: " + ex.Message); }
            }
        }

        private void MonitorTick()
        {
            MemoryInfo memory = NativeMethods.GetMemory();
            int foregroundPid = NativeMethods.GetForegroundProcessId();
            Process foreground = SafeGetProcess(foregroundPid);
            string foregroundName = foreground == null ? "—" : foreground.ProcessName;
            string foregroundTitle = NativeMethods.GetForegroundWindowTitle();

            Settings current;
            lock (sync) current = settings.Clone();
            current.ApplyProfileDefaults(memory.TotalGb, false);

            int optimizedCount = 0;
            bool aiFocus = false;
            string mode = current.ProfileDisplayName + " · Normal";
            if (paused)
            {
                mode = current.ProfileDisplayName + " · Duraklatıldı";
            }
            else
            {
                ProcessTree tree = NativeMethods.GetProcessTree();
                int aiRoot = FindAiRoot(foregroundPid, tree);
                if (aiRoot == 0 && IsBrowserAiWindow(foregroundName, foregroundTitle)) aiRoot = FindBrowserRoot(foregroundPid, tree, foregroundName);
                aiFocus = current.EnableAiFocus && aiRoot > 0;
                HashSet<int> desiredActive = aiFocus
                    ? SelectActiveAiProcesses(aiRoot, foregroundPid, tree, current.AiHotProcessLimit)
                    : new HashSet<int>(foregroundPid > 0 ? new[] { foregroundPid } : new int[0]);
                RestoreMissing(activeStates, desiredActive);
                foreach (int pid in desiredActive)
                {
                    if (pid == Process.GetCurrentProcess().Id) continue;
                    if (ApplyTuning(activeStates, pid, ProcessPriorityClass.AboveNormal, 5, false)) optimizedCount++;
                }
                previousForegroundPid = foregroundPid;
                if (aiFocus) mode = current.ProfileDisplayName + " · AI odağı";

                if (benchmarkOverride)
                {
                    pressureActive = false;
                    mode = current.ProfileDisplayName + " · Etki testi";
                    ApplyPressurePolicy(current, desiredActive, false);
                }
                int effectivePressure = aiFocus ? Math.Max(current.PressurePercent, memory.TotalGb <= 8 ? 38 : 28) : current.PressurePercent;
                int effectiveRecovery = aiFocus ? Math.Max(current.RecoveryPercent, memory.TotalGb <= 8 ? 46 : 34) : current.RecoveryPercent;
                if (!benchmarkOverride && !pressureActive && memory.AvailablePercent <= effectivePressure)
                {
                    pressureActive = true;
                    Logger.Write("Memory pressure started at " + memory.AvailablePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                }
                else if (!benchmarkOverride && pressureActive && memory.AvailablePercent >= effectiveRecovery)
                {
                    pressureActive = false;
                    RestoreDictionary(backgroundStates);
                    Logger.Write("Memory pressure ended at " + memory.AvailablePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                }

                if (pressureActive)
                {
                    bool critical = memory.AvailablePercent <= current.CriticalPercent;
                    mode = current.ProfileDisplayName + (critical ? " · Kritik bellek" : (aiFocus ? " · AI + bellek odağı" : " · Bellek baskısı"));
                    ApplyPressurePolicy(current, desiredActive, critical);
                }
            }

            lock (sync)
            {
                snapshot = new StatusSnapshot
                {
                    Timestamp = DateTime.Now,
                    Mode = mode,
                    Profile = current.ProfileName,
                    ForegroundName = foregroundName,
                    ForegroundPid = foregroundPid,
                    AvailableGb = memory.AvailableGb,
                    TotalGb = memory.TotalGb,
                    AvailablePercent = memory.AvailablePercent,
                    AiFocus = aiFocus,
                    OptimizedProcessCount = optimizedCount
                };
            }
            WriteStatusFile(snapshot);
            if (foreground != null) foreground.Dispose();
        }

        private void ApplyPressurePolicy(Settings current, HashSet<int> activePids, bool critical)
        {
            foreach (string name in current.CandidateNames)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); } catch { continue; }
                foreach (Process process in processes)
                {
                    try
                    {
                        if (activePids.Contains(process.Id) || Protected.Contains(process.ProcessName)) continue;
                        ApplyTuning(backgroundStates, process.Id, ProcessPriorityClass.BelowNormal, 4, true);
                        if (critical && current.EnableWorkingSetTrim && (DateTime.Now - lastTrim).TotalSeconds >= current.TrimCooldownSeconds)
                            NativeMethods.EmptyWorkingSet(process.Handle);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            if (critical && current.EnableWorkingSetTrim && (DateTime.Now - lastTrim).TotalSeconds >= current.TrimCooldownSeconds)
            {
                lastTrim = DateTime.Now;
                Logger.Write("Background working sets trimmed");
            }
        }

        private int FindAiRoot(int foregroundPid, ProcessTree tree)
        {
            if (foregroundPid <= 0 || !tree.Names.ContainsKey(foregroundPid)) return 0;
            int cursor = foregroundPid;
            int found = AiHosts.Contains(tree.Names[cursor]) || AiWorkers.Contains(tree.Names[cursor]) ? cursor : 0;
            HashSet<int> seen = new HashSet<int>();
            while (tree.Parents.ContainsKey(cursor) && seen.Add(cursor))
            {
                int parent = tree.Parents[cursor];
                if (!tree.Names.ContainsKey(parent)) break;
                if (AiHosts.Contains(tree.Names[parent]) || AiWorkers.Contains(tree.Names[parent])) found = parent;
                else if (found > 0) break;
                cursor = parent;
            }
            return found;
        }

        private static bool IsBrowserAiWindow(string processName, string title)
        {
            if (!BrowserHosts.Contains(processName) || string.IsNullOrEmpty(title)) return false;
            foreach (string keyword in AiWindowKeywords)
                if (title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static int FindBrowserRoot(int foregroundPid, ProcessTree tree, string browserName)
        {
            int root = foregroundPid;
            int cursor = foregroundPid;
            HashSet<int> seen = new HashSet<int>();
            while (tree.Parents.ContainsKey(cursor) && seen.Add(cursor))
            {
                int parent = tree.Parents[cursor];
                string name;
                if (!tree.Names.TryGetValue(parent, out name) || !name.Equals(browserName, StringComparison.OrdinalIgnoreCase)) break;
                root = parent;
                cursor = parent;
            }
            return root;
        }

        private HashSet<int> SelectActiveAiProcesses(int root, int foregroundPid, ProcessTree tree, int hotLimit)
        {
            List<int> family = tree.Descendants(root);
            HashSet<int> selected = new HashSet<int>();
            selected.Add(root);
            selected.Add(foregroundPid);
            List<KeyValuePair<int, double>> hot = new List<KeyValuePair<int, double>>();
            foreach (int pid in family)
            {
                string name;
                if (tree.Names.TryGetValue(pid, out name) && AiWorkers.Contains(name)) selected.Add(pid);
                Process process = SafeGetProcess(pid);
                if (process == null) continue;
                try
                {
                    TimeSpan now = process.TotalProcessorTime;
                    TimeSpan old;
                    double delta = cpuSamples.TryGetValue(pid, out old) ? (now - old).TotalMilliseconds : 0;
                    cpuSamples[pid] = now;
                    if (delta > 2) hot.Add(new KeyValuePair<int, double>(pid, delta));
                }
                catch { }
                finally { process.Dispose(); }
            }
            foreach (KeyValuePair<int, double> item in hot.OrderByDescending(x => x.Value).Take(Math.Max(1, hotLimit))) selected.Add(item.Key);
            return selected;
        }

        private void RestoreAll()
        {
            RestoreDictionary(activeStates);
            RestoreDictionary(backgroundStates);
            previousForegroundPid = 0;
            pressureActive = false;
        }

        private bool ApplyTuning(Dictionary<int, ProcessTuningState> states, int pid, ProcessPriorityClass priority, uint memoryPriority, bool ecoQoS)
        {
            Process process = SafeGetProcess(pid);
            if (process == null) return false;
            try
            {
                if (Protected.Contains(process.ProcessName)) return false;
                lock (tuningSync)
                {
                    if (!states.ContainsKey(pid)) states[pid] = ProcessTuningState.Capture(process);
                    process.PriorityClass = priority;
                    NativeMethods.SetMemoryPriority(process.Handle, memoryPriority);
                    NativeMethods.SetEcoQoS(process.Handle, ecoQoS);
                }
                return true;
            }
            catch { return false; }
            finally { process.Dispose(); }
        }

        private void RestoreMissing(Dictionary<int, ProcessTuningState> states, HashSet<int> keep)
        {
            List<int> remove;
            lock (tuningSync) remove = states.Keys.Where(pid => !keep.Contains(pid)).ToList();
            foreach (int pid in remove) RestoreOne(states, pid);
        }

        private void RestoreDictionary(Dictionary<int, ProcessTuningState> states)
        {
            List<int> ids;
            lock (tuningSync) ids = states.Keys.ToList();
            foreach (int pid in ids) RestoreOne(states, pid);
        }

        private void RestoreOne(Dictionary<int, ProcessTuningState> states, int pid)
        {
            ProcessTuningState state;
            lock (tuningSync)
            {
                if (!states.TryGetValue(pid, out state)) return;
                states.Remove(pid);
            }
            Process process = SafeGetProcess(pid);
            if (process == null) return;
            try
            {
                process.PriorityClass = state.Priority;
                if (state.MemoryPriority > 0) NativeMethods.SetMemoryPriority(process.Handle, state.MemoryPriority);
                NativeMethods.SetPowerState(process.Handle, state.PowerState);
            }
            catch { }
            finally { process.Dispose(); }
        }

        private static Process SafeGetProcess(int pid)
        {
            if (pid <= 0) return null;
            try { return Process.GetProcessById(pid); } catch { return null; }
        }

        private void UpdateTray(StatusSnapshot value)
        {
            try
            {
                string text = value.Mode + " · RAM %" + value.AvailablePercent.ToString("0.0") + " · " + value.ForegroundName;
                statusItem.Text = text;
                tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
            catch { }
        }

        private static void WriteStatusFile(StatusSnapshot value)
        {
            try
            {
                string content = "timestamp=" + value.Timestamp.ToString("o") + Environment.NewLine +
                    "mode=" + value.Mode + Environment.NewLine +
                    "profile=" + value.Profile + Environment.NewLine +
                    "foreground=" + value.ForegroundName + Environment.NewLine +
                    "foreground_pid=" + value.ForegroundPid.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "available_ram_percent=" + value.AvailablePercent.ToString("0.0", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "available_ram_gb=" + value.AvailableGb.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "ai_focus=" + (value.AiFocus ? "1" : "0") + Environment.NewLine +
                    "optimized_processes=" + value.OptimizedProcessCount.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;
                File.WriteAllText(Paths.StatusFile, content, Encoding.UTF8);
            }
            catch { }
        }

        private void Shutdown()
        {
            stopEvent.Set();
            if (worker != null && worker.IsAlive) worker.Join(3000);
            RestoreAll();
            eventTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            showEvent.Dispose();
            shutdownEvent.Dispose();
            benchmarkPauseEvent.Dispose();
            benchmarkResumeEvent.Dispose();
            benchmarkOptimizedEvent.Dispose();
            benchmarkEndEvent.Dispose();
            stopEvent.Dispose();
            Logger.Write("Native optimizer stopped; priorities restored");
            ExitThread();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly OptimizerContext context;
        private readonly Label status;
        private readonly Label memory;
        private readonly Label foreground;
        private readonly CheckBox trim;
        private readonly CheckBox aiFocus;
        private readonly CheckBox startup;
        private readonly ComboBox profile;
        private readonly Label profileHelp;
        private readonly NumericUpDown pressure;
        private readonly NumericUpDown critical;
        private readonly NumericUpDown recovery;
        private readonly Button pause;
        private readonly System.Windows.Forms.Timer timer;

        internal SettingsForm(OptimizerContext value)
        {
            context = value;
            Text = "FocusQoS";
            ClientSize = new Size(520, 520);
            MinimumSize = new Size(536, 559);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = IconFactory.Create();
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(246, 248, 251);

            Label title = NewLabel("FocusQoS", 24, 20, 470, 32, 18F, FontStyle.Bold);
            Controls.Add(title);
            Controls.Add(NewLabel("Aktif AI uygulamasının çalışan süreçlerini öne alır; arka plan yükünü güvenle azaltır.", 26, 58, 460, 42, 9F, FontStyle.Regular));

            Panel card = new Panel();
            card.Location = new Point(24, 108);
            card.Size = new Size(472, 112);
            card.BackColor = Color.White;
            Controls.Add(card);
            status = NewLabel("Başlatılıyor…", 18, 14, 430, 25, 12F, FontStyle.Bold);
            memory = NewLabel("RAM: —", 18, 47, 210, 24, 9F, FontStyle.Regular);
            foreground = NewLabel("Ön plan: —", 18, 76, 430, 24, 9F, FontStyle.Regular);
            card.Controls.Add(status); card.Controls.Add(memory); card.Controls.Add(foreground);

            Settings s = context.GetSettings();
            Controls.Add(NewLabel("Optimizasyon modu", 28, 238, 120, 24, 9F, FontStyle.Bold));
            profile = new ComboBox { Location = new Point(150, 235), Size = new Size(180, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            profile.Items.AddRange(new object[] { "Güvenli", "Dengeli", "4–8 GB Düşük Bellek" });
            profile.SelectedIndex = s.ProfileName == "Safe" ? 0 : (s.ProfileName == "LowMemory" ? 2 : 1);
            profile.SelectedIndexChanged += delegate { ApplySelectedProfile(); };
            profileHelp = NewLabel("Dengeli kullanım", 345, 238, 150, 24, 8F, FontStyle.Regular);
            Controls.Add(profile); Controls.Add(profileHelp);

            aiFocus = new CheckBox { Text = "AI odağı: ChatGPT, Codex ve AI araçlarının süreç ailesini hızlandır", Location = new Point(28, 275), Size = new Size(460, 25), Checked = s.EnableAiFocus };
            trim = new CheckBox { Text = "Kritik durumda güvenli arka plan belleğini daralt", Location = new Point(28, 305), Size = new Size(430, 25), Checked = s.EnableWorkingSetTrim };
            startup = new CheckBox { Text = "Windows ile otomatik başlat", Location = new Point(28, 335), Size = new Size(300, 25), Checked = StartupManager.IsEnabled() };
            Controls.Add(aiFocus); Controls.Add(trim); Controls.Add(startup);

            Controls.Add(NewLabel("Baskı %", 28, 376, 80, 24, 9F, FontStyle.Regular));
            pressure = NewNumber(105, 373, s.PressurePercent);
            Controls.Add(pressure);
            Controls.Add(NewLabel("Kritik %", 190, 376, 75, 24, 9F, FontStyle.Regular));
            critical = NewNumber(263, 373, s.CriticalPercent);
            Controls.Add(critical);
            Controls.Add(NewLabel("Toparlanma %", 347, 376, 100, 24, 9F, FontStyle.Regular));
            recovery = NewNumber(441, 373, s.RecoveryPercent);
            recovery.Width = 54;
            Controls.Add(recovery);

            Button save = new Button { Text = "Kaydet", Location = new Point(28, 439), Size = new Size(105, 38) };
            save.Click += delegate { Save(); };
            Button benchmark = new Button { Text = "Etki testi", Location = new Point(142, 439), Size = new Size(105, 38) };
            benchmark.Click += delegate { LaunchBenchmark(); };
            pause = new Button { Text = "15 dk duraklat", Location = new Point(256, 439), Size = new Size(105, 38) };
            pause.Click += delegate { context.SetPaused(!context.IsPaused); RefreshStatus(); };
            Button hide = new Button { Text = "Tepsiye küçült", Location = new Point(370, 439), Size = new Size(122, 38) };
            hide.Click += delegate { context.SetUiVisible(false); Hide(); };
            Controls.Add(save); Controls.Add(benchmark); Controls.Add(pause); Controls.Add(hide);

            FormClosing += delegate(object sender, FormClosingEventArgs e) { e.Cancel = true; context.SetUiVisible(false); Hide(); };
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { RefreshStatus(); };
            timer.Start();
            RefreshStatus();
        }

        private void ApplySelectedProfile()
        {
            Settings s = context.GetSettings();
            s.ProfileName = profile.SelectedIndex == 0 ? "Safe" : (profile.SelectedIndex == 2 ? "LowMemory" : "Balanced");
            s.ApplyProfileDefaults(context.GetSnapshot().TotalGb, true);
            pressure.Value = Math.Max(pressure.Minimum, Math.Min(pressure.Maximum, s.PressurePercent));
            critical.Value = Math.Max(critical.Minimum, Math.Min(critical.Maximum, s.CriticalPercent));
            recovery.Value = Math.Max(recovery.Minimum, Math.Min(recovery.Maximum, s.RecoveryPercent));
            trim.Checked = s.EnableWorkingSetTrim;
            profileHelp.Text = s.ProfileName == "Safe" ? "En az müdahale" : (s.ProfileName == "LowMemory" ? "4–8 GB için" : "Önerilen");
        }

        private void LaunchBenchmark()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "FocusQoSBenchmark.exe");
            if (!File.Exists(path))
            {
                MessageBox.Show("Etki testi bileşeni bulunamadı. Kurulumu onarın.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Etki testi başlatılamadı.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static Label NewLabel(string text, int x, int y, int width, int height, float size, FontStyle style)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size, style) };
        }

        private static NumericUpDown NewNumber(int x, int y, int value)
        {
            NumericUpDown n = new NumericUpDown { Location = new Point(x, y), Size = new Size(62, 24), Minimum = 5, Maximum = 60, Value = Math.Max(5, Math.Min(60, value)) };
            return n;
        }

        private void RefreshStatus()
        {
            StatusSnapshot s = context.GetSnapshot();
            status.Text = s.Mode;
            status.ForeColor = s.Mode.IndexOf("Kritik", StringComparison.OrdinalIgnoreCase) >= 0 ? Color.Firebrick : Color.FromArgb(25, 92, 62);
            memory.Text = "Boş RAM: " + s.AvailableGb.ToString("0.00") + " / " + s.TotalGb.ToString("0.00") + " GB (%" + s.AvailablePercent.ToString("0.0") + ")";
            foreground.Text = "Ön plan: " + s.ForegroundName + (s.AiFocus ? " · AI ailesi: " + s.OptimizedProcessCount + " süreç" : "");
            pause.Text = context.IsPaused ? "Devam et" : "15 dk duraklat";
        }

        private void Save()
        {
            if (critical.Value >= pressure.Value || pressure.Value >= recovery.Value)
            {
                MessageBox.Show("Eşik sırası Kritik < Baskı < Toparlanma olmalıdır.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Settings s = context.GetSettings();
            s.ProfileName = profile.SelectedIndex == 0 ? "Safe" : (profile.SelectedIndex == 2 ? "LowMemory" : "Balanced");
            s.EnableWorkingSetTrim = trim.Checked;
            s.EnableAiFocus = aiFocus.Checked;
            s.PressurePercent = (int)pressure.Value;
            s.CriticalPercent = (int)critical.Value;
            s.RecoveryPercent = (int)recovery.Value;
            context.SaveSettings(s);
            StartupManager.SetEnabled(startup.Checked);
            MessageBox.Show("Ayarlar kaydedildi.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal sealed class Settings
    {
        internal string ProfileName = "Balanced";
        internal int PressurePercent;
        internal int CriticalPercent;
        internal int RecoveryPercent;
        internal int PollMilliseconds;
        internal int TrimCooldownSeconds;
        internal bool EnableWorkingSetTrim = true;
        internal bool EnableAiFocus = true;
        internal int AiHotProcessLimit = 4;
        internal List<string> CandidateNames = new List<string>
        {
            "steam", "steamwebhelper", "OneDrive", "OneDrive.Sync.Service", "PhoneExperienceHost",
            "Widgets", "WidgetService", "msedgewebview2", "EpicGamesLauncher", "Spotify", "Discord",
            "Creative Cloud", "CCXProcess", "FocusQoSBenchmarkWorker"
        };

        internal string ProfileDisplayName
        {
            get
            {
                if (ProfileName == "Safe") return "Güvenli";
                if (ProfileName == "LowMemory") return "4–8 GB";
                return "Dengeli";
            }
        }

        internal void ApplyProfileDefaults(double totalGb, bool force)
        {
            if (!force && PressurePercent > 0 && CriticalPercent > 0 && RecoveryPercent > 0 && PollMilliseconds > 0 && TrimCooldownSeconds > 0) return;
            if (ProfileName == "Safe")
            {
                PressurePercent = 12; CriticalPercent = 8; RecoveryPercent = 20;
                EnableWorkingSetTrim = false; PollMilliseconds = 3000; TrimCooldownSeconds = 180;
            }
            else if (ProfileName == "LowMemory")
            {
                PressurePercent = 30; CriticalPercent = 18; RecoveryPercent = 38;
                EnableWorkingSetTrim = true; PollMilliseconds = 1500; TrimCooldownSeconds = 60;
            }
            else
            {
                ProfileName = "Balanced";
                if (totalGb <= 6) { PressurePercent = 22; CriticalPercent = 14; RecoveryPercent = 30; }
                else if (totalGb <= 10) { PressurePercent = 20; CriticalPercent = 12; RecoveryPercent = 28; }
                else if (totalGb <= 20) { PressurePercent = 16; CriticalPercent = 10; RecoveryPercent = 22; }
                else { PressurePercent = 12; CriticalPercent = 8; RecoveryPercent = 18; }
                EnableWorkingSetTrim = true; PollMilliseconds = 2000; TrimCooldownSeconds = 90;
            }
        }

        internal Settings Clone()
        {
            Settings s = new Settings();
            s.ProfileName = ProfileName;
            s.PressurePercent = PressurePercent;
            s.CriticalPercent = CriticalPercent;
            s.RecoveryPercent = RecoveryPercent;
            s.PollMilliseconds = PollMilliseconds;
            s.TrimCooldownSeconds = TrimCooldownSeconds;
            s.EnableWorkingSetTrim = EnableWorkingSetTrim;
            s.EnableAiFocus = EnableAiFocus;
            s.AiHotProcessLimit = AiHotProcessLimit;
            s.CandidateNames = new List<string>(CandidateNames);
            return s;
        }

        internal static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(Paths.SettingsFile))
                {
                    s.ApplyProfileDefaults(NativeMethods.GetMemory().TotalGb, true);
                    s.Save();
                    return s;
                }
                foreach (string raw in File.ReadAllLines(Paths.SettingsFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int split = line.IndexOf('=');
                    if (split < 1) continue;
                    string key = line.Substring(0, split).Trim();
                    string val = line.Substring(split + 1).Trim();
                    int number;
                    if (key == "profile") s.ProfileName = val;
                    else if (key == "pressure" && int.TryParse(val, out number)) s.PressurePercent = number;
                    else if (key == "critical" && int.TryParse(val, out number)) s.CriticalPercent = number;
                    else if (key == "recovery" && int.TryParse(val, out number)) s.RecoveryPercent = number;
                    else if (key == "poll_ms" && int.TryParse(val, out number)) s.PollMilliseconds = number;
                    else if (key == "trim_cooldown" && int.TryParse(val, out number)) s.TrimCooldownSeconds = number;
                    else if (key == "trim") s.EnableWorkingSetTrim = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key == "ai_focus") s.EnableAiFocus = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key == "ai_hot_limit" && int.TryParse(val, out number)) s.AiHotProcessLimit = Math.Max(1, Math.Min(8, number));
                }
                s.ApplyProfileDefaults(NativeMethods.GetMemory().TotalGb, false);
                s.Save();
            }
            catch (Exception ex) { Logger.Write("Settings load error: " + ex.Message); }
            return s;
        }

        internal void Save()
        {
            Paths.Ensure();
            string text = "# FocusQoS settings\r\n" +
                "profile=" + ProfileName + "\r\npressure=" + PressurePercent + "\r\ncritical=" + CriticalPercent +
                "\r\nrecovery=" + RecoveryPercent + "\r\npoll_ms=" + PollMilliseconds +
                "\r\ntrim_cooldown=" + TrimCooldownSeconds + "\r\ntrim=" + (EnableWorkingSetTrim ? "1" : "0") +
                "\r\nai_focus=" + (EnableAiFocus ? "1" : "0") + "\r\nai_hot_limit=" + AiHotProcessLimit + "\r\n";
            File.WriteAllText(Paths.SettingsFile, text, Encoding.UTF8);
        }
    }

    internal sealed class StatusSnapshot
    {
        internal DateTime Timestamp;
        internal string Mode = "Başlatılıyor";
        internal string Profile = "Balanced";
        internal string ForegroundName = "—";
        internal int ForegroundPid;
        internal double AvailableGb;
        internal double TotalGb;
        internal double AvailablePercent;
        internal bool AiFocus;
        internal int OptimizedProcessCount;
        internal StatusSnapshot Clone() { return (StatusSnapshot)MemberwiseClone(); }
    }

    internal sealed class ProcessTuningState
    {
        internal ProcessPriorityClass Priority;
        internal uint MemoryPriority;
        internal NativeMethods.PROCESS_POWER_THROTTLING_STATE PowerState;

        internal static ProcessTuningState Capture(Process process)
        {
            ProcessTuningState state = new ProcessTuningState();
            state.Priority = process.PriorityClass;
            state.MemoryPriority = NativeMethods.GetMemoryPriority(process.Handle);
            state.PowerState = NativeMethods.GetPowerState(process.Handle);
            return state;
        }
    }

    internal sealed class ProcessTree
    {
        internal readonly Dictionary<int, int> Parents = new Dictionary<int, int>();
        internal readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        internal List<int> Descendants(int root)
        {
            List<int> result = new List<int>();
            Queue<int> queue = new Queue<int>();
            HashSet<int> seen = new HashSet<int>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!seen.Add(current)) continue;
                result.Add(current);
                foreach (KeyValuePair<int, int> pair in Parents)
                    if (pair.Value == current && !seen.Contains(pair.Key)) queue.Enqueue(pair.Key);
            }
            return result;
        }
    }

    internal static class StartupManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FocusQoS";

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key != null && key.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        internal static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --background");
                else key.DeleteValue(ValueName, false);
            }
        }
    }

    internal static class Paths
    {
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusQoS");
        internal static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.ini");
        internal static readonly string StatusFile = Path.Combine(DataDirectory, "status.txt");
        internal static readonly string LogFile = Path.Combine(DataDirectory, "optimizer.log");
        internal static void Ensure() { Directory.CreateDirectory(DataDirectory); }
    }

    internal static class Logger
    {
        private static readonly object Sync = new object();
        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Paths.Ensure();
                    if (File.Exists(Paths.LogFile) && new FileInfo(Paths.LogFile).Length > 1024 * 1024)
                    {
                        string old = Paths.LogFile + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(Paths.LogFile, old);
                    }
                    File.AppendAllText(Paths.LogFile, DateTime.Now.ToString("u") + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    internal static class IconFactory
    {
        internal static Icon Create()
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(39, 111, 191), Color.FromArgb(30, 178, 120), 45F))
                    g.FillEllipse(brush, 1, 1, 30, 30);
                using (Pen pen = new Pen(Color.White, 3F))
                {
                    g.DrawLine(pen, 8, 21, 13, 15);
                    g.DrawLine(pen, 13, 15, 18, 18);
                    g.DrawLine(pen, 18, 18, 24, 9);
                }
            }
            IntPtr handle = bitmap.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(handle).Clone();
            NativeMethods.DestroyIcon(handle);
            bitmap.Dispose();
            return icon;
        }
    }

    internal sealed class MemoryInfo
    {
        internal double AvailableGb;
        internal double TotalGb;
        internal double AvailablePercent;
    }

    internal static class NativeMethods
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        private enum PROCESS_INFORMATION_CLASS
        {
            ProcessMemoryPriority = 0,
            ProcessPowerThrottling = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_PRIORITY_INFORMATION
        {
            internal uint MemoryPriority;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_POWER_THROTTLING_STATE
        {
            internal uint Version;
            internal uint ControlMask;
            internal uint StateMask;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            internal uint dwSize;
            internal uint cntUsage;
            internal uint th32ProcessID;
            internal IntPtr th32DefaultHeapID;
            internal uint th32ModuleID;
            internal uint cntThreads;
            internal uint th32ParentProcessID;
            internal int pcPriClassBase;
            internal uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            internal uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            internal uint dwMemoryLoad;
            internal ulong ullTotalPhys;
            internal ulong ullAvailPhys;
            internal ulong ullTotalPageFile;
            internal ulong ullAvailPageFile;
            internal ulong ullTotalVirtual;
            internal ulong ullAvailVirtual;
            internal ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessInformation")] private static extern bool GetMemoryInformation(IntPtr process, PROCESS_INFORMATION_CLASS infoClass, ref MEMORY_PRIORITY_INFORMATION info, uint size);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")] private static extern bool SetMemoryInformation(IntPtr process, PROCESS_INFORMATION_CLASS infoClass, ref MEMORY_PRIORITY_INFORMATION info, uint size);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessInformation")] private static extern bool GetPowerInformation(IntPtr process, PROCESS_INFORMATION_CLASS infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetProcessInformation")] private static extern bool SetPowerInformation(IntPtr process, PROCESS_INFORMATION_CLASS infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("psapi.dll", SetLastError = true)] internal static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern bool DestroyIcon(IntPtr handle);

        internal static int GetForegroundProcessId()
        {
            IntPtr window = GetForegroundWindow();
            uint pid;
            if (window == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(window, out pid);
            return (int)pid;
        }

        internal static string GetForegroundWindowTitle()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return string.Empty;
            StringBuilder title = new StringBuilder(512);
            return GetWindowText(window, title, title.Capacity) > 0 ? title.ToString() : string.Empty;
        }

        internal static MemoryInfo GetMemory()
        {
            MEMORYSTATUSEX value = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(value)) return new MemoryInfo();
            return new MemoryInfo
            {
                AvailableGb = value.ullAvailPhys / 1073741824.0,
                TotalGb = value.ullTotalPhys / 1073741824.0,
                AvailablePercent = value.ullTotalPhys == 0 ? 0 : value.ullAvailPhys * 100.0 / value.ullTotalPhys
            };
        }

        internal static ProcessTree GetProcessTree()
        {
            ProcessTree tree = new ProcessTree();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == new IntPtr(-1)) return tree;
            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32();
                entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snapshot, ref entry)) return tree;
                do
                {
                    int pid = unchecked((int)entry.th32ProcessID);
                    tree.Parents[pid] = unchecked((int)entry.th32ParentProcessID);
                    tree.Names[pid] = Path.GetFileNameWithoutExtension(entry.szExeFile ?? string.Empty);
                    entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            return tree;
        }

        internal static uint GetMemoryPriority(IntPtr process)
        {
            MEMORY_PRIORITY_INFORMATION info = new MEMORY_PRIORITY_INFORMATION();
            try
            {
                return GetMemoryInformation(process, PROCESS_INFORMATION_CLASS.ProcessMemoryPriority, ref info,
                    (uint)Marshal.SizeOf(typeof(MEMORY_PRIORITY_INFORMATION))) ? info.MemoryPriority : 5;
            }
            catch (EntryPointNotFoundException) { return 5; }
        }

        internal static bool SetMemoryPriority(IntPtr process, uint priority)
        {
            MEMORY_PRIORITY_INFORMATION info = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = priority };
            try
            {
                return SetMemoryInformation(process, PROCESS_INFORMATION_CLASS.ProcessMemoryPriority, ref info,
                    (uint)Marshal.SizeOf(typeof(MEMORY_PRIORITY_INFORMATION)));
            }
            catch (EntryPointNotFoundException) { return false; }
        }

        internal static PROCESS_POWER_THROTTLING_STATE GetPowerState(IntPtr process)
        {
            PROCESS_POWER_THROTTLING_STATE info = new PROCESS_POWER_THROTTLING_STATE { Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION };
            try
            {
                GetPowerInformation(process, PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref info,
                    (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            catch (EntryPointNotFoundException) { }
            return info;
        }

        internal static bool SetEcoQoS(IntPtr process, bool enabled)
        {
            PROCESS_POWER_THROTTLING_STATE info = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enabled ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
            };
            return SetPowerState(process, info);
        }

        internal static bool SetPowerState(IntPtr process, PROCESS_POWER_THROTTLING_STATE info)
        {
            try
            {
                return SetPowerInformation(process, PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref info,
                    (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
}

