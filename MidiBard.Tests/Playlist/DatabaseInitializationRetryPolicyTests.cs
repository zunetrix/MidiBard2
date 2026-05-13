using System.IO;

using MidiBard.Playlist.Helpers;

namespace MidiBard.Tests.Playlist;

public class DatabaseInitializationRetryPolicyTests
{
    [Fact]
    public void Execute_TransientFailure_RetriesAndReturnsValue()
    {
        var attempts = 0;
        var retries = 0;

        var result = DatabaseInitializationRetryPolicy.Execute(
            () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new IOException("The process cannot access the file because it is being used by another process.");

                return 42;
            },
            retryEnabled: true,
            timeout: TimeSpan.FromSeconds(1),
            onRetry: (_, _, _) => retries++,
            delayFactory: _ => TimeSpan.Zero,
            sleep: _ => { });

        result.ShouldBe(42);
        attempts.ShouldBe(3);
        retries.ShouldBe(2);
    }

    [Fact]
    public void Execute_RetryDisabled_DoesNotRetryTransientFailure()
    {
        var attempts = 0;

        Should.Throw<IOException>(() => DatabaseInitializationRetryPolicy.Execute<object?>(
            () =>
            {
                attempts++;
                throw new IOException("The process cannot access the file because it is being used by another process.");
            },
            retryEnabled: false,
            timeout: TimeSpan.FromSeconds(1),
            delayFactory: _ => TimeSpan.Zero,
            sleep: _ => { }));

        attempts.ShouldBe(1);
    }

    [Fact]
    public void Execute_NonTransientFailure_DoesNotRetry()
    {
        var attempts = 0;

        Should.Throw<InvalidOperationException>(() => DatabaseInitializationRetryPolicy.Execute<object?>(
            () =>
            {
                attempts++;
                throw new InvalidOperationException("not transient");
            },
            retryEnabled: true,
            timeout: TimeSpan.FromSeconds(1),
            delayFactory: _ => TimeSpan.Zero,
            sleep: _ => { }));

        attempts.ShouldBe(1);
    }

    [Fact]
    public void IsTransientDatabaseAccessException_DetectsNestedAccessException()
    {
        var exception = new Exception(
            "wrapper",
            new UnauthorizedAccessException("Access to the path is denied."));

        DatabaseInitializationRetryPolicy.IsTransientDatabaseAccessException(exception).ShouldBeTrue();
    }
}
