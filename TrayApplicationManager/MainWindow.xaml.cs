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
        private bool SettingsUpdated;
        private bool ProgramPaused;
        private TaskbarIcon Tb;
        private ProcessStatus CurrentStatus;
        private Process CurrentProcess;
        private Timer CheckProcessStatusTimer;
        private Thread CheckProcessStatusThread;

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
            SettingsUpdated = true;
            StartCheckWorkThread();
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

            // Start the checking thread
            StartCheckWorkThread();

            // TODO: Implemenation of the timer instead of thread 
            // Start the checking timer
            //StartCheckWorkTimer();

            Tb.ShowBalloonTip(Properties.Resources.ProgramName, "Started", BalloonIcon.Info);
        }

        private void StartCheckWorkTimer()
        {

        }

        private void StartCheckWorkThread()
        {
            if (CheckProcessStatusThread != null)
            {
                while (CheckProcessStatusThread.IsAlive)
                {
                    // Wait for previous thread to exit
                    Thread.Sleep(CheckInterval);
                }
            }
            CheckProcessStatusThread = new Thread(CheckProcessStatusThreadWork);
            CheckProcessStatusThread.Start();
        }

        private void CheckProcessStatusThreadWork()
        {
            // Reset variables
            ProgramPaused = false;
            SettingsUpdated = false;

            if (UseProcessNameContains)
            {
                CheckProcessStatus = CheckProcessStatusContains;
            }
            else
            {
                CheckProcessStatus = CheckProcessStatusExact;
            }

            while (!(ProgramPaused || SettingsUpdated))
            {
                CheckProcess();
                Thread.Sleep(CheckInterval);
            }
        }

        private void CheckProcess()
        {
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

        private Process CheckProcessStatusExact()
        {
            SetProcessStatus(ProcessStatus.Checking);
            var processes = Process.GetProcessesByName(ProcessName);
            if (processes != null && processes.Any())
            {
                return processes.First();
            }
            return null;
        }

        private Process CheckProcessStatusContains()
        {
            SetProcessStatus(ProcessStatus.Checking);
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
        }

        // Context menu methods
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void PauseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ProgramPaused = !ProgramPaused;
            if (ProgramPaused)
            {
                PauseMenuItem.Header = "Unpause";
                SetProcessStatus(ProcessStatus.ProgramPaused);
            }
            else
            {
                // Restart thread
                PauseMenuItem.Header = "Pause";
                StartCheckWorkThread();
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
