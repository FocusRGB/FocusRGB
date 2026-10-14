using System.Security;
using System.Text;
using System.Text.Json;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Persistence
{
    /// <summary>
    /// Stores the three local documents atomically in a caller-selected cross-platform directory.
    /// </summary>
    public sealed class LocalStateStore : ISelectionStateStore
    {
        public const string SettingsFileName = "settings.json";
        public const string StateFileName = "state.json";
        public const string NativeSetupFileName = "native-setup.json";

        private readonly string _directory;

        public LocalStateStore(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            _directory = Path.GetFullPath(directory);
        }

        /// <summary>Loads all startup documents, seeding missing files and explicitly archiving invalid ones.</summary>
        public async Task<StartupResult> InitializeAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var recoveries = new List<RecoveryNotice>();

                var settingsResult = await LoadSettingsAsync(now, recoveries, cancellationToken);
                if (!settingsResult.IsSuccess)
                {
                    return StartupResult.Fail(settingsResult.Error!);
                }

                var settings = settingsResult.Settings!;
                var stateResult = await LoadStateAsync(settings, now, recoveries, cancellationToken);
                if (!stateResult.IsSuccess)
                {
                    return StartupResult.Fail(stateResult.Error!);
                }

                var nativeResult = await LoadNativeSetupAsync(now, recoveries, cancellationToken);
                if (!nativeResult.IsSuccess)
                {
                    return StartupResult.Fail(nativeResult.Error!);
                }

                var state = stateResult.State!;
                return StartupResult.Ok(new StartupSnapshot
                {
                    Settings = settings,
                    State = state,
                    NativeSetup = nativeResult.NativeSetup!,
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
                return StartupResult.Fail(Error("startup", exception));
            }
        }

        /// <summary>Saves validated user settings without changing selection or native fallback files.</summary>
        public async Task<SaveResult> SaveSettingsAsync(
            SettingsDocument settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var errors = SettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                return Invalid(SettingsFileName, errors);
            }

            return await SerializeAndWriteAsync(
                SettingsFileName,
                () => SettingsJson.Serialize(settings),
                cancellationToken);
        }

        /// <summary>Saves validated desired intent even when no integration can apply output.</summary>
        public async Task<SaveResult> SaveStateAsync(
            SelectionStateDocument state,
            SettingsDocument settings,
            CancellationToken cancellationToken = default)
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
                cancellationToken);
        }

        /// <summary>Saves native fallback only through an explicit native-setup operation.</summary>
        public async Task<SaveResult> SaveNativeSetupAsync(
            NativeSetupDocument nativeSetup,
            CancellationToken cancellationToken = default)
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
                cancellationToken);
        }

        private async Task<SettingsStartupResult> LoadSettingsAsync(
            DateTimeOffset now,
            List<RecoveryNotice> recoveries,
            CancellationToken cancellationToken)
        {
            var path = PathFor(SettingsFileName);
            if (!File.Exists(path))
            {
                var defaults = SettingsJson.LoadDefaults().Settings!;
                var save = await SaveSettingsAsync(defaults, cancellationToken);
                return save.IsSuccess
                    ? SettingsStartupResult.Ok(defaults)
                    : SettingsStartupResult.Fail(save.Error!);
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var loaded = SettingsJson.Load(json);
            if (loaded.IsSuccess)
            {
                return SettingsStartupResult.Ok(loaded.Settings!);
            }
            if (HasUnsupportedVersion(json))
            {
                return SettingsStartupResult.Fail(InvalidDocument(SettingsFileName, loaded.Errors));
            }

            var recovery = ArchiveInvalid(SettingsFileName, "corrupt", now, loaded.Errors);
            recoveries.Add(recovery);
            var replacement = SettingsJson.LoadDefaults().Settings!;
            var result = await SaveSettingsAsync(replacement, cancellationToken);
            return result.IsSuccess
                ? SettingsStartupResult.Ok(replacement)
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
                return await SaveFirstRunStateAsync(settings, now, SelectionReason.FirstRun(), cancellationToken);
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var loaded = StateJson.Load(json);
            if (!loaded.IsSuccess)
            {
                if (HasUnsupportedVersion(json))
                {
                    return StateStartupResult.Fail(InvalidDocument(StateFileName, loaded.Errors));
                }

                recoveries.Add(ArchiveInvalid(StateFileName, "corrupt", now, loaded.Errors));
                return await SaveFirstRunStateAsync(settings, now, SelectionReason.FirstRun(), cancellationToken);
            }

            var referenceErrors = StateJson.ValidateProfileReferences(loaded.State!, settings);
            if (referenceErrors.Count > 0)
            {
                recoveries.Add(ArchiveInvalid(StateFileName, "invalid", now, referenceErrors));
                return await SaveFirstRunStateAsync(
                    settings,
                    now,
                    SelectionReason.InvalidStateReset(),
                    cancellationToken);
            }

            return StateStartupResult.Ok(loaded.State!, SelectionReason.Restored());
        }

        private async Task<StateStartupResult> SaveFirstRunStateAsync(
            SettingsDocument settings,
            DateTimeOffset now,
            SelectionReason reason,
            CancellationToken cancellationToken)
        {
            var state = StateJson.CreateFirstRun(settings.Selection!.DefaultProfile!, now);
            var save = await SaveStateAsync(state, settings, cancellationToken);
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
                var save = await SaveNativeSetupAsync(initial, cancellationToken);
                return save.IsSuccess
                    ? NativeStartupResult.Ok(initial)
                    : NativeStartupResult.Fail(save.Error!);
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var loaded = NativeSetupJson.Load(json);
            if (loaded.IsSuccess)
            {
                return NativeStartupResult.Ok(loaded.NativeSetup!);
            }
            if (HasUnsupportedVersion(json))
            {
                return NativeStartupResult.Fail(InvalidDocument(NativeSetupFileName, loaded.Errors));
            }

            recoveries.Add(ArchiveInvalid(NativeSetupFileName, "corrupt", now, loaded.Errors));
            var replacement = NativeSetupJson.CreateDefault();
            var result = await SaveNativeSetupAsync(replacement, cancellationToken);
            return result.IsSuccess
                ? NativeStartupResult.Ok(replacement)
                : NativeStartupResult.Fail(result.Error!);
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
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, PathFor(fileName), overwrite: true);
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

        private static bool HasUnsupportedVersion(string json)
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
                    && version.ValueKind == JsonValueKind.Number
                    && version.TryGetInt64(out var value)
                    && value > 1;
            }
            catch (JsonException)
            {
                return false;
            }
        }

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
            public PersistenceError? Error { get; private init; }
            public static SettingsStartupResult Ok(SettingsDocument settings) => new() { Settings = settings };
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
    }
}
