using System.Text.Json.Nodes;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.Tests
{
    public sealed class MappingEditorViewModelTests
    {
        [Fact]
        public void Reorder_CommitsOnceAfterMultiplePreviewPositions()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.LivePreview = true;
            var original = editor.Mappings.ToArray();
            var changes = 0;
            editor.Mappings.CollectionChanged += (_, _) => changes++;
            Assert.True(editor.BeginReorder(original[2]));
            editor.PreviewReorder(1);
            editor.PreviewReorder(0);
            Assert.Equal(original, editor.Mappings);
            Assert.False(editor.IsDirty);
            Assert.Equal(0, editor.PreviewRevision);
            Assert.Equal([1, 2, 3], editor.Mappings.Select(row => row.Position));

            Assert.True(editor.CommitReorder());

            Assert.Equal([original[2], original[0], original[1]], editor.Mappings);
            Assert.Equal([1, 2, 3], editor.Mappings.Select(row => row.Position));
            Assert.False(editor.IsDirty);
            Assert.Equal(1, editor.PreviewRevision);
            Assert.Equal(1, changes);
            Assert.False(editor.CommitReorder());
            Assert.Equal(1, editor.PreviewRevision);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reorder_CancelPreservesOriginalDraftAndPreview(bool alreadyDirty)
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.LivePreview = true;
            if (alreadyDirty) editor.Mappings[0].Eligibility = "while-running";
            var original = editor.Mappings.ToArray();
            var revision = editor.PreviewRevision;
            Assert.True(editor.BeginReorder(original[0]));
            editor.PreviewReorder(2);
            editor.CancelReorder();
            editor.PreviewReorder(1);
            Assert.Equal(original, editor.Mappings);
            Assert.Equal(alreadyDirty, editor.IsDirty);
            Assert.Equal(revision, editor.PreviewRevision);
            Assert.Null(editor.ReorderingMapping);
            Assert.Equal(-1, editor.ReorderIndex);
            Assert.False(editor.CommitReorder());
        }

        [Theory]
        [InlineData(0, 100, 2, true)]
        [InlineData(2, -100, 0, true)]
        [InlineData(0, -1, 0, false)]
        [InlineData(2, 3, 2, false)]
        public void Reorder_ClampsAtEdgesAndSkipsUnchangedDrop(int start, int preview, int expected, bool changed)
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var item = editor.Mappings[start];
            Assert.True(editor.BeginReorder(item));
            Assert.False(editor.BeginReorder(editor.Mappings[1]));
            editor.PreviewReorder(preview);
            Assert.Equal(expected, editor.ReorderIndex);
            Assert.Equal(changed, editor.CommitReorder());
            Assert.Same(item, editor.Mappings[expected]);
            Assert.False(editor.IsDirty);
        }

        [Fact]
        public void Reorder_RejectsForeignRowsAndDoesNotOverwriteConcurrentChanges()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            Assert.False(editor.BeginReorder(MainWindowViewModel.CreatePreview().MappingEditor.Mappings[0]));
            Assert.True(editor.BeginReorder(editor.Mappings[0]));
            editor.PreviewReorder(2);
            editor.Remove(editor.Mappings[2]);
            var changed = editor.Mappings.ToArray();
            Assert.False(editor.CommitReorder());
            Assert.Equal(changed, editor.Mappings);
        }

        [Theory]
        [InlineData(-10, -12)]
        [InlineData(0, -12)]
        [InlineData(18, -6)]
        [InlineData(50, 0)]
        [InlineData(182, 6)]
        [InlineData(210, 12)]
        public void MappingAutoScroll_ScalesAndBoundsSpeedAtEdges(double pointerY, double expected) =>
            Assert.Equal(expected, MainWindow.MappingAutoScrollDelta(pointerY, 200));

        [Theory]
        [InlineData(0, -1, 0)]
        [InlineData(0, 1, 1)]
        [InlineData(2, 1, 2)]
        [InlineData(2, -1, 1)]
        public void Move_ChangesOneStepAndStopsAtEdges(int start, int offset, int expected)
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var item = editor.Mappings[start];
            editor.Move(item, offset);
            Assert.Same(item, editor.Mappings[expected]);
            Assert.False(editor.IsDirty);
            Assert.Equal([1, 2, 3], editor.Mappings.Select(row => row.Position));
            Assert.All(editor.Mappings, row =>
            {
                Assert.Equal(row.Position > 1, row.CanMoveUp);
                Assert.Equal(row.Position < editor.Mappings.Count, row.CanMoveDown);
            });
        }

        [Fact]
        public void MoveAvailability_RefreshesWhenLastRowIsRemoved()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var middle = editor.Mappings[1];
            var changed = new List<string?>();
            middle.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
            editor.Remove(editor.Mappings.Last());
            Assert.False(middle.CanMoveDown);
            Assert.Contains(nameof(middle.CanMoveDown), changed);
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(3, -3, false)]
        [InlineData(4, 0, true)]
        [InlineData(0, -4, true)]
        public void MappingDrag_WaitsForMovementThreshold(double x, double y, bool expected) =>
            Assert.Equal(expected, MainWindow.HasMappingDragThreshold(default, new Avalonia.Point(x, y)));

        [Fact]
        public void CreatePreview_LoadsNamedGroupsAndMappingsInSavedOrder()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            Assert.Equal(["Programming", "Movie", "Music"],
                editor.ApplicationGroups.Select(group => group.DisplayName));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
            Assert.Equal(["Code.exe", "devenv.exe"], editor.ApplicationGroups[0].Executables);
            Assert.Equal("programming", editor.Mappings[0].SelectedApplicationGroup.Id);
        }

        [Fact]
        public void MappingRowsExposeUsefulAccessibleNames()
        {
            var mapping = MainWindowViewModel.CreatePreview().MappingEditor.Mappings[0];

            Assert.Equal("Programming maps to Programming, while-focused", mapping.AccessibleName);
            Assert.Equal(mapping.AccessibleName, mapping.ToString());
            Assert.Equal("Remove Programming Focus", mapping.RemoveAccessibleName);
            Assert.Equal("Programming", mapping.SelectedApplicationGroup.ToString());
        }

        [Fact]
        public void ApplicationGroup_AddsUniqueExecutableNamesAndRemovesSelection()
        {
            var group = MainWindowViewModel.CreatePreview().ApplicationGroups[0];

            group.AddExecutable(@"C:\Tools\Preview.exe");
            group.AddExecutable(@"D:\Other\preview.EXE");
            group.SelectedExecutable = "Preview.exe";
            group.RemoveSelectedExecutable();

            Assert.DoesNotContain(group.Executables, name =>
                string.Equals(name, "Preview.exe", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void MappingChange_WaitsForApply_WhenLivePreviewIsOff()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.SelectedMapping!.Eligibility = "while-running";

            Assert.True(editor.IsDirty);
            Assert.Equal("while-focused", editor.AppliedMappings[0].Eligibility);

            editor.Apply();

            Assert.False(editor.IsDirty);
            Assert.Equal("while-running", editor.AppliedMappings[0].Eligibility);
        }

        [Fact]
        public void MoveAndDrop_ChangeOrderAndPositions()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.Move(editor.Mappings[2], -1);
            editor.MoveBefore(editor.Mappings[1], editor.Mappings[0]);

            Assert.Equal(["music", "programming", "movie"], editor.Mappings.Select(mapping => mapping.Id));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
            Assert.False(editor.IsDirty);
        }

        [Fact]
        public void AddMapping_UsesSelectedApplicationGroupAndProfile()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.NewApplicationGroup = editor.ApplicationGroups.Single(group => group.Id == "music");
            editor.NewProfile = editor.Profiles.Single(profile => profile.Id == "movie");

            editor.SaveMappingAs();

            var added = editor.Mappings[^1];
            Assert.Equal("music", added.SelectedApplicationGroup.Id);
            Assert.Equal("movie", added.SelectedProfile.Id);
        }

        [Fact]
        public void AddGameMappings_GeneratesValidIdsForWildcardAndOpaqueTargets()
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            var opaqueId = "APP_ID:" + new string('X', 100) + ".release";
            var catalog = new GameCatalogDocument
            {
                SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
                Games =
                [
                    new GameCatalogEntry
                    {
                        Launcher = "steam",
                        Id = opaqueId,
                        DisplayName = "Opaque game",
                        Executables = ["Game.exe"],
                        Available = true,
                        LastRefreshedAt = DateTimeOffset.UtcNow
                    }
                ]
            };
            var editor = MainWindowViewModel.CreatePreview(settings, catalog).MappingEditor;
            editor.NewProfile = editor.Profiles.Single(profile => profile.Id == "movie");

            editor.NewTarget = editor.Targets.Single(target =>
                target.GameLauncher == "*" && target.GameId == "*");
            editor.SaveMappingAs();
            editor.NewTarget = editor.Targets.Single(target => target.GameId == opaqueId);
            editor.SaveMappingAs();

            var generated = editor.Mappings.TakeLast(2).Select(mapping => mapping.Id).ToList();
            Assert.All(generated, id =>
            {
                Assert.Matches("^[a-z0-9][a-z0-9-]{0,63}$", id);
                Assert.InRange(id.Length, 1, 64);
            });
            Assert.StartsWith("any-game-", generated[0], StringComparison.Ordinal);
        }

        [Fact]
        public void MoveBefore_UsesInsertionPosition()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            editor.MoveBefore(editor.Mappings[2], editor.Mappings[0]);
            Assert.Equal(["music", "programming", "movie"], editor.Mappings.Select(mapping => mapping.Id));
        }

        [Fact]
        public void MoveAfter_InsertsAfterTargetForEitherDragDirection()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            editor.MoveAfter(editor.Mappings[2], editor.Mappings[0]);
            Assert.Equal(["programming", "music", "movie"], editor.Mappings.Select(mapping => mapping.Id));

            editor.MoveAfter(editor.Mappings[0], editor.Mappings[2]);
            Assert.Equal(["music", "movie", "programming"], editor.Mappings.Select(mapping => mapping.Id));
        }

        [Fact]
        public void Remove_DeletesSelectedMappingAndRenumbersRows()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            editor.Remove(editor.Mappings[1]);

            Assert.Equal(["programming", "music"], editor.Mappings.Select(mapping => mapping.Id));
            Assert.Equal([1, 2], editor.Mappings.Select(mapping => mapping.Position));
        }

        [Fact]
        public void ProfileOwnsItsEditableEffectAndLayout()
        {
            var profile = MainWindowViewModel.CreatePreview().Profiles.Single(item => item.Id == "movie");

            profile.EffectName = "New effect";
            profile.LayoutName = "Monitor layout";

            Assert.Equal("New effect", profile.EffectName);
            Assert.Equal("Monitor layout", profile.LayoutName);
        }

        [Fact]
        public void GameRule_IsFirstClassAndPreservesUnknownFields()
        {
            var root = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            root["rules"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"example-game","profile":"movie","match":{"game":{"launcher":"example","id":"sample-game"}},"eligibility":"while-running","enabled":false,"future":1}
                """));
            var settings = SettingsJson.Load(root.ToJsonString()).Settings!;

            var editor = MainWindowViewModel.CreatePreview(settings).MappingEditor;
            var saved = editor.CreateRuleSettings();

            Assert.Equal(4, editor.Mappings.Count);
            var projected = editor.Mappings.Single(mapping => mapping.Id == "example-game");
            Assert.Equal("example", projected.SelectedTarget.GameLauncher);
            Assert.Equal("sample-game", projected.SelectedTarget.GameId);
            var game = Assert.Single(saved, rule => rule.Id == "example-game");
            Assert.Equal("sample-game", game.Match!.Game!.Id);
            Assert.False(game.Enabled);
            Assert.Equal(1, game.AdditionalProperties!["future"].GetInt32());
        }

        [Fact]
        public void GameAndApplicationRules_ShareOneOrderedEditor()
        {
            var root = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            root["rules"]!.AsArray().Insert(1, JsonNode.Parse("""
                {"id":"any-game","profile":"movie","match":{"game":{"launcher":"*","id":"*"}},"eligibility":"while-running","enabled":true}
                """));
            var editor = MainWindowViewModel.CreatePreview(
                SettingsJson.Load(root.ToJsonString()).Settings!).MappingEditor;

            editor.Move(editor.Mappings.Single(mapping => mapping.Id == "any-game"), -1);

            Assert.Equal(["any-game", "programming", "movie", "music"],
                editor.CreateRuleSettings().Select(rule => rule.Id));
        }

        [Fact]
        public void RemovingMapping_DoesNotMoveFollowingApplicationAheadOfGameRule()
        {
            var root = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            root["rules"]!.AsArray().Insert(1, JsonNode.Parse("""
                {"id":"example-game","profile":"movie","match":{"game":{"launcher":"example","id":"sample-game"}},"eligibility":"while-running","enabled":true}
                """));
            var editor = MainWindowViewModel.CreatePreview(
                SettingsJson.Load(root.ToJsonString()).Settings!).MappingEditor;

            editor.Remove(editor.Mappings.Single(mapping => mapping.Id == "programming"));
            var saved = editor.CreateRuleSettings();

            Assert.Equal(["example-game", "movie", "music"], saved.Select(rule => rule.Id));
        }
    }
}
