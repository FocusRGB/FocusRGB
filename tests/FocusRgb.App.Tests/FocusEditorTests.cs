using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class FocusEditorTests
    {
        [Fact]
        public void Selection_DoesNotOpenEditor_AndCancelDiscardsDetachedChanges()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var item = editor.Mappings[0];
            editor.SelectedMapping = item;
            Assert.False(editor.HasEditor);
            editor.BeginEdit(item);
            editor.EditingMapping!.Eligibility = "while-running";
            Assert.Equal("while-focused", item.Eligibility);
            Assert.True(editor.IsDirty);
            editor.Cancel();
            Assert.False(editor.HasEditor);
            Assert.False(editor.IsDirty);
        }

        [Fact]
        public void CommitEditing_ClonesWithUniqueIdAndPreservesEditedFields()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            var original = editor.Mappings[0];
            editor.BeginEdit(original);
            editor.EditingMapping!.Eligibility = "while-running";
            Assert.True(editor.CommitEditing(saveAs: true));
            var copy = editor.Mappings.Last();
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal("while-running", copy.Eligibility);
            Assert.Equal("while-focused", original.Eligibility);
            Assert.Equal(original.SelectedTarget, copy.SelectedTarget);
            editor.Apply();
            Assert.False(editor.HasEditor);
            Assert.False(editor.IsDirty);
        }

        [Fact]
        public void CreateAndReorder_UseUnifiedEditorAndKeepPositionsContiguous()
        {
            var editor = MainWindowViewModel.CreatePreview().MappingEditor;
            editor.BeginCreate();
            Assert.True(editor.HasEditor);
            Assert.True(editor.CommitEditing(false));
            var created = editor.Mappings.Last();
            editor.MoveToTop(created);
            Assert.Same(created, editor.Mappings[0]);
            editor.MoveToBottom(created);
            Assert.Same(created, editor.Mappings.Last());
            Assert.Equal(Enumerable.Range(1, editor.Mappings.Count), editor.Mappings.Select(item => item.Position));
        }
    }
}
