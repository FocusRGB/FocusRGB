using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Selection;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class GameCatalogViewModelTests : IDisposable
    {
        private static readonly DateTimeOffset Start =
            new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        private readonly TempDirectory _tempDirectory = new(nameof(GameCatalogViewModelTests));

        [Fact]
        public async Task ExecutableSelection_PersistsAcrossRefreshOfflineAndRestartWithoutLosingOtherDrafts()
        {
            var (viewModel, service) = await CreateAsync();
            await viewModel.RefreshGameCatalogAsync("steam", ExecutableCatalog(), TestContext.Current.CancellationToken);
            viewModel.EditingGame = Assert.Single(viewModel.Games);
            viewModel.GameExecutableOptions.Single(option => option.Name == "Tool.exe").IsSelected = false;
            var editor = viewModel.MappingEditor;
            editor.SelectedMapping!.Eligibility = "while-running";
            viewModel.ProfileName = "Unsaved profile";
            await viewModel.SaveDiscoveredGameExecutablesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["Main.exe"], Assert.Single(service.Current.GameCatalog.Games!).SelectedExecutables);
            Assert.Same(editor, viewModel.MappingEditor);
            Assert.True(editor.IsDirty);
            Assert.Equal("Unsaved profile", viewModel.ProfileName);
            Assert.Contains(editor.Targets, target => target.GameLauncher == "steam" && target.GameId == "570");

            await viewModel.RefreshGameCatalogAsync("steam", ExecutableCatalog(updated: true), TestContext.Current.CancellationToken);
            await viewModel.RefreshGameCatalogAsync("steam", new PluginGatewayResult<PluginCatalogResult>("plugin-absent", "Offline"), TestContext.Current.CancellationToken);
            var restarted = await new LocalStateStore(_tempDirectory.Path).InitializeAsync(Start, TestContext.Current.CancellationToken);
            var saved = Assert.Single(restarted.Snapshot!.GameCatalog.Games!);
            Assert.Equal("570", saved.Id);
            Assert.Equal("steam", saved.Launcher);
            Assert.Equal(["Main.exe"], saved.SelectedExecutables);
            Assert.Equal(["Updated.exe", "Tool.exe"], saved.Executables);
            Assert.False(saved.Available);
            Assert.Contains(viewModel.GameExecutableOptions, option => option.Name == "Main.exe" && option.IsSelected);

            await viewModel.RefreshGameCatalogAsync("steam", new PluginGatewayResult<PluginCatalogResult>(new()
            {
                Support = "supported", RefreshedAt = Start, Items = []
            }), TestContext.Current.CancellationToken);
            Assert.Equal(["Main.exe"], Assert.Single(service.Current.GameCatalog.Games!).SelectedExecutables);
        }

        [Fact]
        public async Task ExecutableSelection_PreservesUnsavedChoicesOnRefreshAndRejectsEmptySelection()
        {
            var (viewModel, service) = await CreateAsync();
            await viewModel.RefreshGameCatalogAsync("steam", ExecutableCatalog(), TestContext.Current.CancellationToken);
            viewModel.EditingGame = Assert.Single(viewModel.Games);
            viewModel.GameExecutableOptions.Single(option => option.Name == "Tool.exe").IsSelected = false;
            await viewModel.RefreshGameCatalogAsync("steam", ExecutableCatalog(updated: true), TestContext.Current.CancellationToken);
            Assert.Equal(["Main.exe"], viewModel.GameExecutableOptions.Where(option => option.IsSelected).Select(option => option.Name));
            foreach (var option in viewModel.GameExecutableOptions) option.IsSelected = false;
            var revision = service.Current.SettingsRevision;
            await viewModel.SaveDiscoveredGameExecutablesAsync(TestContext.Current.CancellationToken);
            Assert.Contains("at least one", viewModel.GameEditorStatus, StringComparison.Ordinal);
            Assert.Equal(revision, service.Current.SettingsRevision);
            Assert.Null(Assert.Single(service.Current.GameCatalog.Games!).SelectedExecutables);
        }

        private static PluginGatewayResult<PluginCatalogResult> ExecutableCatalog(bool updated = false) => new(new()
        {
            Support = "supported", RefreshedAt = Start,
            Items = [new PluginCatalogItem
            {
                ResourceId = "570", DisplayName = "Example game", Available = true,
                Game = new PluginGameCatalogMetadata { Executables = [updated ? "Updated.exe" : "Main.exe", "Tool.exe"] }
            }]
        });

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
                        ResourceId = "sample-game",
                        DisplayName = "Sample Game",
                        Available = true,
                        Game = new PluginGameCatalogMetadata { Executables = ["Sample.exe"] }
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
            Assert.Equal(["Sample.exe"], cached.Executables);
            Assert.Contains("offline", cached.Status, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnsupportedPluginWithoutCachedGames_DoesNotAdvanceRevision()
        {
            var (viewModel, service) = await CreateAsync();
            var revision = service.Current.SettingsRevision;

            await viewModel.RefreshGameCatalogAsync(
                "signalrgb",
                new PluginGatewayResult<PluginCatalogResult>(
                    "capability-unsupported", "SignalRGB does not discover games."),
                TestContext.Current.CancellationToken);

            Assert.Equal(revision, service.Current.SettingsRevision);
            Assert.Empty(service.Current.GameCatalog.Games!);
        }

        [Fact]
        public async Task CatalogRefresh_PreservesDirtyEditorsAndUpdatesGameTargetsInPlace()
        {
            var (viewModel, _) = await CreateAsync();
            var editor = viewModel.MappingEditor;
            viewModel.ProfileName = "Unsaved profile name";
            viewModel.StartNewManualGame();
            viewModel.GameName = "Unsaved manual game";
            viewModel.ManualGameExecutable = "Draft.exe";
            viewModel.AddManualGameExecutable();

            await viewModel.RefreshGameCatalogAsync(
                "steam", SupportedCatalog(), TestContext.Current.CancellationToken);

            Assert.False(viewModel.HasSettingsConflict);
            Assert.Same(editor, viewModel.MappingEditor);
            Assert.Equal("Unsaved profile name", viewModel.ProfileName);
            Assert.Equal("Unsaved manual game", viewModel.GameName);
            Assert.Equal(["Draft.exe"], viewModel.DraftGameExecutables);
            Assert.Contains(editor.Targets, target =>
                target.GameLauncher == "steam" && target.GameId == "sample-game");
        }

        [Fact]
        public async Task ManagedLifecycleRefresh_LoadsThenMarksCachedGamesUnavailable()
        {
            var (viewModel, _) = await CreateAsync();
            var loadCalls = 0;

            await App.RefreshManagedGameCatalogAsync(
                viewModel,
                "steam",
                _ =>
                {
                    loadCalls++;
                    return Task.FromResult(SupportedCatalog());
                },
                TestContext.Current.CancellationToken);
            await App.RefreshManagedGameCatalogAsync(
                viewModel, "steam", null, TestContext.Current.CancellationToken);

            Assert.Equal(1, loadCalls);
            var cached = Assert.Single(viewModel.Games);
            Assert.False(cached.Available);
            Assert.Contains("not running", cached.UnavailableReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ManualGame_AddsSelectedDetectedExecutable()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.StartNewManualGame();
            await viewModel.ObserveApplicationsAsync(new ApplicationObservation
            {
                RunningExecutables = ["Game.exe", "Other.exe"]
            }, TestContext.Current.CancellationToken);
            viewModel.SelectedDetectedExecutable = "Game.exe";

            viewModel.AddDetectedGameExecutable();

            Assert.Equal(["Game.exe"], viewModel.DraftGameExecutables);
        }

        [Fact]
        public void DiscoveredGame_ExposesCachedExecutablesButRejectsDraftChanges()
        {
            var viewModel = MainWindowViewModel.CreatePreview(
                SettingsJson.LoadDefaults().Settings!,
                new GameCatalogDocument
                {
                    SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
                    Games =
                    [
                        new GameCatalogEntry
                        {
                            Launcher = "example",
                            Id = "sample-game",
                            DisplayName = "Sample Game",
                            Executables = ["Sample.exe"],
                            Available = true,
                            LastRefreshedAt = Start
                        }
                    ]
                });
            viewModel.EditingGame = Assert.Single(viewModel.Games);

            Assert.False(viewModel.CanEditGame);
            Assert.Equal(["Sample.exe"], viewModel.DraftGameExecutables);
            Assert.False(viewModel.AddDraftGameExecutable("Other.exe"));
            Assert.Equal(["Sample.exe"], viewModel.DraftGameExecutables);
        }

        [Fact]
        public async Task CatalogRefresh_SkipsInvalidItemAndKeepsValidItems()
        {
            var (viewModel, service) = await CreateAsync();
            var valid = Assert.Single(SupportedCatalog().Value!.Items);
            var catalog = new PluginCatalogResult
            {
                Support = "supported", RefreshedAt = Start,
                Items = [valid, new PluginCatalogItem
                {
                    ResourceId = "invalid", DisplayName = "Invalid", Available = true,
                    Game = new PluginGameCatalogMetadata { Executables = ["no-extension"] }
                }]
            };

            await viewModel.RefreshGameCatalogAsync("example", new PluginGatewayResult<PluginCatalogResult>(catalog),
                TestContext.Current.CancellationToken);

            Assert.Equal(valid.ResourceId, Assert.Single(service.Current.GameCatalog.Games!).Id);
            Assert.Contains("Skipped 1", viewModel.GameEditorStatus, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CatalogRefresh_IgnoresTimestampOnlyChanges()
        {
            var (viewModel, service) = await CreateAsync();
            await viewModel.RefreshGameCatalogAsync("example", SupportedCatalog(), TestContext.Current.CancellationToken);
            var revision = service.Current.SettingsRevision;
            var updated = new PluginCatalogResult
            {
                Support = "supported", RefreshedAt = Start.AddMinutes(1),
                Items = SupportedCatalog().Value!.Items
            };

            await viewModel.RefreshGameCatalogAsync("example", new PluginGatewayResult<PluginCatalogResult>(updated),
                TestContext.Current.CancellationToken);

            Assert.Equal(revision, service.Current.SettingsRevision);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task CatalogRefresh_MergesIncompleteResultsAndRetainsUnvisitedGames(bool explicitReason, bool hasTimestamp)
        {
            var (viewModel, service) = await CreateAsync();
            var original = Assert.Single(SupportedCatalog().Value!.Items);
            var unvisited = new PluginCatalogItem
            {
                ResourceId = "unvisited", DisplayName = "Unvisited", Available = true,
                Game = new PluginGameCatalogMetadata { Executables = ["Unvisited.exe"] }
            };
            await viewModel.RefreshGameCatalogAsync("example", new PluginGatewayResult<PluginCatalogResult>(new()
            {
                Support = "supported", RefreshedAt = Start, Items = [original, unvisited]
            }), TestContext.Current.CancellationToken);
            await viewModel.RefreshGameCatalogAsync("other", SupportedCatalog(), TestContext.Current.CancellationToken);
            var changed = new PluginCatalogItem
            {
                ResourceId = original.ResourceId, DisplayName = "Updated", Available = true,
                Game = new PluginGameCatalogMetadata { Executables = ["Updated.exe"] }
            };
            var added = new PluginCatalogItem
            {
                ResourceId = "new", DisplayName = "New", Available = true,
                Game = new PluginGameCatalogMetadata { Executables = ["New.exe"] }
            };

            await viewModel.RefreshGameCatalogAsync("example", new PluginGatewayResult<PluginCatalogResult>(new()
            {
                Support = "supported", RefreshedAt = hasTimestamp ? Start : null, Items = [changed, added],
                Reason = explicitReason ? new PluginReason { Code = "catalog-scan-incomplete", Message = "Scanning more games." } : null
            }), TestContext.Current.CancellationToken);

            var cached = service.Current.GameCatalog.Games!.Where(game => game.Launcher == "example").ToArray();
            Assert.Equal(3, cached.Length);
            Assert.Equal(["Updated.exe"], cached.Single(game => game.Id == original.ResourceId).Executables);
            Assert.Equal(["Unvisited.exe"], cached.Single(game => game.Id == "unvisited").Executables);
            Assert.True(cached.Single(game => game.Id == "unvisited").Available);
            Assert.Contains(service.Current.GameCatalog.Games!, game => game.Launcher == "other");
            Assert.Contains(explicitReason ? "Scanning more games." : "incomplete", viewModel.GameEditorStatus, StringComparison.Ordinal);
            var persisted = GameCatalogJson.Load(await File.ReadAllTextAsync(
                Path.Combine(_tempDirectory.Path, "games.json"), TestContext.Current.CancellationToken));
            Assert.Equal(3, persisted.Catalog!.Games!.Count(game => game.Launcher == "example"));

            await viewModel.RefreshGameCatalogAsync("example", SupportedCatalog(), TestContext.Current.CancellationToken);

            Assert.Single(service.Current.GameCatalog.Games!, game => game.Launcher == "example");
            Assert.DoesNotContain("Scanning", viewModel.GameEditorStatus, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CatalogRefresh_ShowsProgressForEmptyUnchangedPartialResult()
        {
            var (viewModel, service) = await CreateAsync();
            await viewModel.RefreshGameCatalogAsync("example", SupportedCatalog(), TestContext.Current.CancellationToken);
            var revision = service.Current.SettingsRevision;

            await viewModel.RefreshGameCatalogAsync("example", new PluginGatewayResult<PluginCatalogResult>(new()
            {
                Support = "supported", Items = [],
                Reason = new PluginReason { Code = "catalog-scan-incomplete", Message = "Scan is running." }
            }), TestContext.Current.CancellationToken);

            Assert.Equal(revision, service.Current.SettingsRevision);
            Assert.Single(service.Current.GameCatalog.Games!);
            Assert.Equal("Scan is running.", viewModel.GameEditorStatus);
        }

        private async Task<(MainWindowViewModel ViewModel, ApplicationStateService Service)> CreateAsync()
        {
            var store = new LocalStateStore(_tempDirectory.Path);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var service = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                store,
                gameCatalog: startup.Snapshot.GameCatalog);
            return (MainWindowViewModel.CreatePersistent(
                startup.Snapshot, store, applicationState: service), service);
        }

        private static PluginGatewayResult<PluginCatalogResult> SupportedCatalog() => new(
            new PluginCatalogResult
            {
                Support = "supported",
                RefreshedAt = Start,
                Items =
                [
                    new PluginCatalogItem
                    {
                        ResourceId = "sample-game",
                        DisplayName = "Sample Game",
                        Available = true,
                        Game = new PluginGameCatalogMetadata { Executables = ["Sample.exe"] }
                    }
                ]
            });

        public void Dispose() => _tempDirectory.Dispose();
    }
}
