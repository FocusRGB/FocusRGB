using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class MappingEditorViewModelTests
    {
        [Fact]
        public void CreatePreview_LoadsRulesInSavedPriorityOrder()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;

            Assert.Equal(["Media Player", "Stremio", "VS Code"],
                editor.Mappings.Select(mapping => mapping.ApplicationName));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
        }

        [Fact]
        public void DraftChange_WaitsForApply_WhenLivePreviewIsOff()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var originalExecutable = editor.AppliedMappings[0].Executable;

            editor.SelectedMapping!.Executable = "player-preview.exe";

            Assert.True(editor.IsDirty);
            Assert.Equal(0, editor.PreviewRevision);
            Assert.Equal(originalExecutable, editor.AppliedMappings[0].Executable);

            editor.Apply();

            Assert.False(editor.IsDirty);
            Assert.Equal(1, editor.AppliedRevision);
            Assert.Equal(1, editor.PreviewRevision);
            Assert.Equal("player-preview.exe", editor.AppliedMappings[0].Executable);
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
            Assert.Equal("Media Player", editor.Mappings[0].ApplicationName);
            Assert.Equal(changedPreviewRevision + 1, editor.PreviewRevision);
            Assert.Contains("restored", editor.PreviewStatus, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MoveSelected_ChangesDraftOrderAndPositions()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.SelectedMapping = editor.Mappings[2];

            editor.MoveSelected(-1);

            Assert.Equal(["media-player", "code", "stremio"], editor.Mappings.Select(mapping => mapping.Id));
            Assert.Equal([1, 2, 3], editor.Mappings.Select(mapping => mapping.Position));
            Assert.True(editor.IsDirty);
        }

        [Fact]
        public void CreatePreview_ProvidesEffectsAndDisablesUnavailableLayouts()
        {
            var mapping = MainWindowViewModel.CreatePreview().MappingEditor.SelectedMapping!;

            Assert.Equal(["Aurora", "Logarithmic Visualizer", "Screen Ambient"],
                mapping.Effects.Select(effect => effect.DisplayName));
            Assert.All(mapping.Effects, effect => Assert.True(effect.IsAvailable));
            Assert.False(mapping.CanChooseLayout);
            Assert.Contains("unavailable", mapping.LayoutStatus, StringComparison.OrdinalIgnoreCase);
        }
    }
}
