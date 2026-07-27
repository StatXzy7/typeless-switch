using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.App;

public partial class LoginWindow : Window
{
    private const string LoginUrl = "https://www.typeless.com/refer?code=JTIF7BK";
    private const string TokenKey = "MAXAI_CLIENT__FEATURES__AUTH__TOKEN_INFO";
    private readonly string _email;
    private readonly TypelessPaths _paths;
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _checking;
    private bool _completed;

    public LoginWindow(string email, TypelessPaths paths)
    {
        InitializeComponent();
        _email = email;
        _paths = paths;
        _pollTimer.Tick += PollTimer_Tick;
    }

    public TypelessSession? Session { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_paths.WebViewDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _paths.WebViewDirectory);
            await Browser.EnsureCoreWebView2Async(environment);
            await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            Browser.CoreWebView2.Navigate(LoginUrl);
            InstructionText.Text = $"目标账号：{_email}。邮箱会自动填写，请输入收到的六位验证码。";
            _pollTimer.Start();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"无法启动登录窗口：{exception.Message}\n\n请确认系统已安装 Microsoft Edge WebView2 Runtime。",
                "Typeless Switch", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) await PrepareLoginPageAsync();
    }

    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (_checking || Browser.CoreWebView2 is null) return;
        _checking = true;
        try
        {
            await PrepareLoginPageAsync();
            var result = await Browser.CoreWebView2.ExecuteScriptAsync(
                $"localStorage.getItem({JsonSerializer.Serialize(TokenKey)})");
            var raw = JsonSerializer.Deserialize<string?>(result);
            if (string.IsNullOrWhiteSpace(raw)) return;
            var tokens = JsonSerializer.Deserialize<LoginTokens>(raw);
            if (string.IsNullOrWhiteSpace(tokens?.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken)
                || string.IsNullOrWhiteSpace(tokens.UserId)) return;

            Session = new TypelessSession
            {
                Email = string.IsNullOrWhiteSpace(tokens.Email) ? _email : tokens.Email,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                UserId = tokens.UserId,
                LoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _completed = true;
            _pollTimer.Stop();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException)
        {
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task PrepareLoginPageAsync()
    {
        if (Browser.CoreWebView2 is null) return;
        await Browser.CoreWebView2.ExecuteScriptAsync($"window.__typelessSwitchEmail = {JsonSerializer.Serialize(_email)};");
        await Browser.CoreWebView2.ExecuteScriptAsync(
            """
            (() => {
              const text = el => (el.textContent || '').replace(/\s+/g, ' ').trim();
              const visible = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
              const elements = [...document.querySelectorAll('button,a')].filter(visible);
              const claim = elements.find(el => /Claim your \$5|领取.*\$5/.test(text(el)));
              if (claim && !document.querySelector('input')) { claim.click(); return 'claim'; }
              const emailChoice = elements.find(el => /Continue with email|使用电子?邮件继续|使用邮箱继续/i.test(text(el)));
              if (emailChoice && !document.querySelector('input[type=email],input[placeholder*=mail i],input[placeholder*=邮箱]')) {
                emailChoice.click(); return 'email-choice';
              }
              const input = document.querySelector('input[type=email],input[placeholder*=mail i],input[placeholder*=邮箱],input[placeholder*=邮件]');
              if (input && !window.__typelessSwitchFilled) {
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                setter.call(input, window.__typelessSwitchEmail);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                input.focus();
                window.__typelessSwitchFilled = true;
                return 'filled';
              }
              return 'waiting';
            })()
            """);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
        if (!_completed && DialogResult is null) DialogResult = false;
    }

    private sealed record LoginTokens
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("userId")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;
    }
}
