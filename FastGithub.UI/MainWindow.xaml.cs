using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FastGithub.UI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly System.Windows.Forms.NotifyIcon notifyIcon;
        private const string FASTGITHUB_UI = "FastGithub.UI";
        private const string RELEASES_URI = "https://github.com/creazyboyone/FastGithub";

        /// <summary>
        /// 是否抑制开关事件（用于代码设置开关状态时）
        /// </summary>
        private bool suppressSpeedModeEvent;

        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += async (sender, args) => await this.SyncSpeedModeAsync();

            var upgrade = new System.Windows.Forms.MenuItem("检测更新(&U)");
            upgrade.Click += (s, e) => Process.Start(RELEASES_URI);

            var exit = new System.Windows.Forms.MenuItem("关闭应用(&C)");
            exit.Click += (s, e) => this.Close();

            var version = this.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            this.Title = $"{FASTGITHUB_UI} v{version}";
            this.notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = FASTGITHUB_UI,
                ContextMenu = new System.Windows.Forms.ContextMenu(new[] { upgrade, exit }),
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            };

            this.notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Show();
                    this.Activate();
                    this.WindowState = WindowState.Normal;
                }
            };
        }


        /// <summary>
        /// 开关切到下载优先
        /// </summary>
        private async void SpeedModeSwitch_Checked(object sender, RoutedEventArgs e)
        {
            if (this.suppressSpeedModeEvent == true)
            {
                return;
            }

            var message = "切换到下载优先后，会定期主动下载少量数据来实测各节点的下载速率，会占用你的带宽。"
                + Environment.NewLine + Environment.NewLine
                + "好处：大文件、Release 下载会优先走实测最快的节点。"
                + Environment.NewLine
                + "延迟优先只按握手延迟排序，不产生任何额外流量。"
                + Environment.NewLine + Environment.NewLine
                + "是否切换到下载优先？";

            var result = MessageBox.Show(this, message, "确认切换", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                this.SetSwitchChecked(false);
                return;
            }

            await this.SetSpeedModeAsync("Throughput");
        }

        /// <summary>
        /// 开关切回延迟优先
        /// </summary>
        private async void SpeedModeSwitch_Unchecked(object sender, RoutedEventArgs e)
        {
            if (this.suppressSpeedModeEvent == true)
            {
                return;
            }

            await this.SetSpeedModeAsync("Latency");
        }

        /// <summary>
        /// 启动时同步选路模式（最多重试10秒，等主程序就绪）
        /// </summary>
        private async Task SyncSpeedModeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var mode = await this.GetSpeedModeAsync();
                if (mode != null)
                {
                    this.ApplySpeedMode(mode);
                    return;
                }
                await Task.Delay(1000);
            }

            this.speedModeHint.Text = "未能读取选路模式（主程序未就绪）";
        }

        /// <summary>
        /// 读取当前选路模式
        /// </summary>
        private async Task<string?> GetSpeedModeAsync()
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5d) };
                var json = await httpClient.GetStringAsync("http://localhost/speedMode");
                return JsonConvert.DeserializeObject<SpeedModeResponse>(json)?.Mode;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 设置选路模式
        /// </summary>
        private async Task SetSpeedModeAsync(string mode)
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5d) };
                var json = await httpClient.GetStringAsync("http://localhost/speedMode/" + mode);
                var response = JsonConvert.DeserializeObject<SpeedModeResponse>(json);
                if (response == null)
                {
                    throw new InvalidOperationException("主程序返回空响应");
                }

                this.ApplySpeedMode(response.Mode);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "切换选路模式失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                var current = await this.GetSpeedModeAsync() ?? "Throughput";
                this.ApplySpeedMode(current);
            }
        }

        /// <summary>
        /// 把选路模式应用到界面
        /// </summary>
        private void ApplySpeedMode(string mode)
        {
            var accent = Color.FromRgb(0x37, 0xae, 0xfe);
            var gray = Color.FromRgb(0x99, 0x99, 0x99);
            var isThroughput = string.Equals(mode, "Throughput", StringComparison.OrdinalIgnoreCase);

            this.SetSwitchChecked(isThroughput);
            if (isThroughput == true)
            {
                this.speedModeHint.Text = "下载优先：会定期占用少量带宽实测最快节点";
                this.throughputModeText.Foreground = new SolidColorBrush(accent);
                this.throughputModeText.FontWeight = FontWeights.Bold;
                this.latencyModeText.Foreground = new SolidColorBrush(gray);
                this.latencyModeText.FontWeight = FontWeights.Normal;
            }
            else
            {
                this.speedModeHint.Text = "延迟优先：只按握手延迟排序，不产生额外流量";
                this.latencyModeText.Foreground = new SolidColorBrush(accent);
                this.latencyModeText.FontWeight = FontWeights.Bold;
                this.throughputModeText.Foreground = new SolidColorBrush(gray);
                this.throughputModeText.FontWeight = FontWeights.Normal;
            }
        }

        /// <summary>
        /// 静默设置开关状态
        /// </summary>
        private void SetSwitchChecked(bool isChecked)
        {
            this.suppressSpeedModeEvent = true;
            try
            {
                this.speedModeSwitch.IsChecked = isChecked;
            }
            finally
            {
                this.suppressSpeedModeEvent = false;
            }
        }

        /// <summary>
        /// 选路模式响应
        /// </summary>
        private class SpeedModeResponse
        {
            public string Mode { get; set; } = string.Empty;
        }

        /// <summary>
        /// 拦截最小化事件
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            hwndSource.AddHook(WndProc);

            IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                const int WM_SYSCOMMAND = 0x112;
                const int SC_MINIMIZE = 0xf020;
                const int SC_CLOSE = 0xf060;

                if (msg == WM_SYSCOMMAND)
                {
                    if (wParam.ToInt32() == SC_MINIMIZE || wParam.ToInt32() == SC_CLOSE)
                    {
                        this.Hide();
                        handled = true;
                    }
                }
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 关闭时
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosed(EventArgs e)
        {
            this.notifyIcon.Icon = null;
            this.notifyIcon.Dispose();
            base.OnClosed(e);
        }
    }
}
