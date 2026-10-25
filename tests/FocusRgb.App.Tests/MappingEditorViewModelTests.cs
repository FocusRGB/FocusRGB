using System.Text.Json.Nodes;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.Tests
{
    public sealed class MappingEditorViewModelTests
    {
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
            Assert.Equal("Remove Programming mapping", mapping.RemoveAccessibleName);
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
            Assert.True(editor.IsDirty);
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
