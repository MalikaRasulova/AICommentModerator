using Microsoft.Extensions.Options;

namespace AICommentModerator.Tests;

/// <summary>Hands the same options object to every caller - enough for the unit tests.</summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}
