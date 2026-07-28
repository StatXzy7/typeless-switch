using TypelessSwitch.Core;

namespace TypelessSwitch.Tests;

public sealed class DiagnosticReportTests
{
    [Fact]
    public void ToRedactedText_RemovesEmailAbsolutePathUserNameAndToken()
    {
        const string token = "abcdefghijklmnopqrstuv.abcdefghijklmnopqrstuv.abcdefghijkl";
        var report = new DiagnosticReport(
            DateTimeOffset.Parse("2026-07-28T12:00:00+08:00"),
            "0.3.0",
            [new DiagnosticCheck(
                "测试",
                DiagnosticStatus.Warning,
                $"person@example.com C:\\Users\\{Environment.UserName}\\secret {token}")]);

        var text = report.ToRedactedText();

        Assert.DoesNotContain("person@example.com", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.Contains("邮箱已隐藏", text);
        Assert.Contains("绝对路径已隐藏", text);
        Assert.Contains("令牌已隐藏", text);
    }
}
