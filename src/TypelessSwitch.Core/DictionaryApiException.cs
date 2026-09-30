using System.Net;
using System.Text.Json;

namespace TypelessSwitch.Core;

public sealed class DictionaryApiException : HttpRequestException
{
    private DictionaryApiException(string message, HttpStatusCode status, int? apiCode)
        : base(message, null, status) => ApiCode = apiCode;

    public int? ApiCode { get; }
    public bool IsUnsupportedClient => ApiCode == 20006;
    public bool CanRetryAfterSessionSync => !IsUnsupportedClient && StatusCode == HttpStatusCode.Unauthorized;

    public static int? ReadApiCode(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("code", out var code)) return null;
            if (code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number)) return number;
            if (code.ValueKind == JsonValueKind.String && int.TryParse(code.GetString(), out number)) return number;
        }
        catch (JsonException) { }
        return null;
    }

    public static DictionaryApiException FromResponse(HttpStatusCode status, string content, string action = "词典操作")
    {
        var code = ReadApiCode(content);
        var suffix = code is null ? $"HTTP {(int)status}" : $"HTTP {(int)status}，错误码 {code}";
        // Keep response bodies out of UI messages: they can contain private account details.
        var message = code == 20006
            ? "Typeless 服务不支持当前客户端（错误码 20006）。重新登录或更换导出目录无法解决这个错误。" +
              "当前无法通过本工具读取云端词典；已有 JSON 备份可离线转为官方 CSV，再在 Typeless 中导入。"
            : status switch
            {
                HttpStatusCode.Unauthorized => $"Typeless 登录验证失败（{suffix}）。请刷新会话；仍失败时在 Typeless 中重新登录。",
                HttpStatusCode.Forbidden => $"Typeless 服务拒绝此操作（{suffix}）。这不一定是登录过期，请先在官方 Typeless 中检查词典是否可用。",
                _ => $"{action}失败（{suffix}）。"
            };
        return new DictionaryApiException(message, status, code);
    }
}
