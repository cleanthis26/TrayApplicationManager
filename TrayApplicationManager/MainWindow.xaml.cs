using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace TrayApplicationManager
{
    enum ProcessStatus
    {
        ProgramPaused,
        Stopped,
        Checking,
        Running
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Constants
        private readonly string DIGITS_ONLY = "^[0-9]+$";

        // Settings
        private bool IsStartupApplication;
        private int CheckInterval;
        private string ProcessName;
        private bool UseProcessNameContains;
        private bool ManualCheckOnly;
        private bool ShowBalloonMessageOnStart;

        // Variables
        private TaskbarIcon Tb;
        private ProcessStatus CurrentStatus;
        private Process CurrentProcess;
        private Timer CheckProcessStatusTimer;

        private delegate Process CheckProcessStatusDelegate();
        private CheckProcessStatusDelegate CheckProcessStatus;

        private void SetWindowVisibility(bool visible)
        {
            this.ShowInTaskbar = visible;
            this.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        }

        private void LoadSettings()
        {
            IsStartupApplication = Properties.Settings.Default.IsStartupApplication;
            CheckInterval = Properties.Settings.Default.CheckInterval;
            CheckIntervalTextBox.Text = CheckInterval + "";
            ProcessName = Properties.Settings.Default.ProcessName;
            UseProcessNameContains = Properties.Settings.Default.UseProcessNameContains;
            ManualCheckOnly = Properties.Settings.Default.ManualCheckOnly;
            ShowBalloonMessageOnStart = Properties.Settings.Default.ShowBalloonMessageOnStart;

            // Set and check path if true, remove if false
            ConfigureStartupApplicationSettings();

            // Set the checking process
            if (UseProcessNameContains)
            {
                CheckProcessStatus = CheckProcessStatusContains;
            }
            else
            {
                CheckProcessStatus = CheckProcessStatusExact;
            }

            // If manual, it cannot be paused
            PauseMenuItem.IsEnabled = !ManualCheckOnly;
        }

        private void CancelSettings()
        {
            Properties.Settings.Default.Reload();
            LoadSettings();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
            LoadSettings();
            StartCheckProcess();
        }

        private void SetProcessStatus(ProcessStatus ps)
        {
            CurrentStatus = ps;
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            SetIconStatus(CurrentStatus);
        }

        private void SetIconStatus(ProcessStatus ps)
        {
            switch (ps)
            {
                case ProcessStatus.ProgramPaused:
                    Tb.Icon = Properties.Resources.ProcessPaused;
                    break;
                case ProcessStatus.Stopped:
                    Tb.Icon = Properties.Resources.ProcessStopped;
                    break;
                case ProcessStatus.Checking:
                    Tb.Icon = Properties.Resources.ProcessChecking;
                    break;
                case ProcessStatus.Running:
                    Tb.Icon = Properties.Resources.ProcessRunning;
                    break;
                default:
                    Tb.Icon = Properties.Resources.ProcessStopped;
                    break;
            }
        }

        private void ConfigureStartupApplicationSettings()
        {
            try
            {
                // Get the registry subkey
                RegistryKey startupRegistry = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                var startupRegistryVal = startupRegistry.GetValue(Properties.Resources.RegistryKeyName);

                if (IsStartupApplication)
                {
                    // Update value
                    startupRegistry.SetValue(Properties.Resources.RegistryKeyName, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                }
                else if (startupRegistryVal != null) // Not set and exists, delete it
                {
                    startupRegistry.DeleteValue(Properties.Resources.RegistryKeyName);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Failed to configure startup settings. Exception:{Environment.NewLine}{e.Message}");
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Get the taskbar icon object
            Tb = this.ProcessNotifyIcon;
            Tb.ToolTipText = Properties.Resources.ProgramName;

            // Set window title
            this.Title = Properties.Resources.ProgramName;

            // Initial setting
            CurrentStatus = ProcessStatus.Stopped;
            UpdateIcon();

            // Don't show window
            SetWindowVisibility(false);

            // Load the settings
            LoadSettings();

            // Start the checking process
            StartCheckProcess();

            if (ShowBalloonMessageOnStart)
            {
                Tb.ShowBalloonTip(Properties.Resources.ProgramName, "Started", BalloonIcon.Info);
            }
        }

        private void StartCheckProcess()
        {
            PauseMenuItem.Header = "Pause";
            if (ManualCheckOnly)
            {
                // If timer exists, dispose it
                if (CheckProcessStatusTimer != null)
                {
                    CheckProcessStatusTimer.Dispose();
                    CheckProcessStatusTimer = null;
                }
            }
            else
            {
                if (CheckProcessStatusTimer == null)
                {
                    CheckProcessStatusTimer = new Timer(CheckProcessStatusTimerCallback, new AutoResetEvent(false), 0, CheckInterval);
                }
                else
                {
                    CheckProcessStatusTimer.Change(0, CheckInterval);
                }
            }
        }

        private void PauseCheckProcess()
        {
            if (CheckProcessStatusTimer != null)
            {
                PauseMenuItem.Header = "Unpause";
                CheckProcessStatusTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void CheckProcessStatusTimerCallback(object state)
        {
            CheckProcess();
        }

        private void CheckProcess()
        {
            SetProcessStatus(ProcessStatus.Checking);
            CurrentProcess = CheckProcessStatus();
            if (CurrentProcess == null)
            {
                SetProcessStatus(ProcessStatus.Stopped);
            }
            else
            {
                SetProcessStatus(ProcessStatus.Running);
            }
        }

        // Check process by exact name and get the first
        private Process CheckProcessStatusExact()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            if (processes != null && processes.Any())
            {
                return processes.First();
            }
            return null;
        }

        // Check process if it contains and get the first
        private Process CheckProcessStatusContains()
        {
            var ps = Process.GetProcesses();

            Process proc = null;
            foreach (var p in ps)
            {
                if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(p.ProcessName, ProcessName, CompareOptions.IgnoreCase) >= 0)
                {
                    proc = p;
                    break;
                }
            }
            return proc;
        }

        // Icon DoubleClick
        private void ProcessNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            CurrentProcess = CheckProcessStatus();
            if (CurrentProcess != null)
            {
                CurrentProcess.Kill();
            }
            SetProcessStatus(ProcessStatus.Stopped);
        }

        // Icon MouseOver
        private void ProcessNotifyIcon_TrayMouseMove(object sender, RoutedEventArgs e)
        {
            if (CurrentStatus.Equals(ProcessStatus.ProgramPaused))
            {
                // If paused do nothing
                return;
            }

            CheckProcess();

            if (CheckProcessStatusTimer != null)
            {
                // Set the timer to check from after last check
                CheckProcessStatusTimer.Change(CheckInterval, CheckInterval);
            }
        }

        // Context menu methods
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void PauseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentStatus.Equals(ProcessStatus.ProgramPaused))
            {
                // Start time again
                StartCheckProcess();
            }
            else
            {
                PauseCheckProcess();
                SetProcessStatus(ProcessStatus.ProgramPaused);
            }
        }

        // Override closing
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            CancelSettings();
            SetWindowVisibility(false);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetWindowVisibility(true);
        }

        private void Setting_MouseEnter(object sender, RoutedEventArgs e)
        {
            HoverTB.Inlines.Clear();
            if (sender.Equals(this.IsStartupApplicationTB) || sender.Equals(this.IsStartupApplicationCB))
            {
                HoverTB.Inlines.Add(new Bold(new Run("Checked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("The program will automatically run when the computer starts.");
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new Bold(new Run("Unchecked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("The program will not run when the computer starts.");
            }
            else if (sender.Equals(this.CheckIntervalTextBlock) || sender.Equals(this.CheckIntervalTextBox))
            {
                HoverTB.Inlines.Add("Interval of the timer to check if the process is open in milliseconds. Ignored if manual check is enabled.");
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Minimum value: 100.");
            }
            else if (sender.Equals(this.ProcessNameTextBlock) || sender.Equals(this.ProcessNameTextBox))
            {
                HoverTB.Inlines.Add("The name of the process to check.");
            }
            else if (sender.Equals(this.UseProcessNameContainsTB) || sender.Equals(this.UseProcessNameContainsCB))
            {
                HoverTB.Inlines.Add(new Bold(new Run("Checked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Check process will check if the process contains the process name.");
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new Bold(new Run("Unchecked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Check process will check for exact process name.");
            }
            else if (sender.Equals(this.ManualCheckOnlyTB) || sender.Equals(this.ManualCheckOnlyCB))
            {
                HoverTB.Inlines.Add(new Bold(new Run("Checked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Mouse over the tray icon to check the process status.");
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new Bold(new Run("Unchecked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Timer will automatically check for the process status. (Mouse over also checks)");
            }
            else if (sender.Equals(this.ShowBalloonMessageOnStartTB) || sender.Equals(this.ShowBalloonMessageOnStartCB))
            {
                HoverTB.Inlines.Add(new Bold(new Run("Checked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Balloon message will appear when the program starts.");
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add(new Bold(new Run("Unchecked")));
                HoverTB.Inlines.Add(new LineBreak());
                HoverTB.Inlines.Add("Balloon message will not be shown when the program starts.");
            }
            else
            {
                this.HoverTB.Text = "Hover over a setting to see their description.";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Verify settings first
            if (Regex.IsMatch(CheckIntervalTextBox.Text, DIGITS_ONLY))
            {
                Properties.Settings.Default.CheckInterval = int.Parse(CheckIntervalTextBox.Text);
                if (Properties.Settings.Default.CheckInterval < 100)
                {
                    MessageBox.Show("Process Check Interval minimum value is 100.");
                    return;
                }
            }
            else
            {
                MessageBox.Show("Process Check Interval must be positive digits only.");
                return;
            }
            SaveSettings();
            SetWindowVisibility(false);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSettings();
            SetWindowVisibility(false);
        }

        private void GithubSourceTB_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
        }
    }
}
