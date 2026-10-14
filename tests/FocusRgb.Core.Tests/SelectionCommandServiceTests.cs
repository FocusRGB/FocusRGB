using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;

namespace FocusRgb.Core.Tests
{
    public sealed class SelectionCommandServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 14, 14, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task SelectProfileAsync_PersistsManualIntentBeforeReturningStatus()
        {
            var store = new RecordingStateStore();
            var service = new SelectionCommandService(store);

            var result = await service.SelectProfileAsync(
                Settings(),
                State(),
                "movie",
                Now,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Same(store.SavedState, result.State);
            Assert.Equal("manual", result.State!.Mode);
            Assert.Equal("movie", result.State.ManualProfile);
            Assert.Equal("movie", result.Status!.Desired.ProfileId);
            Assert.Equal("manual", result.Status.Desired.Reason.Kind);
            Assert.Null(result.Status.AppliedProfileId);
        }

        [Fact]
        public async Task SetModeAsync_AutomaticRetainsDesiredProfileUntilObservationsExist()
        {
            var service = new SelectionCommandService(new RecordingStateStore());

            var result = await service.SetModeAsync(
                Settings(),
                State("music"),
                "automatic",
                Now,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("automatic", result.State!.Mode);
            Assert.Equal("music", result.Status!.Desired.ProfileId);
            Assert.Equal("observation-pending", result.Status.Desired.Reason.Kind);
        }

        [Fact]
        public async Task SelectProfileAsync_ReturnsFailureWithoutNewState_WhenStorageFails()
        {
            var service = new SelectionCommandService(new RecordingStateStore(fail: true));

            var result = await service.SelectProfileAsync(
                Settings(),
                State(),
                "movie",
                Now,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("persistence-failed", result.ErrorCode);
            Assert.Contains("disk unavailable", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Null(result.State);
        }

        private static SettingsDocument Settings() => SettingsJson.LoadDefaults().Settings!;

        private static SelectionStateDocument State(string profile = "default") => new()
        {
            SchemaVersion = 1,
            Mode = "manual",
            ManualProfile = profile,
            LastSelectedProfile = profile,
            UpdatedAt = Now.AddMinutes(-1)
        };

        private sealed class RecordingStateStore(bool fail = false) : ISelectionStateStore
        {
            public SelectionStateDocument? SavedState { get; private set; }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                if (fail)
                {
                    return Task.FromResult(SaveResult.Fail(new PersistenceError
                    {
                        FileName = LocalStateStore.StateFileName,
                        Message = "disk unavailable"
                    }));
                }

                SavedState = state;
                return Task.FromResult(SaveResult.Ok());
            }
        }
    }
}
