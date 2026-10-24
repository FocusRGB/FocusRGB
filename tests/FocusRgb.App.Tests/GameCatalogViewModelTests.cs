using FocusRgb.App.ViewModels;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class GameCatalogViewModelTests : IDisposable
    {
        private static readonly DateTimeOffset Start =
            new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        private readonly TempDirectory _tempDirectory = new(nameof(GameCatalogViewModelTests));

        [Fact]
        public async Task ManualGame_SavePersistsAndBecomesAMappingTarget()
        {
            var store = new LocalStateStore(_tempDirectory.Path);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var viewModel = MainWindowViewModel.CreatePersistent(startup.Snapshot!, store);
            viewModel.StartNewManualGame();
            viewModel.GameName = "Local game";
            viewModel.ManualGameExecutable = "Local.exe";
            viewModel.AddManualGameExecutable();

            await viewModel.SaveManualGameAsync(TestContext.Current.CancellationToken);
            var restarted = await new LocalStateStore(_tempDirectory.Path).InitializeAsync(
                Start.AddMinutes(1), TestContext.Current.CancellationToken);

            var saved = Assert.Single(restarted.Snapshot!.GameCatalog.Games!);
            Assert.Equal("manual", saved.Launcher);
            Assert.Equal(["Local.exe"], saved.Executables);
            Assert.Contains(viewModel.MappingEditor.Targets, target =>
                target.GameLauncher == "manual" && target.GameId == saved.Id);
        }

        [Fact]
        public async Task DiscoveryFailure_KeepsCachedExecutablesAndShowsUnavailableStatus()
        {
            var store = new LocalStateStore(_tempDirectory.Path);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var viewModel = MainWindowViewModel.CreatePersistent(startup.Snapshot!, store);
            var catalog = new PluginCatalogResult
            {
                Support = "supported",
                RefreshedAt = Start,
                Items =
                [
                    new PluginCatalogItem
                    {
                        ResourceId = "570",
                        DisplayName = "Dota 2",
                        Available = true,
                        Game = new PluginGameCatalogMetadata { Executables = ["Dota.exe"] }
                    }
                ]
            };
            await viewModel.RefreshGameCatalogAsync(
                "steam", new PluginGatewayResult<PluginCatalogResult>(catalog),
                TestContext.Current.CancellationToken);

            await viewModel.RefreshGameCatalogAsync(
                "steam", new PluginGatewayResult<PluginCatalogResult>("plugin-absent", "Steam discovery is offline."),
                TestContext.Current.CancellationToken);

            var cached = Assert.Single(viewModel.Games);
            Assert.False(cached.Available);
            Assert.Equal(["Dota.exe"], cached.Executables);
            Assert.Contains("offline", cached.Status, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose() => _tempDirectory.Dispose();
    }
}
