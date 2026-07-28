using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TypelessSwitch.Core;

public enum DiagnosticStatus
{
    Passed,
    Warning,
    Failed
}

public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Message);

public sealed record DiagnosticReport(
    DateTimeOffset CreatedAt,
    string AppVersion,
    IReadOnlyList<DiagnosticCheck> Checks)
{
    public int Passed => Checks.Count(item => item.Status == DiagnosticStatus.Passed);
    public int Warnings => Checks.Count(item => item.Status == DiagnosticStatus.Warning);
    public int Failed => Checks.Count(item => item.Status == DiagnosticStatus.Failed);

    public string ToRedactedText()
    {
        var lines = new List<string>
        {
            "Typeless Switch 脱敏诊断报告",
            $"版本：v{AppVersion}",
            $"生成时间：{CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}",
            $"结果：通过 {Passed} / 警告 {Warnings} / 失败 {Failed}",
            ""
        };
        lines.AddRange(Checks.Select(item =>
            $"[{StatusLabel(item.Status)}] {Redact(item.Name)}：{Redact(item.Message)}"));
        lines.Add("");
        lines.Add("隐私说明：报告不包含邮箱、用户 ID、令牌、用户名或绝对路径。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string StatusLabel(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Passed => "通过",
        DiagnosticStatus.Warning => "警告",
        _ => "失败"
    };

    private static string Redact(string value)
    {
        var redacted = Regex.Replace(
            value,
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            "[邮箱已隐藏]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b(?:bearer\s+)?[a-z0-9_-]{20,}\.[a-z0-9_-]{20,}(?:\.[a-z0-9_-]{10,})?\b",
            "[令牌已隐藏]",
            RegexOptions.CultureInvariant);
        redacted = Regex.Replace(
            redacted,
            @"(?i)(?:[a-z]:\\|\\\\)[^\s，；。]+",
            "[绝对路径已隐藏]",
            RegexOptions.CultureInvariant);
        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
            redacted = redacted.Replace(userName, "[用户名已隐藏]", StringComparison.OrdinalIgnoreCase);
        return redacted;
    }
}

public sealed class EnvironmentDiagnosticsService
{
    private const string BackupDirectoryPrefix = "typeless-switch-backup-";
    private readonly TypelessPaths _paths;
    private readonly SessionStoreService _sessionStore;
    private readonly AccountRegistryService _accounts;
    private readonly AccountVaultService _vault;

    public EnvironmentDiagnosticsService(
        TypelessPaths paths,
        SessionStoreService sessionStore,
        AccountRegistryService accounts,
        AccountVaultService vault)
    {
        _paths = paths;
        _sessionStore = sessionStore;
        _accounts = accounts;
        _vault = vault;
    }

    public async Task<DiagnosticReport> RunAsync(
        string appVersion,
        string? webViewVersion,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DiagnosticCheck>();
        checks.Add(new DiagnosticCheck(
            "运行平台",
            OperatingSystem.IsWindows() ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            OperatingSystem.IsWindows()
                ? $"Windows {Environment.OSVersion.Version} / {RuntimeArchitecture()}"
                : "当前系统不是受支持的 Windows 环境"));

        checks.Add(new DiagnosticCheck(
            "Typeless 程序",
            _paths.FindTypelessExecutable() is not null ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            _paths.FindTypelessExecutable() is not null
                ? "已在标准用户路径或 TYPELESS_APP_PATH 中找到"
                : "未找到 Typeless.exe"));

        checks.Add(CheckWebView(webViewVersion));
        checks.Add(await CheckWritableStorageAsync(cancellationToken));
        checks.Add(await CheckCurrentSessionAsync(cancellationToken));
        checks.Add(await CheckAccountVaultAsync(cancellationToken));
        checks.Add(CheckRunningProcess());
        checks.Add(CheckTemporaryBackups());

        return new DiagnosticReport(DateTimeOffset.Now, appVersion, checks);
    }

    private static DiagnosticCheck CheckWebView(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? new DiagnosticCheck("WebView2 Runtime", DiagnosticStatus.Failed, "未检测到，邮箱登录窗口将无法打开")
            : new DiagnosticCheck("WebView2 Runtime", DiagnosticStatus.Passed, $"已安装（{version}）");

    private async Task<DiagnosticCheck> CheckWritableStorageAsync(CancellationToken cancellationToken)
    {
        var probe = Path.Combine(_paths.AppDataDirectory, $"diagnostic-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_paths.AppDataDirectory);
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
            return new DiagnosticCheck(
                "本地存储",
                DiagnosticStatus.Passed,
                "%LOCALAPPDATA%\\TypelessSwitch 可读写");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new DiagnosticCheck(
                "本地存储",
                DiagnosticStatus.Failed,
                "应用数据目录不可写");
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private async Task<DiagnosticCheck> CheckCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.UserDataFile))
            return new DiagnosticCheck("当前会话", DiagnosticStatus.Warning, "尚未发现 Typeless 登录状态");

        try
        {
            var session = await _sessionStore.ReadAsync(cancellationToken);
            var health = AccountHealth.Evaluate(session);
            return health switch
            {
                AccountHealthStatus.Healthy => new DiagnosticCheck("当前会话", DiagnosticStatus.Passed, "身份字段完整且会话可读取"),
                AccountHealthStatus.NeedsLogin => new DiagnosticCheck("当前会话", DiagnosticStatus.Warning, "长期登录状态已过期，需要重新登录"),
                _ => new DiagnosticCheck("当前会话", DiagnosticStatus.Failed, "会话缺少必要身份字段")
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck("当前会话", DiagnosticStatus.Failed, "会话文件存在但无法安全读取");
        }
    }

    private async Task<DiagnosticCheck> CheckAccountVaultAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new DiagnosticCheck("加密账号库", DiagnosticStatus.Failed, "DPAPI 账号库仅支持 Windows");

        try
        {
            var records = await _accounts.LoadAsync(cancellationToken);
            if (records.Count == 0)
                return new DiagnosticCheck("加密账号库", DiagnosticStatus.Warning, "尚未保存本地账号");

            var valid = 0;
            var needsLogin = 0;
            var invalid = 0;
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_vault.HasSession(record.UserId))
                {
                    needsLogin++;
                    continue;
                }

                try
                {
                    var session = await _vault.LoadAsync(record.UserId, cancellationToken);
                    if (session is null ||
                        !string.Equals(session.UserId, record.UserId, StringComparison.Ordinal) ||
                        !string.Equals(session.Email, record.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        invalid++;
                    }
                    else if (AccountHealth.Evaluate(session) == AccountHealthStatus.NeedsLogin)
                    {
                        needsLogin++;
                    }
                    else
                    {
                        valid++;
                    }
                }
                catch (InvalidDataException)
                {
                    invalid++;
                }
            }

            var status = invalid > 0
                ? DiagnosticStatus.Failed
                : needsLogin > 0 ? DiagnosticStatus.Warning : DiagnosticStatus.Passed;
            return new DiagnosticCheck(
                "加密账号库",
                status,
                $"共 {records.Count} 个：本地可读取 {valid}，需重新登录 {needsLogin}，异常 {invalid}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck("加密账号库", DiagnosticStatus.Failed, "无法读取账号摘要或 DPAPI 会话库");
        }
    }

    private static DiagnosticCheck CheckRunningProcess()
    {
        var processes = Process.GetProcessesByName("Typeless");
        try
        {
            return new DiagnosticCheck(
                "Typeless 进程",
                DiagnosticStatus.Passed,
                processes.Length == 0 ? "当前未运行（可正常执行切换）" : $"当前运行中（{processes.Length} 个进程）");
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static DiagnosticCheck CheckTemporaryBackups()
    {
        try
        {
            var count = Directory.EnumerateDirectories(Path.GetTempPath(), BackupDirectoryPrefix + "*").Count();
            return count == 0
                ? new DiagnosticCheck("临时备份", DiagnosticStatus.Passed, "没有遗留的切换备份")
                : new DiagnosticCheck("临时备份", DiagnosticStatus.Warning, $"发现 {count} 个历史切换备份，可在确认无恢复需要后清理");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck("临时备份", DiagnosticStatus.Warning, "无法枚举系统临时目录");
        }
    }

    private static string RuntimeArchitecture() =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
}
