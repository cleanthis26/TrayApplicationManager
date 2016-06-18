using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;

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
        // Settings
        private bool IsStartupApplication;
        private int CheckInterval;
        private string ProcessName;
        private bool UseProcessNameContains;
        private bool ManualCheckOnly;

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
            ProcessName = Properties.Settings.Default.ProcessName;
            UseProcessNameContains = Properties.Settings.Default.UseProcessNameContains;
            ManualCheckOnly = Properties.Settings.Default.ManualCheckOnly;

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
                    //TODO fix icon
                    Tb.Icon = Properties.Resources.ProcessChecking;
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

        public MainWindow()
        {
            InitializeComponent();

            Tb = this.ProcessNotifyIcon;
            Tb.ToolTipText = Properties.Resources.ProgramName;

            CurrentStatus = ProcessStatus.Stopped;
            UpdateIcon();

            // Don't show window
            SetWindowVisibility(false);

            LoadSettings();

            // Start the checking timer
            StartCheckProcess();

            Tb.ShowBalloonTip(Properties.Resources.ProgramName, "Started", BalloonIcon.Info);
        }

        private void StartCheckProcess()
        {
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

        private void StopCheckProcess()
        {
            if (CheckProcessStatusTimer != null)
            {
                CheckProcessStatusTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void CheckProcessStatusTimerCallback(object state)
        {
            Debug.WriteLine(DateTime.Now.ToString("dd/MM/yy HH:mm:ss"));
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
                if (p.ProcessName.Contains(ProcessName))
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
                PauseMenuItem.Header = "Pause";
                StartCheckProcess();
            }
            else
            {
                PauseMenuItem.Header = "Unpause";
                SetProcessStatus(ProcessStatus.ProgramPaused);
                StopCheckProcess();
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
    }
}
