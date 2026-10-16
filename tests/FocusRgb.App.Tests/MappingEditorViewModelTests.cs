using FocusRgb.App.ViewModels;

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
    }
}
