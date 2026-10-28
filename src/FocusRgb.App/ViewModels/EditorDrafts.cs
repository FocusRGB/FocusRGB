namespace FocusRgb.App.ViewModels
{
    [Flags]
    internal enum EditorDrafts
    {
        None = 0,
        Settings = 1,
        FocusLibrary = 2,
        Profiles = 4,
        All = Settings | FocusLibrary | Profiles
    }
}
