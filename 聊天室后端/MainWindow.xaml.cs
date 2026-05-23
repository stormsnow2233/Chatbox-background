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
        private readonly string _backendUrl = "http://localhost:8080";
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

        // 🟢 托盘图标初始化
        private void InitSystemTray()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            // 使用系统自带的高清安全盾牌图标作为托盘标识（可换为自定义ico路径）
            _notifyIcon.Icon = System.Drawing.SystemIcons.Shield;
            _notifyIcon.Text = "局域网核心控制台守护进程";
            _notifyIcon.Visible = true;

            // 双击托盘图标唤醒主窗口
            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            // 右键快捷菜单
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("打开控制面板", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            contextMenu.Items.Add("彻底销毁退出", null, (s, e) => { ShutdownApplication(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        // 🔐 检查当前注册表内是否已存在开机自启
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

        // 🛠️ 核心需求：一键注册/注销 Windows 开机自启
        private void ToggleAutostart_Click(object sender, RoutedEventArgs e)
        {
            // 该方法已弃用，使用 Checked/Unchecked 事件处理
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
                    ShowNotification("🚀 开机自启链注册成功！");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    ShowNotification("🧹 成功从注册表自启项中抹除！");
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

        // 📡 定时轮询 Go 后端的实时运行状态
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

                    TxtServerStatus.Text = "🟢 后端服务正常";
                    ToggleMute.IsChecked = isMuted;
                    UpdateToggleBorderState(BorderToggleMute, isMuted);
                }
            }
            catch
            {
                TxtServerStatus.Text = "🔴 后端断开或未启动";
            }
        }

        // 🔇 联动 Go 后端：一键开关全场禁言
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
            // 立即更新视觉反馈
            UpdateToggleBorderState(BorderToggleMute, newState);

            try
            {
                HttpResponseMessage resp = await _httpClient.GetAsync($"{_backendUrl}/api/admin/toggle-mute?secret={_adminSecret}");
                if (!resp.IsSuccessStatusCode)
                {
                    // 回退
                    ToggleMute.Checked -= ToggleMute_Checked;
                    ToggleMute.Unchecked -= ToggleMute_Unchecked;
                    ToggleMute.IsChecked = !newState;
                    ToggleMute.Checked += ToggleMute_Checked;
                    ToggleMute.Unchecked += ToggleMute_Unchecked;
                    UpdateToggleBorderState(BorderToggleMute, ToggleMute.IsChecked == true);
                    ShowNotification("无法同步状态，请检查 Go 后端服务是否存活");
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
                ShowNotification("无法同步状态，请检查 Go 后端服务是否存活");
            }
        }

        // 📢 联动 Go 后端：发送核心公告群发
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
                    ShowNotification("📢 广播信号成功切入公屏数据流！");
                }
            }
            catch
            {
                ShowNotification("广播群发失败，与 Go 链路对接异常");
            }
        }

        // 更新边框外观以反映 Toggle 状态
        private void UpdateToggleBorderState(System.Windows.Controls.Border? border, bool isChecked)
        {
            if (border == null) return;
            border.BorderBrush = isChecked ? System.Windows.Media.Brushes.LightSeaGreen : System.Windows.Media.Brushes.LightGray;
            border.Background = isChecked ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 32, 178, 170)) : System.Windows.Media.Brushes.Transparent;
        }

        // 显示内置通知（替代 MessageBox）
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

        // 🌐 窗口交互基础支撑逻辑
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove(); // 支持无边框窗口全局拖拽
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e) => this.Hide(); // 隐藏主窗口，转入后台托盘运行

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Hide(); // 点击关闭同样转入托盘，防止误触导致服务关闭

        private void ShutdownApplication()
        {
            if (_notifyIcon != null) _notifyIcon.Dispose(); // 释放托盘图标生命周期
            Application.Current.Shutdown();
        }

        // ✍️ 文本框占位符交互
        private void TxtBroadcast_GotFocus(object sender, RoutedEventArgs e) { if (TxtBroadcast.Text == "在此处键入需要广播的内容...") TxtBroadcast.Text = ""; }
        private void TxtBroadcast_LostFocus(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(TxtBroadcast.Text)) TxtBroadcast.Text = "在此处键入需要广播的内容..."; }
    }
}