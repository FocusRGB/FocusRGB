using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Persistence
{
    /// <summary>Persists the application documents owned by the shared command service.</summary>
    public interface IApplicationStateStore : ISelectionStateStore
    {
        /// <summary>Atomically saves validated application settings.</summary>
        Task<SaveResult> SaveSettingsAsync(
            SettingsDocument settings,
            CancellationToken cancellationToken = default);

        /// <summary>Atomically saves the validated app-owned game catalog.</summary>
        Task<SaveResult> SaveGameCatalogAsync(
            GameCatalogDocument catalog,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Persists validated selection intent for shared UI and future MCP commands.</summary>
    public interface ISelectionStateStore
    {
        /// <summary>Atomically saves selection intent after validating its profile references.</summary>
        Task<SaveResult> SaveStateAsync(
            SelectionStateDocument state,
            SettingsDocument settings,
            CancellationToken cancellationToken = default);
    }
}
