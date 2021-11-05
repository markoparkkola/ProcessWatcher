using Microsoft.VisualBasic.Devices;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Management;

namespace ProcessWatcher
{
    public partial class MainForm : Form
    {
        private static readonly Settings.AppSettings AppSettings = new Settings.AppSettings(new List<Settings.Process>(), new List<Settings.Alert>());
        private static readonly ManualResetEvent Waitable = new ManualResetEvent(false);
        private static readonly Thread WorkerThread = new Thread(ProcessWatcherThread);
        private static bool IsClosing = false;
        private static bool Aborting = false;
        private static bool IsVisible = true;
        private static object Lockable = new object();
        private static readonly ulong TotalMemory;

        private static string AppSettingsFileName { get => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\weicco\ProcessWatcher\appSettings.json"; }

        static MainForm()
        {
            var directory = AppSettingsFileName.Substring(0, AppSettingsFileName.LastIndexOf('\\'));
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(AppSettingsFileName))
            {
                AppSettings = JsonConvert.DeserializeObject<Settings.AppSettings>(File.ReadAllText(AppSettingsFileName)) ??
                    new Settings.AppSettings(new List<Settings.Process>(), new List<Settings.Alert>());
            }

            TotalMemory = new ComputerInfo().AvailablePhysicalMemory;
        }

        public MainForm()
        {
            InitializeComponent();

            var screenArea = Screen.PrimaryScreen.WorkingArea;
            Left = screenArea.Width - 20 - Width;
            Top = screenArea.Height - 20 - Height;

            listView1.Columns[0].Width = listView1.Width - 20;
            WorkerThread.Start(this);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            PopulateNewWatchList();
            PopulateWatchTabs();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
            {
                lock (Lockable)
                {
                    AppSettings.Processes.Add(new Settings.Process((string)listView1.SelectedItems[0].Tag, listView1.SelectedItems[0].Text));
                }

                File.WriteAllText(AppSettingsFileName, JsonConvert.SerializeObject(AppSettings));
            }

            PopulateNewWatchList();
            PopulateWatchTabs();
        }

        private void PopulateNewWatchList()
        {
            var processes = Process.GetProcesses().Where(x => !AppSettings.Processes.Any(y => y.Name == x.ProcessName));
            var names = new List<(string Name, string Filename, Image Icon)>();

            foreach (var process in processes)
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.MainModule?.FileName))
                    {
                        FileVersionInfo info = FileVersionInfo.GetVersionInfo(process.MainModule.FileName);
                        if (!string.IsNullOrEmpty(info.FileDescription))
                        {
                            if (!names.Any(x => x.Filename == info.FileDescription))
                            {
                                names.Add((process.ProcessName, info.FileDescription, Icon.ExtractAssociatedIcon(process.MainModule.FileName)!.ToBitmap()));
                            }
                        }
                    }
                } catch { }
            }

            listView1.Items.Clear();
            imageList1.Images.Clear();

            listView1.Items.AddRange(names.OrderBy(x => x.Item1).Select(x =>
            {
                imageList1.Images.Add(x.Filename, x.Icon);
                var item = new ListViewItem(x.Filename, x.Filename);
                item.Tag = x.Name;
                return item;
            }).ToArray());
        }

        private void PopulateWatchTabs()
        {
            var removables = tabControl1.TabPages.OfType<TabPage>().Where(x => x != tabPage1).ToList();
            removables.ForEach(x => tabControl1.TabPages.Remove(x));
            tabControl1.TabPages.AddRange(AppSettings.Processes.Select(x =>
            {
                var processWatcherControl = new ProcessWatcherControl();
                processWatcherControl.Process = x;
                processWatcherControl.Dock = DockStyle.Fill;
                
                processWatcherControl.ControlClosed += (s, e) =>
                {
                    lock(Lockable)
                    {
                        AppSettings.Processes.Remove(x);
                        File.WriteAllText(AppSettingsFileName, JsonConvert.SerializeObject(AppSettings));
                    }

                    PopulateNewWatchList();
                    PopulateWatchTabs();
                };

                processWatcherControl.AlertSet += (s, e) =>
                {
                    lock (Lockable)
                    {
                        var alert = AppSettings.Alerts.FirstOrDefault(a => a.Process.Name == x.Name);
                        if (alert == null)
                        {
                            alert = new Settings.Alert(x, e.Count, e.CpuThreshold, e.MemoryThreshold);
                            AppSettings.Alerts.Add(alert);
                        }
                        else
                        {
                            alert.Count = e.Count;
                            alert.CpuThreshold = e.CpuThreshold;
                            alert.MemoryThreshold = e.MemoryThreshold;
                        }

                        File.WriteAllText(AppSettingsFileName, JsonConvert.SerializeObject(AppSettings));
                    }
                };

                var page = new TabPage(x.Description);
                page.Controls.Add(processWatcherControl);
                return page;
            }).ToArray());
        }

        private void RefreshValues(IEnumerable<RunningProcess> runningProcesses)
        {
            foreach (var process in runningProcesses)
            {
                var page = tabControl1.TabPages.OfType<TabPage>().FirstOrDefault(x => x.Text == process.Description);
                if (page != null)
                {
                    var control = (ProcessWatcherControl)page.Controls[0];
                    control.ForeColor = Color.Black;

                    if (AppSettings.Alerts.Any(x => x.Process.Name == process.Name && (x.Count < process.Count || x.CpuThreshold < process.CpuUsage || x.MemoryThreshold < process.MemoryUsage)))
                    {
                        if (control.Alert())
                        {
                            Show();
                            BringToFront();
                            tabControl1.SelectedTab = page;
                        }
                    }

                    control.Diagnostics = new ProcessDiagnostics(process.Count, process.CpuUsage, process.MemoryUsage);
                }
            }
        }

        private static void ProcessWatcherThread(object? state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            MainForm owner = (MainForm)state;
            List<RunningProcessInfo> runningProcesses = new List<RunningProcessInfo>();

            while (!Aborting)
            {
                lock (Lockable)
                {
                    if (!runningProcesses.All(x => AppSettings.Processes.Any(y => y.Name == x.Name)) ||
                        !AppSettings.Processes.All(x => runningProcesses.Any(y => y.Name == x.Name)))
                    {
                        runningProcesses = AppSettings.Processes.Select(x => new RunningProcessInfo(x.Name, x.Description, new PerformanceCounter("Process", "% Processor Time", x.Name, true))).ToList();
                    }
                }

                foreach (var runningProcess in runningProcesses)
                {
                    var process = Process.GetProcessesByName(runningProcess.Name);
                    runningProcess.Count = process.Count();
                    runningProcess.MemoryUsage = (float)TotalMemory / (float)process.Sum(x => x.VirtualMemorySize64) * 100f;
                }

                Waitable.WaitOne(IsVisible ? 1000 : 5000); // 1 sec when visible, 5 sec otherwise
                if (Aborting)
                {
                    break;
                }

                foreach (var runningProcess in runningProcesses)
                {
                    if (Process.GetProcessesByName(runningProcess.Name).Any())
                    {
                        var newValue = runningProcess.PerformanceCounter.NextValue();
                        runningProcess.CpuUsage = newValue / Environment.ProcessorCount;
                    }
                    else
                    {
                        runningProcess.CpuUsage = 0;
                    }
                }

                owner.tabControl1.BeginInvoke(owner.RefreshValues, runningProcesses.Cast<RunningProcess>());
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsClosing)
            {
                Aborting = true;
                Waitable.Set();
            }
            else
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IsClosing = true;
            Close();
        }

        private void showToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
            BringToFront();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            IsClosing = true;
            Close();
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            BringToFront();
        }

        private void MainForm_VisibleChanged(object sender, EventArgs e)
        {
            IsVisible = Visible;
        }
    }

    internal record RunningProcessInfo : RunningProcess
    {
        public RunningProcessInfo(string name, string description, PerformanceCounter performanceCounter)
            : base(name, description)
        {
            PerformanceCounter = performanceCounter;
            PreviousCpuUsage = Process.GetProcessesByName(name).Any() ? PerformanceCounter.NextValue() : 0;
        }

        public PerformanceCounter PerformanceCounter;
        public float PreviousCpuUsage;
    }
}