using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AFOBenchmark
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BenchmarkForm());
        }
    }

    internal sealed class BenchmarkForm : Form
    {
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly TextBox results;
        private readonly Button run;
        private readonly BackgroundWorker worker;

        internal BenchmarkForm()
        {
            Text = "FocusQoS Etki Testi";
            ClientSize = new Size(620, 470);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(246, 248, 251);

            Controls.Add(new Label { Text = "Ölçülebilir etki testi", Location = new Point(28, 22), Size = new Size(560, 36), Font = new Font("Segoe UI", 19F, FontStyle.Bold) });
            Controls.Add(new Label { Text = "İki adet 10 saniyelik yerel yük oluşturur. Önce optimizasyon kapalı, sonra açık durumda zamanlayıcı gecikmesini karşılaştırır. Ağ kullanmaz ve en fazla 128 MB test belleği ayırır.", Location = new Point(30, 70), Size = new Size(560, 62) });
            status = new Label { Text = "Hazır", Location = new Point(30, 145), Size = new Size(550, 28), Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            progress = new ProgressBar { Location = new Point(30, 180), Size = new Size(560, 22), Minimum = 0, Maximum = 100 };
            results = new TextBox { Location = new Point(30, 220), Size = new Size(560, 175), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), Text = "Test henüz çalıştırılmadı." };
            run = new Button { Text = "Etki testini başlat", Location = new Point(30, 414), Size = new Size(170, 38) };
            Button close = new Button { Text = "Kapat", Location = new Point(214, 414), Size = new Size(110, 38) };
            close.Click += delegate { Close(); };
            run.Click += delegate { StartTest(); };
            Controls.Add(status); Controls.Add(progress); Controls.Add(results); Controls.Add(run); Controls.Add(close);

            worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += RunBenchmark;
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e)
            {
                progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                status.Text = Convert.ToString(e.UserState);
            };
            worker.RunWorkerCompleted += Completed;
        }

        private void StartTest()
        {
            string workerPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "FocusQoSBenchmarkWorker.exe");
            if (!File.Exists(workerPath))
            {
                MessageBox.Show("Test işçisi bulunamadı. Kurulumu onarın.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            run.Enabled = false;
            results.Text = "Test başlatılıyor…";
            worker.RunWorkerAsync(workerPath);
        }

        private void RunBenchmark(object sender, DoWorkEventArgs e)
        {
            string workerPath = (string)e.Argument;
            BenchmarkResult result = new BenchmarkResult();
            try
            {
                Signal(@"Local\AdaptiveForegroundOptimizer.BenchmarkPause");
                worker.ReportProgress(5, "Temel ölçüm için optimizasyon duraklatılıyor…");
                Thread.Sleep(2500);
                result.Baseline = RunPhase(workerPath, "Optimizasyon kapalı", 10, 10, 45);

                Signal(@"Local\AdaptiveForegroundOptimizer.BenchmarkOptimized");
                worker.ReportProgress(50, "Optimizasyonlu ölçüm hazırlanıyor…");
                Thread.Sleep(2500);
                result.Optimized = RunPhase(workerPath, "Optimizasyon açık", 10, 55, 95);
                result.Timestamp = DateTime.Now;
                result.Save();
                e.Result = result;
            }
            finally
            {
                Signal(@"Local\AdaptiveForegroundOptimizer.BenchmarkEnd");
                Signal(@"Local\AdaptiveForegroundOptimizer.BenchmarkResume");
            }
        }

        private PhaseResult RunPhase(string workerPath, string name, int seconds, int startProgress, int endProgress)
        {
            worker.ReportProgress(startProgress, name + " — sentetik yük çalışıyor…");
            ProcessStartInfo info = new ProcessStartInfo(workerPath,
                "--seconds " + seconds + " --memory-mb 128 --threads " + Math.Min(64, Environment.ProcessorCount + 4));
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            Process load = Process.Start(info);

            List<double> delays = new List<double>();
            Stopwatch watch = Stopwatch.StartNew();
            double previous = watch.Elapsed.TotalMilliseconds;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(5);
                double now = watch.Elapsed.TotalMilliseconds;
                double delay = Math.Max(0, (now - previous) - 5.0);
                previous = now;
                delays.Add(delay);
                if (delays.Count % 100 == 0)
                {
                    int percent = startProgress + (int)((endProgress - startProgress) * watch.Elapsed.TotalSeconds / seconds);
                    worker.ReportProgress(Math.Min(endProgress, percent), name + " — " + watch.Elapsed.TotalSeconds.ToString("0") + "/" + seconds + " sn");
                }
            }
            if (!load.WaitForExit(3000)) try { load.Kill(); } catch { }
            load.Dispose();
            delays.Sort();
            int p95Index = Math.Max(0, Math.Min(delays.Count - 1, (int)(delays.Count * 0.95)));
            return new PhaseResult
            {
                Name = name,
                Samples = delays.Count,
                P95DelayMs = delays.Count == 0 ? 0 : delays[p95Index],
                MaxDelayMs = delays.Count == 0 ? 0 : delays[delays.Count - 1]
            };
        }

        private void Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            run.Enabled = true;
            progress.Value = 100;
            if (e.Error != null)
            {
                status.Text = "Test tamamlanamadı";
                results.Text = e.Error.ToString();
                return;
            }
            BenchmarkResult value = (BenchmarkResult)e.Result;
            status.Text = "Test tamamlandı";
            results.Text = value.ToDisplayText();
        }

        private static void Signal(string name)
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name)) handle.Set();
            }
            catch { }
        }
    }

    internal sealed class PhaseResult
    {
        internal string Name;
        internal int Samples;
        internal double P95DelayMs;
        internal double MaxDelayMs;
    }

    internal sealed class BenchmarkResult
    {
        internal DateTime Timestamp;
        internal PhaseResult Baseline;
        internal PhaseResult Optimized;

        internal double Improvement
        {
            get { return Baseline.P95DelayMs <= 0 ? 0 : (Baseline.P95DelayMs - Optimized.P95DelayMs) * 100.0 / Baseline.P95DelayMs; }
        }

        internal string ToDisplayText()
        {
            return "Optimizasyon kapalı P95 gecikme : " + Baseline.P95DelayMs.ToString("0.00") + " ms\r\n" +
                "Optimizasyon açık P95 gecikme  : " + Optimized.P95DelayMs.ToString("0.00") + " ms\r\n" +
                "Kapalı maksimum gecikme        : " + Baseline.MaxDelayMs.ToString("0.00") + " ms\r\n" +
                "Açık maksimum gecikme          : " + Optimized.MaxDelayMs.ToString("0.00") + " ms\r\n" +
                "P95 değişimi                    : %" + Improvement.ToString("0.0") + "\r\n\r\n" +
                "Pozitif değer daha düşük ön plan gecikmesi demektir. Bu sentetik testtir; gerçek sonuç donanım ve açık uygulamalara göre değişir.";
        }

        internal void Save()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusQoS", "benchmarks");
            Directory.CreateDirectory(directory);
            string text = "{\r\n" +
                "  \"timestamp\": \"" + Timestamp.ToString("o") + "\",\r\n" +
                "  \"baseline_p95_ms\": " + Baseline.P95DelayMs.ToString("0.000", CultureInfo.InvariantCulture) + ",\r\n" +
                "  \"optimized_p95_ms\": " + Optimized.P95DelayMs.ToString("0.000", CultureInfo.InvariantCulture) + ",\r\n" +
                "  \"baseline_max_ms\": " + Baseline.MaxDelayMs.ToString("0.000", CultureInfo.InvariantCulture) + ",\r\n" +
                "  \"optimized_max_ms\": " + Optimized.MaxDelayMs.ToString("0.000", CultureInfo.InvariantCulture) + ",\r\n" +
                "  \"improvement_percent\": " + Improvement.ToString("0.0", CultureInfo.InvariantCulture) + "\r\n}\r\n";
            string file = Path.Combine(directory, "benchmark-" + Timestamp.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(file, text, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "benchmark-latest.txt"), ToDisplayText(), Encoding.UTF8);
        }
    }
}

