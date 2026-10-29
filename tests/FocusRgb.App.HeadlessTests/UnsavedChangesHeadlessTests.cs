using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class UnsavedChangesHeadlessTests
    {
        public static IEnumerable<object[]> DialogKeyboardCases()
        {
            foreach (var editor in new[] { "Settings", "Focus", "Profiles" })
            foreach (var close in new[] { false, true })
            foreach (var action in new[] { "Enter", "Escape", "Close" })
                yield return new object[] { editor, close, action };
        }

        [AvaloniaTheory]
        [MemberData(nameof(DialogKeyboardCases))]
        public async Task RealDialog_SavesWithEnterAndCancelsWithEscapeOrClose(string editor, bool close, string action)
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                var source = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, editor));
                var target = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Games"));
                tabs.SelectedItem = source;
                MakeDirty(model, editor);
                if (close) window.Close();
                else tabs.SelectedItem = target;

                await RespondToRealDialogAsync(window, action);

                var cancelled = action != "Enter";
                Assert.Equal(cancelled, model.HasUnsavedChanges(EditorDrafts.All));
                Assert.Equal(!close || cancelled, window.IsVisible);
                Assert.Same(close || cancelled ? source : target, tabs.SelectedItem);
                AssertDraftValues(model, editor, cancelled ? 0 : 2);
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RealDialog_EnterKeepsDraftAndBlocksActionWhenSaveFails(bool close)
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                var source = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Profiles"));
                tabs.SelectedItem = source;
                model.ProfileEffect = "";
                Assert.True(model.IsProfileDirty);
                if (close) window.Close();
                else tabs.SelectedIndex = 0;

                await RespondToRealDialogAsync(window, "Enter");

                Assert.True(window.IsVisible);
                Assert.Same(source, tabs.SelectedItem);
                Assert.True(model.IsProfileDirty);
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
                window.AllowClose();
                window.Close();
            }
        }

        private static async Task RespondToRealDialogAsync(MainWindow window, string action)
        {
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("UnsavedChanges", dialog.Name);
            var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save"));
            Assert.True(save.IsDefault);
            Assert.True(save.IsFocused);
            Assert.Contains("accent", save.Classes);
            if (action == "Close") dialog.Close();
            else
            {
                var key = action == "Enter" ? Key.Enter : Key.Escape;
                var physicalKey = action == "Enter" ? PhysicalKey.Enter : PhysicalKey.Escape;
                dialog.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            }
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
        }

        [AvaloniaTheory]
        [InlineData("Settings", 0)]
        [InlineData("Settings", 1)]
        [InlineData("Settings", 2)]
        [InlineData("Focus", 0)]
        [InlineData("Focus", 1)]
        [InlineData("Focus", 2)]
        [InlineData("Profiles", 0)]
        [InlineData("Profiles", 1)]
        [InlineData("Profiles", 2)]
        public void TabSwitch_HonorsCancelDiscardAndSave(string editor, int answer)
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                var source = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, editor));
                tabs.SelectedItem = source;
                MakeDirty(model, editor);
                var prompts = 0;
                window.UnsavedChangesPrompt = () => { prompts++; return Task.FromResult((UnsavedChangesChoice)answer); };
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Games"));
                Assert.Equal(1, prompts);
                Assert.Equal(answer == 0, ReferenceEquals(tabs.SelectedItem, source));
                Assert.Equal(answer == 0, model.HasUnsavedChanges(EditorDrafts.All));
                AssertDraftValues(model, editor, answer);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData("Settings", 0)]
        [InlineData("Settings", 1)]
        [InlineData("Settings", 2)]
        [InlineData("Focus", 0)]
        [InlineData("Focus", 1)]
        [InlineData("Focus", 2)]
        [InlineData("Profiles", 0)]
        [InlineData("Profiles", 1)]
        [InlineData("Profiles", 2)]
        public void CloseToTray_HonorsCancelDiscardAndSave(string editor, int answer)
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                MakeDirty(model, editor);
                window.UnsavedChangesPrompt = () => Task.FromResult((UnsavedChangesChoice)answer);
                window.Close();
                Assert.Equal(answer == 0, window.IsVisible);
                Assert.Equal(answer == 0, model.HasUnsavedChanges(EditorDrafts.All));
                AssertDraftValues(model, editor, answer);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Close_UsesNewClosePreferenceOnlyAfterSuccessfulSave()
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                model.CloseButtonBehavior = "Exit application";
                var exits = 0;
                window.ExitRequested += (_, _) => exits++;
                window.UnsavedChangesPrompt = () => Task.FromResult(UnsavedChangesChoice.Save);
                window.Close();
                Assert.Equal(1, exits);
                Assert.False(model.IsApplicationSettingsDirty);
                Assert.False(model.ShouldMinimizeOnClose);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task QuitGuard_RejectsConcurrentPromptsAndCancelPreservesAllDrafts()
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            var decision = new TaskCompletionSource<UnsavedChangesChoice>();
            window.Show();
            try
            {
                MakeDirty(model, "Settings");
                MakeDirty(model, "Focus");
                MakeDirty(model, "Profiles");
                var prompts = 0;
                window.UnsavedChangesPrompt = () => { prompts++; return decision.Task; };
                var first = window.ConfirmUnsavedChangesAsync();
                Assert.False(await window.ConfirmUnsavedChangesAsync());
                decision.SetResult(UnsavedChangesChoice.Cancel);
                Assert.False(await first);
                Assert.Equal(1, prompts);
                Assert.True(model.IsApplicationSettingsDirty);
                Assert.True(model.MappingEditor.IsDirty);
                Assert.True(model.IsProfileDirty);
                window.UnsavedChangesPrompt = () => Task.FromResult(UnsavedChangesChoice.Save);
                Assert.True(await window.ConfirmUnsavedChangesAsync());
                Assert.False(model.HasUnsavedChanges(EditorDrafts.All));
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void SaveFailure_BlocksTabSwitchOrCloseAndKeepsDraft(bool close)
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                var source = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Profiles"));
                tabs.SelectedItem = source;
                model.ProfileEffect = "";
                Assert.True(model.IsProfileDirty);
                window.UnsavedChangesPrompt = () => Task.FromResult(UnsavedChangesChoice.Save);
                if (close) window.Close();
                else tabs.SelectedIndex = 0;
                Assert.True(window.IsVisible);
                Assert.Same(source, tabs.SelectedItem);
                Assert.True(model.IsProfileDirty);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        private static void MakeDirty(MainWindowViewModel model, string editor)
        {
            switch (editor)
            {
                case "Settings": model.VerboseLogging = !model.VerboseLogging; break;
                case "Focus": model.MappingEditor.BeginCreate(); break;
                case "Profiles":
                    model.EditingProfile = model.Profiles.First(profile => profile.Id != "default");
                    model.ProfileName = "Changed profile";
                    break;
            }
            Assert.True(model.HasUnsavedChanges(EditorDrafts.All));
        }

        private static void AssertDraftValues(MainWindowViewModel model, string editor, int answer)
        {
            if (editor == "Settings" && answer != 0) Assert.Equal(answer == 2, model.VerboseLogging);
            if (editor == "Profiles" && answer != 0)
                Assert.Equal(answer == 2, model.ProfileName == "Changed profile");
            if (editor == "Focus" && answer != 0) Assert.False(model.MappingEditor.HasEditor);
        }
    }
}
