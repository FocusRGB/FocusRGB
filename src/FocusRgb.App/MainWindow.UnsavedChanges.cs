using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App
{
    internal enum UnsavedChangesChoice { Cancel, Discard, Save }

    public sealed partial class MainWindow
    {
        private bool _unsavedPromptPending;
        private bool _restoringTab;
        private int _activeTabIndex;
        internal Func<Task<UnsavedChangesChoice>>? UnsavedChangesPrompt { get; set; }

        public Task<bool> ConfirmUnsavedChangesAsync() => ConfirmUnsavedChangesAsync(EditorDrafts.All);

        private async Task<bool> ConfirmUnsavedChangesAsync(EditorDrafts editors)
        {
            if (_unsavedPromptPending) return false;
            if (!ViewModel.HasUnsavedChanges(editors)) return true;
            _unsavedPromptPending = true;
            try
            {
                var choice = await (UnsavedChangesPrompt?.Invoke() ?? ShowUnsavedChangesDialogAsync());
                switch (choice)
                {
                    case UnsavedChangesChoice.Save:
                        var settingsChanged = editors.HasFlag(EditorDrafts.Settings) && ViewModel.IsApplicationSettingsDirty;
                        var saved = await ViewModel.SaveUnsavedChangesAsync(editors);
                        if (saved && settingsChanged) ApplySavedApplicationPreferences();
                        return saved;
                    case UnsavedChangesChoice.Discard:
                        if (editors.HasFlag(EditorDrafts.Profiles) && Application.Current is App app)
                            await app.CancelProfilePreviewAsync();
                        ViewModel.DiscardUnsavedChanges(editors);
                        if (editors.HasFlag(EditorDrafts.Settings))
                            (Application.Current as App)?.ApplyTheme(ViewModel.ApplicationTheme);
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception exception)
            {
                ViewModel.ReportUnexpectedError($"Could not save draft changes: {exception.Message}");
                return false;
            }
            finally
            {
                _unsavedPromptPending = false;
            }
        }

        private async Task<UnsavedChangesChoice> ShowUnsavedChangesDialogAsync()
        {
            if (!IsVisible) ShowFromTray();
            var dialog = new Window
            {
                Name = "UnsavedChanges", Title = "Unsaved changes", Width = 440, Height = 190, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var choice in new[] { UnsavedChangesChoice.Save, UnsavedChangesChoice.Discard, UnsavedChangesChoice.Cancel })
            {
                var button = new Button
                {
                    Content = choice.ToString(),
                    IsDefault = choice == UnsavedChangesChoice.Save,
                    IsCancel = choice == UnsavedChangesChoice.Cancel
                };
                if (choice == UnsavedChangesChoice.Save)
                {
                    button.Classes.Add("accent");
                    dialog.Opened += (_, _) => button.Focus(NavigationMethod.Tab);
                }
                button.Click += (_, _) => dialog.Close(choice);
                buttons.Children.Add(button);
            }
            dialog.Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 20,
                Children =
                {
                    new TextBlock { Text = "You have unsaved changes. Do you want to save them before leaving?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    buttons
                }
            };
            return await dialog.ShowDialog<UnsavedChangesChoice>(this);
        }

        private static EditorDrafts DraftsForTab(object? tab) => (tab as TabItem)?.Header switch
        {
            "Settings" => EditorDrafts.Settings,
            "Focus" => EditorDrafts.FocusLibrary,
            "Profiles" => EditorDrafts.Profiles,
            _ => EditorDrafts.None
        };

        private void ApplySavedApplicationPreferences()
        {
            (Application.Current as App)?.ApplyTheme(ViewModel.ApplicationTheme);
            AppLog.Configure(_dataDirectory, ViewModel.SavedVerboseLogging);
            AppLog.Write("Application preferences saved.");
            _pluginsLoaded = false;
        }
    }
}
