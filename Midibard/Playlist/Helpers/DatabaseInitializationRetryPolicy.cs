using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using LiteDB;

namespace MidiBard.Playlist.Helpers;

internal static class DatabaseInitializationRetryPolicy
{
    private static readonly ThreadLocal<Random> Random = new(() => new Random(Guid.NewGuid().GetHashCode()));

    public static T Execute<T>(
        Func<T> operation,
        bool retryEnabled,
        TimeSpan timeout,
        Action<Exception, int, TimeSpan>? onRetry = null,
        Func<int, TimeSpan>? delayFactory = null,
        Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var elapsed = Stopwatch.StartNew();
        var attempt = 0;

        while (true)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (retryEnabled && IsTransientDatabaseAccessException(ex))
            {
                if (elapsed.Elapsed >= timeout)
                    throw;

                attempt++;
                var delay = delayFactory?.Invoke(attempt) ?? CreateDefaultDelay();
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                var remaining = timeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw;

                if (delay > remaining)
                    delay = remaining;

                onRetry?.Invoke(ex, attempt, delay);
                (sleep ?? Thread.Sleep)(delay);
            }
        }
    }

    public static bool IsTransientDatabaseAccessException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is AggregateException aggregateException)
            return aggregateException.InnerExceptions.Any(IsTransientDatabaseAccessException);

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is IOException or UnauthorizedAccessException)
                return true;

            if (current is LiteException && IsTransientDatabaseMessage(current.Message))
                return true;
        }

        return false;
    }

    private static TimeSpan CreateDefaultDelay()
    {
        return TimeSpan.FromMilliseconds(Random.Value!.Next(100, 351));
    }

    private static bool IsTransientDatabaseMessage(string message)
    {
        return message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }
}
