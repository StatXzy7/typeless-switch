using System.Net;
using TypelessSwitch.Core;

namespace TypelessSwitch.Tests;

public sealed class DictionaryApiExceptionTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"code\":20006}", false)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"code\":\"20006\"}", false)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"code\":20001}", true)]
    [InlineData(HttpStatusCode.Forbidden, "{\"code\":20001}", false)]
    [InlineData(HttpStatusCode.Forbidden, "<html>private-account</html>", false)]
    public void RetryOnlyAppliesToUnauthorizedSessions(HttpStatusCode status, string body, bool retry)
    {
        var exception = DictionaryApiException.FromResponse(status, body);
        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(retry, exception.CanRetryAfterSessionSync);
        Assert.DoesNotContain("private-account", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"code\":{\"secret\":\"private-detail\"}}")]
    [InlineData("{\"code\":9999999999999999999999}")]
    [InlineData("{\"code\":\"private-detail\"}")]
    public void MalformedCodesNeverExposeResponseBodies(string body)
    {
        var exception = DictionaryApiException.FromResponse(HttpStatusCode.BadRequest, body);
        Assert.Null(exception.ApiCode);
        Assert.DoesNotContain("private-detail", exception.Message);
    }
}
