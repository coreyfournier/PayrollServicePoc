namespace PayrollService.IntegrationTests.Infrastructure;

public static class PollingHelper
{
    /// <summary>
    /// Polls until the condition returns true or the timeout is reached.
    /// Useful for waiting on async workflow completion and Kafka event propagation.
    /// </summary>
    public static async Task<T> WaitForAsync<T>(
        Func<Task<T>> query,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        string? timeoutMessage = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        interval ??= TimeSpan.FromSeconds(2);

        var deadline = DateTime.UtcNow + timeout.Value;
        T result = default!;

        while (DateTime.UtcNow < deadline)
        {
            result = await query();
            if (condition(result))
                return result;

            await Task.Delay(interval.Value);
        }

        throw new TimeoutException(
            timeoutMessage ?? $"Condition not met within {timeout.Value.TotalSeconds}s. Last result: {result}");
    }
}
