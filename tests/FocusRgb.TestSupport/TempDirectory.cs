namespace FocusRgb.TestSupport
{
    /// <summary>Owns one isolated test directory and retries cleanup when Windows briefly holds files.</summary>
    public sealed class TempDirectory : IDisposable, IAsyncDisposable
    {
        public TempDirectory(string suite)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(suite);
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "FocusRgb.Tests", suite, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; Directory.Exists(Path); attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (Exception exception) when (exception is (IOException or UnauthorizedAccessException)
                    && attempt < 49)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }
            }
        }
    }
}
