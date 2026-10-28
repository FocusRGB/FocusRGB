using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class UnsavedChangesHeadlessTests
    {
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
