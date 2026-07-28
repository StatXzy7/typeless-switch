using System.Diagnostics;
using System.Net.Mail;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
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
    private readonly AccountVaultService _vault;
    private readonly DictionaryService _dictionary;
    private readonly UpdateService _updates;
    private readonly SessionVerificationService _sessionVerifier;
    private readonly EnvironmentDiagnosticsService _diagnostics;
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
        _vault = new AccountVaultService(_paths);
        _dictionary = new DictionaryService(_httpClient);
        _updates = new UpdateService(_httpClient);
        _sessionVerifier = new SessionVerificationService(_sessionStore);
        _diagnostics = new EnvironmentDiagnosticsService(_paths, _sessionStore, _accounts, _vault);
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

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticReport? report = null;
        await RunOperationAsync("正在执行本地环境自检…", async token =>
        {
            string? webViewVersion = null;
            try { webViewVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or InvalidOperationException) { }

            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.3.2";
            report = await _diagnostics.RunAsync(version, webViewVersion, token);
            SetStatus($"自检完成：通过 {report.Passed}，警告 {report.Warnings}，失败 {report.Failed}", 100);
        });

        if (report is null) return;
        var text = report.ToRedactedText();
        var choice = MessageBox.Show(
            this,
            text + "\n\n是否将这份脱敏报告复制到剪贴板？",
            "环境自检",
            MessageBoxButton.YesNo,
            report.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        if (choice == MessageBoxResult.Yes)
        {
            Clipboard.SetText(text);
            SetStatus("脱敏诊断报告已复制；不含邮箱、用户 ID、令牌或绝对路径", 100);
        }
    }

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
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 2, 1);
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
            if (_session is not null)
                await SaveKnownSessionAsync(_session, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _session = null;
            AccountEmailText.Text = "无法读取本地会话";
            AccountStatusText.Text = FriendlyMessage(exception);
        }

        await RefreshSavedAccountsAsync();
    }

    private async Task SaveKnownSessionAsync(
        TypelessSession session,
        CancellationToken cancellationToken,
        AccountHealthStatus? healthStatus = null,
        DateTimeOffset? verifiedAt = null)
    {
        await _vault.SaveAsync(session, cancellationToken);
        var status = healthStatus ?? AccountHealth.Evaluate(session);
        await _accounts.SaveAsync(
            new AccountRecord(
                session.Email,
                session.UserId,
                DateTimeOffset.UtcNow,
                status,
                verifiedAt ?? DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task RefreshSavedAccountsAsync()
    {
        var selectedUserId = (SavedAccountsList.SelectedItem as SavedAccountItem)?.UserId;
        try
        {
            var records = await _accounts.LoadAsync();
            var items = new List<SavedAccountItem>(records.Count);
            foreach (var record in records)
            {
                var hasSession = _vault.HasSession(record.UserId);
                var health = hasSession ? record.HealthStatus : AccountHealthStatus.NeedsLogin;
                if (hasSession)
                {
                    try
                    {
                        var saved = await _vault.LoadAsync(record.UserId);
                        if (saved is null)
                            health = AccountHealthStatus.NeedsLogin;
                        else if (!string.Equals(saved.UserId, record.UserId, StringComparison.Ordinal) ||
                                 !string.Equals(saved.Email, record.Email, StringComparison.OrdinalIgnoreCase))
                            health = AccountHealthStatus.InvalidSession;
                        else if (AccountHealth.IsJwtExpired(saved.RefreshToken))
                            health = AccountHealthStatus.NeedsLogin;
                    }
                    catch (InvalidDataException)
                    {
                        health = AccountHealthStatus.InvalidSession;
                    }
                }

                items.Add(new SavedAccountItem(
                    record.Email,
                    record.UserId,
                    hasSession,
                    string.Equals(record.UserId, _session?.UserId, StringComparison.Ordinal),
                    record.LastUsedAt.ToLocalTime(),
                    health,
                    record.LastVerifiedAt?.ToLocalTime()));
            }
            SavedAccountsList.ItemsSource = items;
            SavedAccountsList.SelectedItem = items.FirstOrDefault(item =>
                string.Equals(item.UserId, selectedUserId, StringComparison.Ordinal)) ?? items.FirstOrDefault();
        }
        catch (Exception exception)
        {
            SavedAccountsList.ItemsSource = Array.Empty<SavedAccountItem>();
            SetStatus($"无法读取本地账号列表：{FriendlyMessage(exception)}", 0);
        }

        UpdateSavedAccountButtons();
    }

    private void SavedAccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSavedAccountButtons();

    private void UpdateSavedAccountButtons()
    {
        if (SwitchSavedAccountButton is null || RemoveSavedAccountButton is null) return;
        var selected = SavedAccountsList.SelectedItem as SavedAccountItem;
        SwitchSavedAccountButton.IsEnabled = !_busy && selected is not null && !selected.IsCurrent;
        RemoveSavedAccountButton.IsEnabled = !_busy && selected is not null && !selected.IsCurrent;
    }

    private async void SwitchSavedAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedAccountsList.SelectedItem is not SavedAccountItem selected || selected.IsCurrent) return;
        if (!selected.HasSession)
        {
            SwitchEmailBox.Text = selected.Email;
            ShowInfo("这个账号只有本地记录，没有可恢复的加密登录状态。请使用邮箱验证码重新登录。\n\n邮箱已自动填入下方输入框。");
            return;
        }

        TypelessSession? savedSession;
        try
        {
            savedSession = await _vault.LoadAsync(selected.UserId);
        }
        catch (Exception exception)
        {
            try
            {
                await _accounts.UpdateHealthAsync(
                    selected.UserId,
                    AccountHealthStatus.InvalidSession,
                    DateTimeOffset.UtcNow);
                await RefreshSavedAccountsAsync();
            }
            catch { }
            ShowInfo($"无法读取保存的登录状态：{FriendlyMessage(exception)}\n\n请重新登录该账号。");
            SwitchEmailBox.Text = selected.Email;
            return;
        }

        if (savedSession is null)
        {
            await _accounts.UpdateHealthAsync(
                selected.UserId,
                AccountHealthStatus.NeedsLogin,
                DateTimeOffset.UtcNow);
            await RefreshSavedAccountsAsync();
            SwitchEmailBox.Text = selected.Email;
            ShowInfo("保存的登录状态不存在，请重新登录该账号。");
            return;
        }
        if (AccountHealth.IsJwtExpired(savedSession.RefreshToken))
        {
            await _accounts.UpdateHealthAsync(
                savedSession.UserId,
                AccountHealthStatus.NeedsLogin,
                DateTimeOffset.UtcNow);
            await RefreshSavedAccountsAsync();
            SwitchEmailBox.Text = selected.Email;
            ShowInfo("这个账号的长期登录状态已经过期，请使用邮箱验证码重新登录。\n\n邮箱已自动填入下方输入框。");
            return;
        }

        await SwitchToSavedSessionAsync(savedSession);
    }

    private async Task SwitchToSavedSessionAsync(TypelessSession savedSession)
    {
        await RunOperationAsync("正在保存当前账号并准备切换…", async token =>
        {
            string? backup = null;
            var switched = false;
            var typelessWasRunning = false;
            var targetStateApplied = false;
            try
            {
                if (_session is not null)
                    await SaveKnownSessionAsync(_session, token);
                typelessWasRunning = await _localState.StopTypelessAsync(CancellationToken.None);
                backup = await _localState.BackupAsync(token);
                await _localState.ClearForStoredSessionSwitchAsync(token);
                targetStateApplied = true;
                var verifiedSession = await ApplyAndVerifySessionAsync(savedSession, token);
                _session = verifiedSession;
                await SaveKnownSessionAsync(
                    verifiedSession,
                    token,
                    AccountHealthStatus.Healthy,
                    DateTimeOffset.UtcNow);
                switched = true;
                AccountEmailText.Text = verifiedSession.Email;
                AccountStatusText.Text = $"用户 ID：{verifiedSession.UserId} · 已严格验证";
                await RefreshSavedAccountsAsync();
                SetStatus($"已切换到 {verifiedSession.Email}，并确认 Typeless 当前账号一致", 100);
            }
            catch (Exception) when (!token.IsCancellationRequested && targetStateApplied)
            {
                try
                {
                    await _accounts.UpdateHealthAsync(
                        savedSession.UserId,
                        AccountHealthStatus.VerificationFailed,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None);
                }
                catch { }
                throw;
            }
            finally
            {
                var backupCanBeDeleted = switched;
                try
                {
                    if (!switched)
                    {
                        SetStatus("正在恢复原账号…", 80);
                        if (targetStateApplied)
                            await _localState.StopTypelessAsync(CancellationToken.None);
                        if (backup is not null)
                            await _localState.RestoreAsync(backup, CancellationToken.None);
                        if (typelessWasRunning)
                            _localState.StartTypeless();
                        await RefreshSessionAsync();
                        backupCanBeDeleted = true;
                    }
                }
                finally
                {
                    if (backup is not null && backupCanBeDeleted && !_localState.TryDeleteBackup(backup))
                        SetStatus("账号操作已完成，但临时备份未能自动清理，请稍后重试。", 100);
                }
            }
        });
    }

    private async Task<TypelessSession> ApplyAndVerifySessionAsync(
        TypelessSession target,
        CancellationToken cancellationToken)
    {
        SetStatus("正在写入目标账号会话…", 58);
        await _sessionStore.WriteAsync(target, cancellationToken);

        SetStatus("正在校验写入结果…", 68);
        var persisted = await _sessionStore.ReadAsync(cancellationToken);
        if (!AccountHealth.IsExpectedIdentity(persisted, target))
            throw new InvalidDataException("目标会话写入后身份不一致，已停止切换。");

        SetStatus("正在启动 Typeless…", 76);
        if (!_localState.StartTypeless())
            throw new FileNotFoundException("未找到 Typeless.exe，无法验证切换结果。请安装 Typeless 或设置 TYPELESS_APP_PATH。");
        if (!await _localState.WaitForTypelessRunningAsync(cancellationToken: cancellationToken))
            throw new InvalidOperationException("Typeless 未能在预期时间内启动，切换将自动回滚。");

        SetStatus("正在重新读取 Typeless 当前账号并严格验证…", 88);
        var verified = await _sessionVerifier.VerifyAsync(target, cancellationToken: cancellationToken);
        if (AccountHealth.Evaluate(verified) != AccountHealthStatus.Healthy)
            throw new InvalidDataException("目标账号的长期登录状态不可用，切换将自动回滚。");
        return verified;
    }

    private async void RemoveSavedAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedAccountsList.SelectedItem is not SavedAccountItem selected || selected.IsCurrent) return;
        var answer = MessageBox.Show(
            this,
            $"确定移除 {selected.Email} 的本地记录和加密登录状态吗？\n\n这不会删除 Typeless 远程账号。",
            "移除本地账号",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await RunOperationAsync("正在移除本地账号记录…", async token =>
        {
            await _vault.DeleteAsync(selected.UserId, token);
            await _accounts.DeleteAsync(selected.UserId, token);
            await RefreshSavedAccountsAsync();
            SetStatus("本地账号记录已移除", 100);
        });
    }

    private async void DefaultExportButton_Click(object sender, RoutedEventArgs e) =>
        await ExportToDirectoryAsync(_paths.DefaultExportDirectory);

    private async void CustomExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择词典导出文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        await ExportToDirectoryAsync(dialog.FolderName);
    }

    private async Task ExportToDirectoryAsync(string outputDirectory)
    {
        await RunOperationAsync("正在读取最新登录状态…", async token =>
        {
            var result = await RunDictionaryOperationAsync(
                "导出",
                (session, operationToken) => _dictionary.ExportAsync(session, outputDirectory, operationToken),
                token);
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

        await RunOperationAsync("正在读取最新登录状态…", async token =>
        {
            var result = await RunDictionaryOperationAsync(
                "导入",
                (session, operationToken) => _dictionary.ImportAsync(
                    session.RefreshToken,
                    ImportPathBox.Text,
                    mode,
                    concurrency,
                    progress,
                    operationToken),
                token);
            SetStatus($"导入完成：成功 {result.Imported}，失败 {result.Failed}，跳过重复 {result.Skipped}", 100);
        });
    }

    private async Task<T> RunDictionaryOperationAsync<T>(
        string actionName,
        Func<TypelessSession, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var session = await ReadLatestDictionarySessionAsync(cancellationToken);
        T result;
        try
        {
            SetStatus($"正在{actionName}词典…", 12);
            result = await operation(session, cancellationToken);
        }
        catch (HttpRequestException exception) when (IsAuthenticationFailure(exception))
        {
            SetStatus($"{actionName}凭据被拒绝，正在重新同步 Typeless 会话并安全重试…", 18);
            await Task.Delay(400, cancellationToken);
            session = await ReadLatestDictionarySessionAsync(cancellationToken);
            result = await operation(session, cancellationToken);
        }

        _session = session;
        await SaveKnownSessionAsync(
            session,
            cancellationToken,
            AccountHealthStatus.Healthy,
            DateTimeOffset.UtcNow);
        AccountEmailText.Text = session.Email;
        AccountStatusText.Text = $"用户 ID：{session.UserId} · 词典凭据已验证";
        await RefreshSavedAccountsAsync();
        return result;
    }

    private async Task<TypelessSession> ReadLatestDictionarySessionAsync(CancellationToken cancellationToken)
    {
        var latest = await _sessionStore.ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException("未读取到 Typeless 登录状态，请先登录后重试。");
        if (string.IsNullOrWhiteSpace(latest.RefreshToken) || AccountHealth.IsJwtExpired(latest.RefreshToken))
        {
            try
            {
                await _accounts.UpdateHealthAsync(
                    latest.UserId,
                    AccountHealthStatus.NeedsLogin,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);
            }
            catch { }
            throw new InvalidOperationException("Typeless 长期登录凭据已失效，请重新登录后重试。");
        }
        return latest;
    }

    private static bool IsAuthenticationFailure(HttpRequestException exception) =>
        exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

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
            var targetStateApplied = false;
            TypelessSession? targetSession = null;
            try
            {
                if (_session is not null)
                    await SaveKnownSessionAsync(_session, token);
                typelessWasRunning = await _localState.StopTypelessAsync(CancellationToken.None);
                backup = await _localState.BackupAsync(token);
                await _localState.ClearForLoginAsync(token);
                SetStatus("请在登录窗口中完成邮箱验证码验证", 30);

                var login = new LoginWindow(email, _paths) { Owner = this };
                if (login.ShowDialog() != true || login.Session is null)
                    throw new OperationCanceledException("已取消账号切换。");

                targetSession = login.Session;
                await _vault.SaveAsync(targetSession, token);
                await _accounts.SaveAsync(
                    new AccountRecord(
                        targetSession.Email,
                        targetSession.UserId,
                        DateTimeOffset.UtcNow,
                        AccountHealthStatus.Unknown,
                        null),
                    token);
                targetStateApplied = true;
                var verifiedSession = await ApplyAndVerifySessionAsync(targetSession, token);
                _session = verifiedSession;
                await SaveKnownSessionAsync(
                    verifiedSession,
                    token,
                    AccountHealthStatus.Healthy,
                    DateTimeOffset.UtcNow);
                switched = true;
                AccountEmailText.Text = verifiedSession.Email;
                AccountStatusText.Text = $"用户 ID：{verifiedSession.UserId} · 已严格验证";
                SwitchEmailBox.Clear();
                await RefreshSavedAccountsAsync();
                SetStatus($"已切换到 {verifiedSession.Email}，并确认 Typeless 当前账号一致", 100);
            }
            catch (Exception) when (!token.IsCancellationRequested && targetStateApplied && targetSession is not null)
            {
                try
                {
                    await _accounts.UpdateHealthAsync(
                        targetSession.UserId,
                        AccountHealthStatus.VerificationFailed,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None);
                }
                catch { }
                throw;
            }
            finally
            {
                var backupCanBeDeleted = switched;
                try
                {
                    if (!switched)
                    {
                        SetStatus("正在恢复原账号…", 80);
                        if (targetStateApplied)
                            await _localState.StopTypelessAsync(CancellationToken.None);
                        if (backup is not null)
                            await _localState.RestoreAsync(backup, CancellationToken.None);
                        if (typelessWasRunning)
                            _localState.StartTypeless();
                        await RefreshSessionAsync();
                        backupCanBeDeleted = true;
                    }
                }
                finally
                {
                    if (backup is not null && backupCanBeDeleted && !_localState.TryDeleteBackup(backup))
                        SetStatus("账号操作已完成，但临时备份未能自动清理，请稍后重试。", 100);
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "正在取消操作…";
            _operationCancellation?.Cancel();
            return;
        }

        StatusPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;
        OperationProgress.Value = 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        DiagnosticsButton.IsEnabled = !busy;
        CheckUpdatesButton.IsEnabled = !busy && !_checkingForUpdates;
        SwitchButton.IsEnabled = !busy;
        DefaultExportButton.IsEnabled = !busy;
        CustomExportButton.IsEnabled = !busy;
        UseDefaultImportButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        ImportModeBox.IsEnabled = !busy;
        ConcurrencyBox.IsEnabled = !busy && ImportModeBox.SelectedIndex == 1;
        StatusPanel.Visibility = Visibility.Visible;
        CancelButton.Content = busy ? "取消" : "×";
        CancelButton.FontSize = busy ? 14 : 20;
        CancelButton.ToolTip = busy ? "取消当前操作" : "关闭状态提示";
        CancelButton.IsEnabled = true;
        OperationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateSavedAccountButtons();
    }

    private void SetStatus(string message, double percent)
    {
        StatusPanel.Visibility = Visibility.Visible;
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
            "已重新同步 Typeless 会话，但长期登录凭据仍被拒绝或账号无权执行此操作。请在 Typeless 中重新登录后重试。",
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

    private sealed record SavedAccountItem(
        string Email,
        string UserId,
        bool HasSession,
        bool IsCurrent,
        DateTimeOffset LastUsedAt,
        AccountHealthStatus HealthStatus,
        DateTimeOffset? LastVerifiedAt)
    {
        public string Details =>
            $"{HealthLabel(HealthStatus)} · " +
            (LastVerifiedAt is null ? "尚未验证" : $"验证于 {LastVerifiedAt:yyyy-MM-dd HH:mm}") +
            $" · 上次使用 {LastUsedAt:yyyy-MM-dd HH:mm}";
        public string CurrentLabel => IsCurrent ? "当前账号" : string.Empty;

        private static string HealthLabel(AccountHealthStatus status) => status switch
        {
            AccountHealthStatus.Healthy => "状态可用",
            AccountHealthStatus.NeedsLogin => "需要重新登录",
            AccountHealthStatus.InvalidSession => "会话异常",
            AccountHealthStatus.VerificationFailed => "上次验证失败",
            _ => "状态待验证"
        };
    }
}
