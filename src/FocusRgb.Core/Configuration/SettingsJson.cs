using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Reads and validates settings in memory before LocalStateStore persists them.</summary>
    public static class SettingsJson
    {
        public const int CurrentSchemaVersion = 6;

        public static SettingsLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<SettingsDocument>(json, ConfigurationJson.Options);
                if (document?.SchemaVersion is 1 or 2 or 3 or 4 or 5)
                {
                    var groups = document.SchemaVersion < 4
                        ? document.ApplicationGroups?.Select(group => new ApplicationGroupSettings
                        {
                            Id = group.Id,
                            DisplayName = group.DisplayName,
                            Executables = NormalizeExecutables(group.Executables),
                            AdditionalProperties = group.AdditionalProperties
                        }).ToList() ?? document.Rules?.Where(rule => rule.Match?.Executables is { Count: > 0 }).Select(rule =>
                            new ApplicationGroupSettings
                            {
                                Id = rule.Id,
                                DisplayName = document.Profiles?.FirstOrDefault(profile => profile.Id == rule.Profile)?.DisplayName
                                    ?? rule.Id,
                                Executables = NormalizeExecutables(rule.Match!.Executables),
                                AdditionalProperties = rule.Match.AdditionalProperties
                            }).ToList()
                        : document.ApplicationGroups;
                    document = new SettingsDocument
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Application = document.SchemaVersion == 1
                            ? CreateDefaultApplicationSettings()
                            : MigrateApplication(document.Application),
                        Plugins = document.SchemaVersion is 1 or 2
                            ? CreateDefaultPluginSettings()
                            : document.Plugins,
                        Selection = MigrateSelection(document.Selection),
                        Profiles = document.SchemaVersion < 5 ? MigrateProfiles(document.Profiles) : document.Profiles,
                        ApplicationGroups = groups,
                        Rules = document.SchemaVersion < 4 ? MigrateRules(document.Rules) : document.Rules,
                        AdditionalProperties = document.AdditionalProperties
                    };
                }
                var errors = SettingsValidator.Validate(document);
                return errors.Count == 0 ? SettingsLoadResult.Ok(document!) : SettingsLoadResult.Fail(errors);
            }
            catch (JsonException exception)
            {
                return SettingsLoadResult.Fail([new ConfigurationError
                {
                    Path = exception.Path ?? "$",
                    Message = exception.Message
                }]);
            }
        }

        public static string Serialize(SettingsDocument settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return JsonSerializer.Serialize(settings, ConfigurationJson.Options);
        }

        public static SettingsLoadResult LoadDefaults()
        {
            using var stream = typeof(SettingsJson).Assembly.GetManifestResourceStream(
                "FocusRgb.Core.Configuration.default-settings.json")
                ?? throw new InvalidOperationException("Default settings resource is missing.");
            using var reader = new StreamReader(stream);
            return Load(reader.ReadToEnd());
        }

        public static NativeSetupDocument CreateDefaultNativeSetup() => NativeSetupJson.CreateDefault();

        public static ApplicationSettings CreateDefaultApplicationSettings() => new()
        {
            CloseButtonBehavior = "minimize-to-tray",
            StartMinimized = false,
            Theme = "system",
            VerboseLogging = false,
            ShowProfileChangePopup = true,
            SwitchingSpeed = "normal"
        };

        public static PluginSettings CreateDefaultPluginSettings() => new() { Enabled = [] };

        private static ApplicationSettings? MigrateApplication(ApplicationSettings? application) => application is null ? null : new()
        {
            CloseButtonBehavior = application.CloseButtonBehavior,
            StartMinimized = application.StartMinimized,
            Theme = application.Theme,
            VerboseLogging = application.VerboseLogging,
            ShowProfileChangePopup = application.ShowProfileChangePopup,
            IncludeBetaPlugins = application.IncludeBetaPlugins,
            SwitchingSpeed = "normal",
            AdditionalProperties = application.AdditionalProperties
        };

        private static List<ProfileSettings>? MigrateProfiles(List<ProfileSettings>? profiles) => profiles?
            .Select(profile => profile.Id == "movie"
                ? new ProfileSettings
                {
                    Id = profile.Id,
                    DisplayName = profile.DisplayName,
                    Bindings = profile.Bindings?.Select(MigrateMovieBinding).ToList(),
                    Fallback = profile.Fallback,
                    AdditionalProperties = profile.AdditionalProperties
                }
                : profile)
            .ToList();

        private static BindingSettings MigrateMovieBinding(BindingSettings binding)
        {
            if (binding.Id != "main"
                || binding.Plugin != "signalrgb"
                || binding.Effect is not { ResourceId: null, DisplayName: "Screen Ambient" })
                return binding;

            return new BindingSettings
            {
                Id = binding.Id,
                Plugin = binding.Plugin,
                RenderingAuthority = binding.RenderingAuthority,
                DeviceScope = binding.DeviceScope,
                Effect = new ResourceReference
                {
                    ResourceId = null,
                    DisplayName = "Screen Ambience",
                    AdditionalProperties = binding.Effect.AdditionalProperties
                },
                Layout = binding.Layout,
                AdditionalProperties = binding.AdditionalProperties
            };
        }

        private static List<string>? NormalizeExecutables(List<string>? executables) => executables?
            .Select(executable => executable.Trim())
            .Select(executable => Path.GetFileNameWithoutExtension(executable) + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private static SelectionSettings? MigrateSelection(SelectionSettings? selection)
        {
            if (selection is null) return null;
            var ignored = selection.IgnoredExecutables?.ToList();
            if (ignored is not null
                && !ignored.Contains("StartMenuExperienceHost.exe", StringComparer.OrdinalIgnoreCase))
                ignored.Add("StartMenuExperienceHost.exe");
            return new SelectionSettings
            {
                DefaultProfile = selection.DefaultProfile,
                Unmatched = selection.Unmatched,
                DebounceMs = 750,
                IgnoredExecutables = ignored,
                AdditionalProperties = selection.AdditionalProperties
            };
        }

        private static List<RuleSettings>? MigrateRules(List<RuleSettings>? rules)
        {
            if (rules is null) return null;
            var usedIds = new HashSet<string>(rules.Select(rule => rule.Id!).Where(id => id is not null), StringComparer.Ordinal);
            var migrated = new List<RuleSettings>(rules.Count);
            foreach (var rule in rules)
            {
                var hasExecutableMatcher = rule.Match?.Executables is not null;
                var hasExecutables = rule.Match?.Executables is { Count: > 0 };
                var hasGame = rule.Match?.Game is not null;
                if (hasExecutables)
                    migrated.Add(CopyRule(rule, rule.ApplicationGroup ?? rule.Id, match: null));
                if (hasGame)
                {
                    var id = hasExecutables ? UniqueRuleId(rule.Id + "-game", usedIds) : rule.Id;
                    migrated.Add(CopyRule(rule, applicationGroup: null, new ApplicationMatch
                    {
                        Game = rule.Match!.Game,
                        AdditionalProperties = rule.Match.AdditionalProperties
                    }, id));
                }
                if (!hasExecutables && !hasGame
                    && (!hasExecutableMatcher || rule.ApplicationGroup is not null))
                    migrated.Add(CopyRule(rule, rule.ApplicationGroup, match: null));
            }
            return migrated;
        }

        private static RuleSettings CopyRule(
            RuleSettings source,
            string? applicationGroup,
            ApplicationMatch? match,
            string? id = null) => new()
            {
                Id = id ?? source.Id,
                ApplicationGroup = applicationGroup,
                Profile = source.Profile,
                Match = match,
                Eligibility = source.Eligibility,
                Enabled = source.Enabled,
                AdditionalProperties = source.AdditionalProperties
            };

        private static string UniqueRuleId(string candidate, HashSet<string> usedIds)
        {
            for (var suffix = 1; ; suffix++)
            {
                var ending = suffix == 1 ? "" : $"-{suffix}";
                var id = candidate[..Math.Min(candidate.Length, 64 - ending.Length)] + ending;
                if (usedIds.Add(id)) return id;
            }
        }
    }
}
