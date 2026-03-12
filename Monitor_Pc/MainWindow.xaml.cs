using System.ComponentModel;
using System.Drawing;
using System.Windows;
using Monitor_Pc.Models;
using Monitor_Pc.ViewModels;

namespace Monitor_Pc
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _isExiting;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
        }

        // ── System Tray ──────────────────────────────────────────────────────

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text    = "Monitor PRO v5.0",
                Visible = false,
                Icon    = SystemIcons.Application
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("📊 Restaurar", null, (_, _) => RestoreFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("❌ Sair", null, (_, _) => { _isExiting = true; Close(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null) _trayIcon.Visible = false;
        }

        /// <summary>Show a balloon notification for critical alerts.</summary>
        public void ShowAlertBalloon(string title, string message)
        {
            if (_trayIcon is { Visible: true })
            {
                _trayIcon.ShowBalloonTip(3000, title, message,
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
        }

        // ── Window controls ──────────────────────────────────────────────────

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // Minimize to tray
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExiting && _trayIcon != null)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                _trayIcon.Visible = true;
                Hide();
                return;
            }

            if (DataContext is MainViewModel vm)
                vm.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            base.OnClosing(e);
        }
    }
}