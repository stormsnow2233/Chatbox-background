using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LANChatAdmin
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _adminSecret = "admin666";
        private readonly string _backendUrl = "http://localhost:40001";
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private DispatcherTimer? _statusTimer;

        // 注册表自启项键名
        private const string RegKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "LANChatAdminDaemon";

        public MainWindow()
        {
            InitializeComponent();
            InitSystemTray();
            CheckInitialAutostartStatus();
            StartBackendStatusPolling();
        }

        private void InitSystemTray()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.SystemIcons.Shield;
            _notifyIcon.Text = "局域网核心控制台守护进程";
            _notifyIcon.Visible = true;

            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("打开控制面板", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("彻底销毁退出", null, (s, e) => { ShutdownApplication(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void CheckInitialAutostartStatus()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegKeyPath);
                if (key?.GetValue(AppName) != null)
                {
                    ToggleAutostart.IsChecked = true;
                    UpdateToggleBorderState(BorderToggleAutostart, true);
                }
                else
                {
                    UpdateToggleBorderState(BorderToggleAutostart, false);
                }
            }
            catch { }
        }

        private void ToggleAutostart_Click(object sender, RoutedEventArgs e)
        {
        }

        private async void ToggleAutostart_Checked(object sender, RoutedEventArgs e)
        {
            await HandleAutostartToggle(true);
        }

        private async void ToggleAutostart_Unchecked(object sender, RoutedEventArgs e)
        {
            await HandleAutostartToggle(false);
        }

        private async Task HandleAutostartToggle(bool newState)
        {
            UpdateToggleBorderState(BorderToggleAutostart, newState);

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegKeyPath, true);
                if (key == null)
                {
                    ToggleAutostart.Checked -= ToggleAutostart_Checked;
                    ToggleAutostart.Unchecked -= ToggleAutostart_Unchecked;
                    ToggleAutostart.IsChecked = !newState;
                    ToggleAutostart.Checked += ToggleAutostart_Checked;
                    ToggleAutostart.Unchecked += ToggleAutostart_Unchecked;
                    UpdateToggleBorderState(BorderToggleAutostart, ToggleAutostart.IsChecked == true);
                    ShowNotification("无法访问注册表，操作失败");
                    return;
                }

                if (newState)
                {
                    string currentExePath = Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory;
                    key.SetValue(AppName, $"\"{currentExePath}\"");
                    ShowNotification("开机自启链注册成功！");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    ShowNotification("成功从注册表自启项中抹除！");
                }
            }
            catch (Exception ex)
            {
                ToggleAutostart.Checked -= ToggleAutostart_Checked;
                ToggleAutostart.Unchecked -= ToggleAutostart_Unchecked;
                ToggleAutostart.IsChecked = !newState;
                ToggleAutostart.Checked += ToggleAutostart_Checked;
                ToggleAutostart.Unchecked += ToggleAutostart_Unchecked;
                UpdateToggleBorderState(BorderToggleAutostart, ToggleAutostart.IsChecked == true);
                ShowNotification($"注册表操作失败，可能受到安全软件拦截: {ex.Message}");
            }
        }

        private void StartBackendStatusPolling()
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += async (s, e) => await SyncWithGoBackend();
            _statusTimer.Start();
        }

        private async Task SyncWithGoBackend()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_backendUrl}/api/admin/status?secret={_adminSecret}");
                if (response.IsSuccessStatusCode)
                {
                    string jsonStr = await response.Content.ReadAsStringAsync();
                    var statusData = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                    bool isMuted = statusData.GetProperty("global_mute").GetBoolean();

                    TxtServerStatus.Text = "后端服务正常";
                    ToggleMute.IsChecked = isMuted;
                    UpdateToggleBorderState(BorderToggleMute, isMuted);
                }
            }
            catch
            {
                TxtServerStatus.Text = "后端断开或未启动";
            }
        }

        private async void ToggleMute_Checked(object sender, RoutedEventArgs e)
        {
            await HandleMuteToggle(true);
        }

        private async void ToggleMute_Unchecked(object sender, RoutedEventArgs e)
        {
            await HandleMuteToggle(false);
        }

        private async Task HandleMuteToggle(bool newState)
        {
            UpdateToggleBorderState(BorderToggleMute, newState);

            try
            {
                HttpResponseMessage resp = await _httpClient.GetAsync($"{_backendUrl}/api/admin/toggle-mute?secret={_adminSecret}");
                if (!resp.IsSuccessStatusCode)
                {
                    ToggleMute.Checked -= ToggleMute_Checked;
                    ToggleMute.Unchecked -= ToggleMute_Unchecked;
                    ToggleMute.IsChecked = !newState;
                    ToggleMute.Checked += ToggleMute_Checked;
                    ToggleMute.Unchecked += ToggleMute_Unchecked;
                    UpdateToggleBorderState(BorderToggleMute, ToggleMute.IsChecked == true);
                    ShowNotification("无法同步状态，请检查 Go 后端");
                }
            }
            catch
            {
                ToggleMute.Checked -= ToggleMute_Checked;
                ToggleMute.Unchecked -= ToggleMute_Unchecked;
                ToggleMute.IsChecked = !newState;
                ToggleMute.Checked += ToggleMute_Checked;
                ToggleMute.Unchecked += ToggleMute_Unchecked;
                UpdateToggleBorderState(BorderToggleMute, ToggleMute.IsChecked == true);
                ShowNotification("无法同步状态，请检查 Go 后端");
            }
        }

        private async void BtnBroadcast_Click(object sender, RoutedEventArgs e)
        {
            string content = TxtBroadcast.Text.Trim();
            if (string.IsNullOrEmpty(content) || content == "在此处键入需要广播的内容...") return;

            try
            {
                var payload = new { secret = _adminSecret, content = content };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var body = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{_backendUrl}/api/admin/broadcast", body);
                if (response.IsSuccessStatusCode)
                {
                    TxtBroadcast.Text = "";
                    ShowNotification("广播发送成功！");
                }
            }
            catch
            {
                ShowNotification("广播发送失败");
            }
        }

        private void UpdateToggleBorderState(System.Windows.Controls.Border? border, bool isChecked)
        {
            if (border == null) return;
            border.BorderBrush = isChecked ? System.Windows.Media.Brushes.LightSeaGreen : System.Windows.Media.Brushes.LightGray;
            border.Background = isChecked ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 32, 178, 170)) : System.Windows.Media.Brushes.Transparent;
        }

        private async void ShowNotification(string text)
        {
            try
            {
                NotifyText.Text = text;
                NotifyPanel.Visibility = Visibility.Visible;
                await Task.Delay(2600);
                NotifyPanel.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void ShutdownApplication()
        {
            if (_notifyIcon != null) _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void TxtBroadcast_GotFocus(object sender, RoutedEventArgs e) { if (TxtBroadcast.Text == "在此处键入需要广播的内容...") TxtBroadcast.Text = ""; }
        private void TxtBroadcast_LostFocus(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(TxtBroadcast.Text)) TxtBroadcast.Text = "在此处键入需要广播的内容..."; }
    }
}