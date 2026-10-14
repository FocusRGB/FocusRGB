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

        [Fact]
        public async Task SetModeAsync_ManualRestoresLastManualProfile()
        {
            var current = State("music");
            current = new SelectionStateDocument
            {
                SchemaVersion = current.SchemaVersion,
                Mode = "automatic",
                ManualProfile = "music",
                LastSelectedProfile = "movie",
                UpdatedAt = current.UpdatedAt
            };

            var result = await new SelectionCommandService(new RecordingStateStore()).SetModeAsync(
                Settings(), current, "manual", Now, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("music", result.Status!.Desired.ProfileId);
        }

        [Theory]
        [InlineData("invalid", "invalid-mode")]
        public async Task SetModeAsync_RejectsInvalidInput(string mode, string errorCode)
        {
            var result = await new SelectionCommandService(new RecordingStateStore()).SetModeAsync(
                Settings(), State(), mode, Now, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(errorCode, result.ErrorCode);
        }

        [Fact]
        public async Task SelectProfileAsync_RejectsUnknownProfile()
        {
            var result = await new SelectionCommandService(new RecordingStateStore()).SelectProfileAsync(
                Settings(), State(), "missing", Now, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("invalid-profile", result.ErrorCode);
        }

        [Fact]
        public async Task SaveCurrentAsync_PersistsCurrentIntent()
        {
            var store = new RecordingStateStore();
            var result = await new SelectionCommandService(store).SaveCurrentAsync(
                Settings(), State("music"), Now, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("music", store.SavedState!.ManualProfile);
            Assert.Equal(Now, store.SavedState.UpdatedAt);
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
