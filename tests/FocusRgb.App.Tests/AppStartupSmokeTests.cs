using System.Diagnostics.CodeAnalysis;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App.Tests
{
    public sealed class AppStartupSmokeTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.App.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        [SuppressMessage("Usage", "xUnit1031:Do not use blocking task operations", Justification = "This regression reproduces a blocked UI thread.")]
        [SuppressMessage("Usage", "xUnit1051:Use TestContext.Current.CancellationToken", Justification = "The five-second timeout is the assertion under test.")]
        public void Startup_Completes_WhenUiSynchronizationContextIsBlocked()
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                var store = new LocalStateStore(_directory);
                var startup = store.InitializeAsync(DateTimeOffset.UtcNow)
                    .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                Assert.True(startup.IsSuccess, startup.Error?.Message);
                Assert.NotNull(MainWindowViewModel.CreatePersistent(startup.Snapshot!, store));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        private sealed class NonPumpingSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object? state)
            {
            }
        }
    }
}
