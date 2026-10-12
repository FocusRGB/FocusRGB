using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class MappingEditorViewModelTests
    {
        [Fact]
        public void CreatePreview_LoadsRulesInSavedPriorityOrder()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            Assert.Equal(["Programming", "Movie", "Music"],
                editor.Mappings.Select(mapping => mapping.ApplicationName));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
            Assert.Equal("Code.exe, devenv.exe", editor.Mappings[0].Executables);
            Assert.Contains("stremio-shell-ng.exe", editor.Mappings[1].Executables, StringComparison.Ordinal);
            Assert.Contains("Spotify.exe", editor.Mappings[2].Executables, StringComparison.Ordinal);
        }

        [Fact]
        public void DraftChange_WaitsForApply_WhenLivePreviewIsOff()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var originalExecutables = editor.AppliedMappings[0].Executables;

            editor.SelectedMapping!.Executables = "player-preview.exe, second-player.exe";

            Assert.True(editor.IsDirty);
            Assert.Equal(0, editor.PreviewRevision);
            Assert.Equal(originalExecutables, editor.AppliedMappings[0].Executables);

            editor.Apply();

            Assert.False(editor.IsDirty);
            Assert.Equal(1, editor.AppliedRevision);
            Assert.Equal(1, editor.PreviewRevision);
            Assert.Equal("player-preview.exe, second-player.exe", editor.AppliedMappings[0].Executables);
        }

        [Fact]
        public void DraftChange_UpdatesPreviewButNotAppliedState_WhenLivePreviewIsOn()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.LivePreview = true;

            editor.SelectedMapping!.Eligibility = "while-running";

            Assert.True(editor.IsDirty);
            Assert.Equal(1, editor.PreviewRevision);
            Assert.Equal("while-focused", editor.AppliedMappings[0].Eligibility);
        }

        [Fact]
        public void Cancel_RestoresAppliedDraftAndPreview()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.LivePreview = true;
            editor.SelectedMapping!.ApplicationName = "Changed name";
            var changedPreviewRevision = editor.PreviewRevision;

            editor.Cancel();

            Assert.False(editor.IsDirty);
            Assert.Equal("Programming", editor.Mappings[0].ApplicationName);
            Assert.Equal(changedPreviewRevision + 1, editor.PreviewRevision);
            Assert.Contains("restored", editor.PreviewStatus, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MoveSelected_ChangesDraftOrderAndPositions()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.SelectedMapping = editor.Mappings[2];

            editor.MoveSelected(-1);

            Assert.Equal(["programming", "music", "movie"], editor.Mappings.Select(mapping => mapping.Id));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
            Assert.True(editor.IsDirty);
        }

        [Fact]
        public void CreatePreview_ProvidesEffectsAndDisablesUnavailableLayouts()
        {
            var mapping = MainWindowViewModel.CreatePreview().MappingEditor.SelectedMapping!;

            Assert.Equal(["Aurora", "Screen Ambient", "Logarithmic Visualizer"],
                mapping.Effects.Select(effect => effect.DisplayName));
            Assert.All(mapping.Effects, effect => Assert.True(effect.IsAvailable));
            Assert.False(mapping.CanChooseLayout);
            Assert.Contains("unavailable", mapping.LayoutStatus, StringComparison.OrdinalIgnoreCase);
        }
    }
}
