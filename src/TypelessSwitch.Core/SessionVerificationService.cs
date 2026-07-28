using TypelessSwitch.Core.Models;

namespace TypelessSwitch.Core;

public sealed class SessionVerificationService
{
    private readonly Func<CancellationToken, Task<TypelessSession?>> _readSession;

    public SessionVerificationService(SessionStoreService sessionStore)
        : this(sessionStore.ReadAsync) { }

    public SessionVerificationService(Func<CancellationToken, Task<TypelessSession?>> readSession) =>
        _readSession = readSession ?? throw new ArgumentNullException(nameof(readSession));

    public async Task<TypelessSession> VerifyAsync(
        TypelessSession expected,
        TimeSpan? initialDelay = null,
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var delay = initialDelay ?? TimeSpan.FromSeconds(1.5);
        var limit = timeout ?? TimeSpan.FromSeconds(7);
        var interval = pollingInterval ?? TimeSpan.FromMilliseconds(500);
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

        var deadline = DateTimeOffset.UtcNow + limit;
        var consecutiveMatches = 0;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypelessSession? actual = null;
            try { actual = await _readSession(cancellationToken); }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            { }

            if (AccountHealth.IsExpectedIdentity(actual, expected))
            {
                consecutiveMatches++;
                if (consecutiveMatches >= 2) return actual!;
            }
            else
            {
                consecutiveMatches = 0;
            }

            await Task.Delay(interval, cancellationToken);
        }

        throw new InvalidDataException("Typeless 启动后未能稳定读取到目标账号，切换已停止并将恢复原账号。");
    }
}
