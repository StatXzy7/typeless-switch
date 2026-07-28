using System.Diagnostics;
using System.Net.Mail;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TypelessSwitch.Core;
using TypelessSwitch.Core.Models;

namespace TypelessSwitch.App;

public partial class MainWindow : Window
{
    private readonly TypelessPaths _paths = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SessionStoreService _sessionStore;
    private readonly LocalStateService _localState;
    private readonly AccountRegistryService _accounts;
    private readonly DictionaryService _dictionary;
    private readonly UpdateService _updates;
    private TypelessSession? _session;
    private CancellationTokenSource? _operationCancellation;
    private bool _busy;
    private bool _checkingForUpdates;
    private bool _automaticUpdateCheckStarted;

    public MainWindow()
    {
        InitializeComponent();
        _sessionStore = new SessionStoreService(_paths);
        _localState = new LocalStateService(_paths);
        _accounts = new AccountRegistryService(_paths);
        _dictionary = new DictionaryService(_httpClient);
        _updates = new UpdateService(_httpClient);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_paths.DefaultExportJsonFile))
            ImportPathBox.Text = _paths.DefaultExportJsonFile;

        await RefreshSessionAsync();
        if (!_automaticUpdateCheckStarted)
        {
            _automaticUpdateCheckStarted = true;
            _ = CheckForUpdatesAsync(interactive: false);
        }
    }
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshSessionAsync();
    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(interactive: true);

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingForUpdates) return;
        if (_busy)
        {
            if (interactive) ShowInfo("当前有操作正在进行，请完成后再检查更新。");
            return;
        }

        _checkingForUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 2);
            var update = await _updates.CheckLatestAsync(currentVersion);
            if (update is null)
            {
                if (interactive) ShowInfo($"当前已经是最新版本（v{currentVersion.ToString(3)}）。");
                return;
            }

            var choice = MessageBox.Show(
                this,
                $"发现新版本 v{update.Version.ToString(3)}。\n\n是否下载并安装？安装程序会在下载完成后打开。",
                "发现更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes) return;

            string? installerPath = null;
            var progress = new Progress<UpdateDownloadProgress>(item =>
            {
                var percent = item.TotalBytes <= 0 ? 5 : item.BytesDownloaded * 100d / item.TotalBytes;
                SetStatus($"正在下载更新… {FormatBytes(item.BytesDownloaded)} / {FormatBytes(item.TotalBytes)}", percent);
            });
            await RunOperationAsync("正在准备下载更新…", async token =>
            {
                installerPath = await _updates.DownloadInstallerAsync(
                    update, _paths.UpdatesDirectory, progress, token);
                SetStatus("更新下载完成，正在等待确认安装…", 100);
            });

            if (installerPath is null || !File.Exists(installerPath)) return;
            var installChoice = MessageBox.Show(
                this,
                "更新安装包已下载并通过 SHA-256 校验。是否现在打开安装程序？",
                "安装更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (installChoice != MessageBoxResult.Yes) return;

            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            if (interactive) SetStatus("更新下载已取消", 0);
        }
        catch (Exception exception)
        {
            if (interactive)
                MessageBox.Show(this, UpdateErrorMessage(exception), "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _checkingForUpdates = false;
            CheckUpdatesButton.IsEnabled = !_busy;
        }
    }

    private async Task RefreshSessionAsync()
    {
        try
        {
            _session = await _sessionStore.ReadAsync();
            AccountEmailText.Text = _session?.Email ?? "未检测到登录账号";
            AccountStatusText.Text = _session is null ? "请先登录 Typeless，或直接切换账号" : $"用户 ID：{_session.UserId}";
        }
        catch (Exception exception)
        {
            _session = null;
            AccountEmailText.Text = "无法读取本地会话";
            AccountStatusText.Text = FriendlyMessage(exception);
        }
    }

    private async void DefaultExportButton_Click(object sender, RoutedEventArgs e) =>
        await ExportToDirectoryAsync(_paths.DefaultExportDirectory);

    private async void CustomExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            ShowInfo("未读取到 Typeless 登录账号。请先登录或切换账号。");
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择词典导出文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        await ExportToDirectoryAsync(dialog.FolderName);
    }

    private async Task ExportToDirectoryAsync(string outputDirectory)
    {
        if (_session is null)
        {
            ShowInfo("未读取到 Typeless 登录账号。请先登录或切换账号。");
            return;
        }

        await RunOperationAsync("正在导出词典…", async token =>
        {
            var result = await _dictionary.ExportAsync(_session, outputDirectory, token);
            ImportPathBox.Text = result.JsonPath;
            SetStatus($"已导出 {result.Total} 个词条到 {outputDirectory}，JSON 已可直接导入", 100);
        });
    }

    private void UseDefaultImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_paths.DefaultExportJsonFile))
        {
            ShowInfo($"默认词典文件尚不存在。请先点击“导出到默认位置”。\n\n默认位置：{_paths.DefaultExportDirectory}");
            return;
        }

        ImportPathBox.Text = _paths.DefaultExportJsonFile;
        SetStatus("已选择默认词典文件", 0);
    }

    private void BrowseImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Typeless Switch 词典文件",
            Filter = "Typeless 词典 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) ImportPathBox.Text = dialog.FileName;
    }

    private void ImportModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConcurrencyBox is not null) ConcurrencyBox.IsEnabled = ImportModeBox.SelectedIndex == 1 && !_busy;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            ShowInfo("未读取到 Typeless 登录账号。请先登录或切换账号。");
            return;
        }
        if (!File.Exists(ImportPathBox.Text))
        {
            ShowInfo("请先选择有效的 JSON 词典文件。");
            return;
        }
        if (!int.TryParse(ConcurrencyBox.Text, out var concurrency) || concurrency is < 1 or > 32)
        {
            ShowInfo("并发数请输入 1 到 32 之间的整数。");
            return;
        }
        var mode = ImportModeBox.SelectedIndex == 0 ? DictionaryImportMode.Bulk : DictionaryImportMode.Full;
        var progress = new Progress<DictionaryProgress>(item =>
        {
            var percent = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
            SetStatus($"{item.Message}（成功 {item.Succeeded}，失败 {item.Failed}）", percent);
        });

        await RunOperationAsync("正在检查词典…", async token =>
        {
            var result = await _dictionary.ImportAsync(
                _session.AccessToken, ImportPathBox.Text, mode, concurrency, progress, token);
            SetStatus($"导入完成：成功 {result.Imported}，失败 {result.Failed}，跳过重复 {result.Skipped}", 100);
        });
    }

    private async void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        var email = SwitchEmailBox.Text.Trim();
        if (!IsEmail(email))
        {
            ShowInfo("请输入有效的邮箱地址。");
            return;
        }

        await RunOperationAsync("正在安全备份当前账号…", async token =>
        {
            string? backup = null;
            var switched = false;
            var typelessWasRunning = false;
            try
            {
                typelessWasRunning = await _localState.StopTypelessAsync(token);
                backup = await _localState.BackupAsync(token);
                await _localState.ClearForLoginAsync(token);
                SetStatus("请在登录窗口中完成邮箱验证码验证", 30);

                var login = new LoginWindow(email, _paths) { Owner = this };
                if (login.ShowDialog() != true || login.Session is null)
                    throw new OperationCanceledException("已取消账号切换。");

                SetStatus("正在写入新账号会话…", 75);
                await _sessionStore.WriteAsync(login.Session, token);
                switched = true;
                try
                {
                    await _accounts.SaveAsync(new AccountRecord(login.Session.Email, login.Session.UserId, DateTimeOffset.UtcNow), token);
                }
                catch { }
                _localState.StartTypeless();
                _session = login.Session;
                AccountEmailText.Text = login.Session.Email;
                AccountStatusText.Text = $"用户 ID：{login.Session.UserId}";
                SwitchEmailBox.Clear();
                SetStatus($"已切换到 {login.Session.Email}", 100);
            }
            finally
            {
                if (!switched)
                {
                    SetStatus("正在恢复原账号…", 80);
                    if (backup is not null)
                        await _localState.RestoreAsync(backup, CancellationToken.None);
                    if (typelessWasRunning)
                        _localState.StartTypeless();
                    await RefreshSessionAsync();
                }
            }
        });
    }

    private async Task RunOperationAsync(string initialStatus, Func<CancellationToken, Task> operation)
    {
        if (_busy) return;
        _operationCancellation = new CancellationTokenSource();
        SetBusy(true);
        SetStatus(initialStatus, 5);
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消", 0);
        }
        catch (Exception exception)
        {
            SetStatus($"操作失败：{FriendlyMessage(exception)}", 0);
            MessageBox.Show(this, FriendlyMessage(exception), "Typeless Switch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        CheckUpdatesButton.IsEnabled = !busy && !_checkingForUpdates;
        SwitchButton.IsEnabled = !busy;
        DefaultExportButton.IsEnabled = !busy;
        CustomExportButton.IsEnabled = !busy;
        UseDefaultImportButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        ImportModeBox.IsEnabled = !busy;
        ConcurrencyBox.IsEnabled = !busy && ImportModeBox.SelectedIndex == 1;
        CancelButton.IsEnabled = busy;
    }

    private void SetStatus(string message, double percent)
    {
        StatusText.Text = message;
        OperationProgress.Value = Math.Clamp(percent, 0, 100);
    }

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, "Typeless Switch", MessageBoxButton.OK, MessageBoxImage.Information);

    private static bool IsEmail(string value)
    {
        try { return new MailAddress(value).Address == value; }
        catch { return false; }
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } =>
            "Typeless 登录状态已过期或无权执行此操作，请重新登录后重试。",
        HttpRequestException => "无法连接 Typeless 服务，请检查网络和登录状态。",
        UnauthorizedAccessException => "没有权限访问所选文件或 Typeless 本地数据。",
        JsonException => "文件格式不正确，或 Typeless 返回了无法识别的数据。",
        _ => exception.Message
    };

    private static string UpdateErrorMessage(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
            "GitHub 更新服务暂时限流，请稍后重试。",
        HttpRequestException { StatusCode: var status } when status is not null =>
            $"GitHub 更新服务返回 HTTP {(int)status.Value}，请稍后重试。",
        HttpRequestException => "无法连接 GitHub 更新服务，请检查网络后重试。",
        InvalidDataException => exception.Message,
        UnauthorizedAccessException => "没有权限保存更新安装包，请检查本地磁盘权限。",
        _ => $"更新失败：{exception.Message}"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "未知大小";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes} B";
    }
}
