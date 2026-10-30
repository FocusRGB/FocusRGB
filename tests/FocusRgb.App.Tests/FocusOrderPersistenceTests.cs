using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class FocusOrderPersistenceTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(0)]
        public async Task CompletedReorder_PersistsAcrossRestartAndLeavesNoOrderDraft(int offset)
        {
            using var directory = new TempDirectory(nameof(FocusOrderPersistenceTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var model = MainWindowViewModel.CreatePersistent(loaded.Snapshot!, store);
            var editor = model.MappingEditor;
            var middle = editor.Mappings[1];
            if (offset == 0)
            {
                Assert.True(editor.BeginReorder(middle));
                editor.PreviewReorder(0);
                Assert.True(editor.CommitReorder());
            }
            else editor.Move(middle, offset);
            var expected = editor.Mappings.Select(item => item.Id).ToArray();
            await model.SaveMappingOrderAsync(TestContext.Current.CancellationToken);
            Assert.False(editor.IsDirty);
            var restarted = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.Equal(expected, restarted.Snapshot!.Settings.Rules!.Select(rule => rule.Id));
        }

        [Fact]
        public async Task Reorder_KeepsAnUnsavedFocusEditorDraftAndRollsBackOrderOnDiskFailure()
        {
            using var directory = new TempDirectory(nameof(FocusOrderPersistenceTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var model = MainWindowViewModel.CreatePersistent(loaded.Snapshot!, store);
            var editor = model.MappingEditor;
            var original = editor.Mappings.Select(item => item.Id).ToArray();
            editor.BeginEdit(editor.Mappings[0]);
            editor.EditingMapping!.Eligibility = "while-running";
            var draft = editor.EditingMapping;
            using (var locked = new FileStream(Path.Combine(directory.Path, LocalStateStore.SettingsFileName), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                editor.Move(editor.Mappings[1], -1);
                await model.SaveMappingOrderAsync(TestContext.Current.CancellationToken);
                Assert.Equal(original, editor.Mappings.Select(item => item.Id));
                Assert.Same(draft, editor.EditingMapping);
                Assert.True(editor.IsDirty);
            }
            editor.Move(editor.Mappings[1], -1);
            await model.SaveMappingOrderAsync(TestContext.Current.CancellationToken);
            Assert.Same(draft, editor.EditingMapping);
            Assert.True(editor.IsDirty);
            var restarted = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.Equal(loaded.Snapshot!.Settings.Rules![0].Eligibility,
                restarted.Snapshot!.Settings.Rules!.Single(rule => rule.Id == original[0]).Eligibility);
        }
    }
}
