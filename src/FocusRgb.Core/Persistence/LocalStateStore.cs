using System.Collections.Concurrent;
using System.Security;
using System.Text;
using System.Text.Json;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Persistence
{
    /// <summary>
    /// Stores the app-owned local documents atomically in a caller-selected cross-platform directory.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The process-lifetime semaphore has no unmanaged resource.")]
    public sealed class LocalStateStore : IApplicationStateStore
    {
        public const string SettingsFileName = "settings.json";
        public const string SettingsRecoveryFileName = "settings.last-known-good.json";
        public const string StateFileName = "state.json";
        public const string PreviewJournalFileName = "preview.json";
        public const string NativeSetupFileName = "native-setup.json";
        public const string GameCatalogFileName = "games.json";
        public const string GameCatalogRecoveryFileName = "games.last-known-good.json";

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly string _directory;
        private readonly SemaphoreSlim _gate;

        public LocalStateStore(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            _gate = Gates.GetOrAdd(_directory, static _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>Loads all startup documents, seeding missing files and explicitly archiving invalid ones.</summary>
        public async Task<StartupResult> InitializeAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var currentFile = SettingsFileName;
            try
            {
                Directory.CreateDirectory(_directory);
                CleanupTemporaryFiles();
                var recoveries = new List<RecoveryNotice>();
                if (File.Exists(PathFor(PreviewJournalFileName)))
                {
                    recoveries.Add(ArchiveInvalid(PreviewJournalFileName, "interrupted", now,
                        [new ConfigurationError { Path = "preview", Message = "An interrupted output preview was discarded. Only saved selection intent is restored." }]));
                }

                var settingsResult = await LoadSettingsAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
                if (!settingsResult.IsSuccess)
                {
                    return StartupResult.Fail(settingsResult.Error!);
                }

                var settings = settingsResult.Settings!;
                currentFile = StateFileName;
                var stateResult = await LoadStateAsync(settings, now, recoveries, cancellationToken).ConfigureAwait(false);
                if (!stateResult.IsSuccess)
                {
                    return StartupResult.Fail(stateResult.Error!);
                }

                currentFile = NativeSetupFileName;
                var nativeResult = await LoadNativeSetupAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
                if (!nativeResult.IsSuccess)
                {
                    return StartupResult.Fail(nativeResult.Error!);
                }

                currentFile = GameCatalogFileName;
                var gameResult = await LoadGameCatalogAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
                if (!gameResult.IsSuccess)
                {
                    return StartupResult.Fail(gameResult.Error!);
                }

                var state = stateResult.State!;
                return StartupResult.Ok(new StartupSnapshot
                {
                    Settings = settings,
                    State = state,
                    NativeSetup = nativeResult.NativeSetup!,
                    GameCatalog = gameResult.Catalog!,
                    SettingsSource = settingsResult.Source!,
                    Desired = new SelectionDecision
                    {
                        ProfileId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!,
                        Reason = stateResult.Reason!
                    },
                    Recoveries = recoveries
                });
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            {
                return StartupResult.Fail(Error(currentFile, exception));
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Journals a temporary override without replacing saved selection intent.</summary>
        public async Task<SaveResult> SavePreviewJournalAsync(string? profileId, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (profileId is not null)
                    return await SerializeAndWriteAsync(PreviewJournalFileName,
                        () => JsonSerializer.Serialize(new { profileId, restore = "saved-selection-only" }), cancellationToken)
                        .ConfigureAwait(false);
                File.Delete(PathFor(PreviewJournalFileName));
                return SaveResult.Ok();
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            {
                return SaveResult.Fail(Error(PreviewJournalFileName, exception));
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Saves the cached and manually entered game catalog.</summary>
        public async Task<SaveResult> SaveGameCatalogAsync(
            GameCatalogDocument catalog,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SaveGameCatalogCoreAsync(catalog, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<SaveResult> SaveGameCatalogCoreAsync(
            GameCatalogDocument catalog,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            var errors = GameCatalogJson.Validate(catalog);
            if (errors.Count > 0) return Invalid(GameCatalogFileName, errors);
            var saved = await SerializeAndWriteAsync(
                GameCatalogFileName,
                () => GameCatalogJson.Serialize(catalog),
                cancellationToken).ConfigureAwait(false);
            if (!saved.IsSuccess) return saved;
            var recovery = await SerializeAndWriteAsync(GameCatalogRecoveryFileName,
                () => GameCatalogJson.Serialize(catalog), cancellationToken).ConfigureAwait(false);
            return recovery.IsSuccess ? SaveResult.Ok() : SaveResult.Ok(recovery.Error);
        }

        /// <summary>Saves validated user settings without changing selection or native fallback files.</summary>
        public async Task<SaveResult> SaveSettingsAsync(
            SettingsDocument settings,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SaveSettingsCoreAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<SaveResult> SaveSettingsCoreAsync(
            SettingsDocument settings,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var errors = SettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                return Invalid(SettingsFileName, errors);
            }

            var current = await SerializeAndWriteAsync(
                SettingsFileName,
                () => SettingsJson.Serialize(settings),
                cancellationToken).ConfigureAwait(false);
            if (!current.IsSuccess) return current;
            var recovery = await SerializeAndWriteAsync(
                SettingsRecoveryFileName,
                () => SettingsJson.Serialize(settings),
                cancellationToken).ConfigureAwait(false);
            return recovery.IsSuccess ? SaveResult.Ok() : SaveResult.Ok(recovery.Error);
        }

        /// <summary>Saves validated desired intent even when no integration can apply output.</summary>
        public async Task<SaveResult> SaveStateAsync(
            SelectionStateDocument state,
            SettingsDocument settings,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SaveStateCoreAsync(state, settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<SaveResult> SaveStateCoreAsync(
            SelectionStateDocument state,
            SettingsDocument settings,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(settings);
            var errors = StateJson.Validate(state)
                .Concat(StateJson.ValidateProfileReferences(state, settings))
                .ToList();
            if (errors.Count > 0)
            {
                return Invalid(StateFileName, errors);
            }

            return await SerializeAndWriteAsync(
                StateFileName,
                () => StateJson.Serialize(state),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Saves native fallback only through an explicit native-setup operation.</summary>
        public async Task<SaveResult> SaveNativeSetupAsync(
            NativeSetupDocument nativeSetup,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SaveNativeSetupCoreAsync(nativeSetup, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<SaveResult> SaveNativeSetupCoreAsync(
            NativeSetupDocument nativeSetup,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(nativeSetup);
            var errors = NativeSetupJson.Validate(nativeSetup);
            if (errors.Count > 0)
            {
                return Invalid(NativeSetupFileName, errors);
            }

            return await SerializeAndWriteAsync(
                NativeSetupFileName,
                () => NativeSetupJson.Serialize(nativeSetup),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<SettingsStartupResult> LoadSettingsAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(SettingsFileName);
            if (!File.Exists(path))
            {
                var missingFileRecovery = await LoadRecoverySettingsAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
                if (missingFileRecovery is not null) return missingFileRecovery;
                var defaults = SettingsJson.LoadDefaults().Settings!;
                var save = await SaveSettingsCoreAsync(defaults, cancellationToken).ConfigureAwait(false);
                return save.IsSuccess
                    ? SettingsStartupResult.Ok(defaults, "defaults")
                    : SettingsStartupResult.Fail(save.Error!);
            }

            var json = await ReadAllTextAsync(SettingsFileName, cancellationToken).ConfigureAwait(false);
            var loaded = SettingsJson.Load(json);
            if (loaded.IsSuccess)
            {
                var loadedVersion = ReadSchemaVersion(json);
                if (loadedVersion < SettingsJson.CurrentSchemaVersion)
                {
                    BackupFile(SettingsFileName, $"schema-{loadedVersion}", now);
                    var save = await SaveSettingsCoreAsync(loaded.Settings!, cancellationToken).ConfigureAwait(false);
                    if (!save.IsSuccess) return SettingsStartupResult.Fail(save.Error!);
                }
                else
                {
                    var recoverySave = await SerializeAndWriteAsync(
                        SettingsRecoveryFileName,
                        () => SettingsJson.Serialize(loaded.Settings!),
                        cancellationToken).ConfigureAwait(false);
                    if (!recoverySave.IsSuccess)
                    {
                        recoveries.Add(new RecoveryNotice
                        {
                            FileName = recoverySave.Error!.FileName,
                            Message = $"Could not refresh the last-known-good copy: {recoverySave.Error.Message}"
                        });
                    }
                }
                return SettingsStartupResult.Ok(loaded.Settings!, "current");
            }
            if (HasUnsupportedVersion(json, SettingsJson.CurrentSchemaVersion))
            {
                return SettingsStartupResult.Fail(InvalidDocument(SettingsFileName, loaded.Errors));
            }

            var recovery = ArchiveInvalid(SettingsFileName, "corrupt", now, loaded.Errors);
            recoveries.Add(recovery);
            var recovered = await LoadRecoverySettingsAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
            if (recovered is not null) return recovered;
            var replacement = SettingsJson.LoadDefaults().Settings!;
            var result = await SaveSettingsCoreAsync(replacement, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? SettingsStartupResult.Ok(replacement, "defaults")
                : SettingsStartupResult.Fail(result.Error!);
        }

        private async Task<StateStartupResult> LoadStateAsync(
            SettingsDocument settings,
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(StateFileName);
            if (!File.Exists(path))
            {
                return await SaveFirstRunStateAsync(settings, now, SelectionReason.FirstRun(), cancellationToken).ConfigureAwait(false);
            }

            var json = await ReadAllTextAsync(StateFileName, cancellationToken).ConfigureAwait(false);
            var loaded = StateJson.Load(json);
            if (!loaded.IsSuccess)
            {
                if (HasUnsupportedVersion(json, 1))
                {
                    return StateStartupResult.Fail(InvalidDocument(StateFileName, loaded.Errors));
                }

                recoveries.Add(ArchiveInvalid(StateFileName, "corrupt", now, loaded.Errors));
                return await SaveFirstRunStateAsync(settings, now, SelectionReason.InvalidStateReset(), cancellationToken).ConfigureAwait(false);
            }

            var referenceErrors = StateJson.ValidateProfileReferences(loaded.State!, settings);
            if (referenceErrors.Count > 0)
            {
                recoveries.Add(ArchiveInvalid(StateFileName, "invalid", now, referenceErrors));
                return await SaveFirstRunStateAsync(
                    settings,
                    now,
                    SelectionReason.InvalidStateReset(),
                    cancellationToken).ConfigureAwait(false);
            }

            return StateStartupResult.Ok(loaded.State!, SelectionReason.Restored());
        }

        private async Task<StateStartupResult> SaveFirstRunStateAsync(
            SettingsDocument settings,
            DateTimeOffset now,
            SelectionReason reason,
            CancellationToken cancellationToken)
        {
            var state = StateJson.CreateFirstRun(settings.Selection!.DefaultProfile!, now.ToUniversalTime());
            var save = await SaveStateCoreAsync(state, settings, cancellationToken).ConfigureAwait(false);
            return save.IsSuccess
                ? StateStartupResult.Ok(state, reason)
                : StateStartupResult.Fail(save.Error!);
        }

        private async Task<NativeStartupResult> LoadNativeSetupAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(NativeSetupFileName);
            if (!File.Exists(path))
            {
                var initial = NativeSetupJson.CreateDefault();
                var save = await SaveNativeSetupCoreAsync(initial, cancellationToken).ConfigureAwait(false);
                return save.IsSuccess
                    ? NativeStartupResult.Ok(initial)
                    : NativeStartupResult.Fail(save.Error!);
            }

            var json = await ReadAllTextAsync(NativeSetupFileName, cancellationToken).ConfigureAwait(false);
            var loaded = NativeSetupJson.Load(json);
            if (loaded.IsSuccess)
            {
                return NativeStartupResult.Ok(loaded.NativeSetup!);
            }
            if (HasUnsupportedVersion(json, 1))
            {
                return NativeStartupResult.Fail(InvalidDocument(NativeSetupFileName, loaded.Errors));
            }

            recoveries.Add(ArchiveInvalid(NativeSetupFileName, "corrupt", now, loaded.Errors));
            var replacement = NativeSetupJson.CreateDefault();
            var result = await SaveNativeSetupCoreAsync(replacement, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? NativeStartupResult.Ok(replacement)
                : NativeStartupResult.Fail(result.Error!);
        }

        private async Task<GameCatalogStartupResult> LoadGameCatalogAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(GameCatalogFileName);
            if (!File.Exists(path))
            {
                var recovered = await RecoverGameCatalogAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
                if (recovered is not null) return recovered;
                var initial = GameCatalogJson.CreateDefault();
                var save = await SaveGameCatalogCoreAsync(initial, cancellationToken).ConfigureAwait(false);
                return save.IsSuccess
                    ? GameCatalogStartupResult.Ok(initial)
                    : GameCatalogStartupResult.Fail(save.Error!);
            }

            var json = await ReadAllTextAsync(GameCatalogFileName, cancellationToken).ConfigureAwait(false);
            var loaded = GameCatalogJson.Load(json);
            if (loaded.IsSuccess)
            {
                var refreshed = await SerializeAndWriteAsync(GameCatalogRecoveryFileName,
                    () => GameCatalogJson.Serialize(loaded.Catalog!), cancellationToken).ConfigureAwait(false);
                if (!refreshed.IsSuccess)
                {
                    recoveries.Add(new RecoveryNotice
                    {
                        FileName = GameCatalogRecoveryFileName,
                        Message = $"Could not refresh the game catalog recovery copy: {refreshed.Error!.Message}"
                    });
                }
                return GameCatalogStartupResult.Ok(loaded.Catalog!);
            }
            if (HasUnsupportedVersion(json, GameCatalogJson.CurrentSchemaVersion))
                return GameCatalogStartupResult.Fail(InvalidDocument(GameCatalogFileName, loaded.Errors));

            recoveries.Add(ArchiveInvalid(GameCatalogFileName, "corrupt", now, loaded.Errors));
            var recovery = await RecoverGameCatalogAsync(now, recoveries, cancellationToken).ConfigureAwait(false);
            if (recovery is not null) return recovery;
            var replacement = GameCatalogJson.CreateDefault();
            var result = await SaveGameCatalogCoreAsync(replacement, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? GameCatalogStartupResult.Ok(replacement)
                : GameCatalogStartupResult.Fail(result.Error!);
        }

        private async Task<GameCatalogStartupResult?> RecoverGameCatalogAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(PathFor(GameCatalogRecoveryFileName))) return null;
            var json = await ReadAllTextAsync(GameCatalogRecoveryFileName, cancellationToken).ConfigureAwait(false);
            var loaded = GameCatalogJson.Load(json);
            if (!loaded.IsSuccess)
            {
                recoveries.Add(ArchiveInvalid(GameCatalogRecoveryFileName, "corrupt", now, loaded.Errors));
                return null;
            }
            var restored = await SerializeAndWriteAsync(GameCatalogFileName,
                () => GameCatalogJson.Serialize(loaded.Catalog!), cancellationToken).ConfigureAwait(false);
            if (!restored.IsSuccess) return GameCatalogStartupResult.Fail(restored.Error!);
            recoveries.Add(new RecoveryNotice
            {
                FileName = GameCatalogFileName,
                Message = "Restored game catalog from its last-known-good copy."
            });
            return GameCatalogStartupResult.Ok(loaded.Catalog!);
        }

        private async Task<SaveResult> SerializeAndWriteAsync(
            string fileName,
            Func<string> serialize,
            CancellationToken cancellationToken)
        {
            string? tempPath = null;
            try
            {
                Directory.CreateDirectory(_directory);
                var content = serialize();
                tempPath = Path.Combine(_directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
                await using (var stream = new FileStream(tempPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                }))
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                await RetryPersistenceAsync(
                    () => File.Move(tempPath, PathFor(fileName), overwrite: true),
                    cancellationToken).ConfigureAwait(false);
                return SaveResult.Ok();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsPersistenceException(exception))
            {
                return SaveResult.Fail(Error(fileName, exception));
            }
            finally
            {
                if (tempPath is not null)
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception exception) when (IsPersistenceException(exception))
                    {
                        // The target was never replaced; a later cleanup may remove this unique temp file.
                    }
                }
            }
        }

        private RecoveryNotice ArchiveInvalid(
            string fileName,
            string kind,
            DateTimeOffset now,
            IReadOnlyList<ConfigurationError> errors)
        {
            var source = PathFor(fileName);
            var stamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
            var archive = Path.Combine(_directory, $"{fileName}.{kind}-{stamp}");
            for (var suffix = 1; File.Exists(archive); suffix++)
            {
                archive = Path.Combine(_directory, $"{fileName}.{kind}-{stamp}-{suffix}");
            }
            File.Move(source, archive);
            return new RecoveryNotice
            {
                FileName = fileName,
                ArchivedPath = archive,
                Message = FormatErrors(errors)
            };
        }

        private void BackupFile(string fileName, string kind, DateTimeOffset now)
        {
            var stamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
            var backup = Path.Combine(_directory, $"{fileName}.{kind}-{stamp}.backup");
            for (var suffix = 1; File.Exists(backup); suffix++)
                backup = Path.Combine(_directory, $"{fileName}.{kind}-{stamp}-{suffix}.backup");
            File.Copy(PathFor(fileName), backup);
        }

        private async Task<SettingsStartupResult?> LoadRecoverySettingsAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(SettingsRecoveryFileName);
            if (!File.Exists(path)) return null;
            var json = await ReadAllTextAsync(SettingsRecoveryFileName, cancellationToken).ConfigureAwait(false);
            var loaded = SettingsJson.Load(json);
            if (loaded.IsSuccess)
            {
                var save = await SaveSettingsCoreAsync(loaded.Settings!, cancellationToken).ConfigureAwait(false);
                return save.IsSuccess
                    ? SettingsStartupResult.Ok(loaded.Settings!, "last-known-good")
                    : SettingsStartupResult.Fail(save.Error!);
            }

            if (HasUnsupportedVersion(json, SettingsJson.CurrentSchemaVersion))
                return SettingsStartupResult.Fail(InvalidDocument(SettingsRecoveryFileName, loaded.Errors));

            recoveries.Add(ArchiveInvalid(SettingsRecoveryFileName, "corrupt", now, loaded.Errors));
            return null;
        }

        private async Task<string> ReadAllTextAsync(string fileName, CancellationToken cancellationToken) =>
            await RetryPersistenceAsync(
                () => File.ReadAllTextAsync(PathFor(fileName), cancellationToken),
                cancellationToken).ConfigureAwait(false);

        private static async Task<T> RetryPersistenceAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception exception) when (attempt < 4
                    && exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 << attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task RetryPersistenceAsync(Action action, CancellationToken cancellationToken) =>
            await RetryPersistenceAsync(() =>
            {
                action();
                return Task.FromResult(true);
            }, cancellationToken).ConfigureAwait(false);

        private void CleanupTemporaryFiles()
        {
            foreach (var fileName in new[]
            {
                SettingsFileName, SettingsRecoveryFileName, StateFileName, NativeSetupFileName, GameCatalogFileName,
                GameCatalogRecoveryFileName
            })
            {
                foreach (var path in Directory.EnumerateFiles(_directory, $".{fileName}.*.tmp"))
                {
                    try
                    {
                        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(5)) continue;
                        File.Delete(path);
                    }
                    catch (Exception exception) when (IsPersistenceException(exception))
                    {
                    }
                }
            }
        }

        private static int? ReadSchemaVersion(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("schemaVersion", out var version)
                    && version.TryGetInt32(out var value)
                    ? value
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool HasUnsupportedVersion(string json, int supportedVersion) =>
            ReadSchemaVersion(json) > supportedVersion;

        private static SaveResult Invalid(string fileName, IReadOnlyList<ConfigurationError> errors) =>
            SaveResult.Fail(InvalidDocument(fileName, errors));

        private static PersistenceError InvalidDocument(
            string fileName,
            IReadOnlyList<ConfigurationError> errors) => new()
            {
                FileName = fileName,
                Message = FormatErrors(errors)
            };

        private static string FormatErrors(IReadOnlyList<ConfigurationError> errors) =>
            string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));

        private static PersistenceError Error(string fileName, Exception exception) => new()
        {
            FileName = fileName,
            Message = exception.Message
        };

        private static bool IsPersistenceException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or SecurityException
                or NotSupportedException or InvalidOperationException;

        private string PathFor(string fileName) => Path.Combine(_directory, fileName);

        private sealed class SettingsStartupResult
        {
            public bool IsSuccess => Settings is not null;
            public SettingsDocument? Settings { get; private init; }
            public string? Source { get; private init; }
            public PersistenceError? Error { get; private init; }
            public static SettingsStartupResult Ok(SettingsDocument settings, string source) => new()
            {
                Settings = settings,
                Source = source
            };
            public static SettingsStartupResult Fail(PersistenceError error) => new() { Error = error };
        }

        private sealed class StateStartupResult
        {
            public bool IsSuccess => State is not null;
            public SelectionStateDocument? State { get; private init; }
            public SelectionReason? Reason { get; private init; }
            public PersistenceError? Error { get; private init; }
            public static StateStartupResult Ok(SelectionStateDocument state, SelectionReason reason) => new()
            {
                State = state,
                Reason = reason
            };
            public static StateStartupResult Fail(PersistenceError error) => new() { Error = error };
        }

        private sealed class NativeStartupResult
        {
            public bool IsSuccess => NativeSetup is not null;
            public NativeSetupDocument? NativeSetup { get; private init; }
            public PersistenceError? Error { get; private init; }
            public static NativeStartupResult Ok(NativeSetupDocument nativeSetup) => new()
            {
                NativeSetup = nativeSetup
            };
            public static NativeStartupResult Fail(PersistenceError error) => new() { Error = error };
        }

        private sealed class GameCatalogStartupResult
        {
            public bool IsSuccess => Catalog is not null;
            public GameCatalogDocument? Catalog { get; private init; }
            public PersistenceError? Error { get; private init; }
            public static GameCatalogStartupResult Ok(GameCatalogDocument catalog) => new() { Catalog = catalog };
            public static GameCatalogStartupResult Fail(PersistenceError error) => new() { Error = error };
        }
    }
}
