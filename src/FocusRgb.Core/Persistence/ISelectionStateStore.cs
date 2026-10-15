using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Persistence
{
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
