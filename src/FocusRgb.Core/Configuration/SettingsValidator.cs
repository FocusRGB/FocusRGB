namespace FocusRgb.Core.Configuration
{
    /// <summary>Checks structural validity and cross-references without calling any provider.</summary>
    public static class SettingsValidator
    {
        public static IReadOnlyList<ConfigurationError> Validate(SettingsDocument? document)
        {
            var errors = new List<ConfigurationError>();
            void Error(string path, string message) => errors.Add(new() { Path = path, Message = message });
            void Required(string? value, string path)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Error(path, "required non-empty string");
                }
            }
            void Id(string? value, string path)
            {
                if (!IsId(value))
                {
                    Error(path, "must match ^[a-z0-9][a-z0-9-]{0,63}$");
                }
            }
            void Choice(string? value, string path, params string[] choices)
            {
                if (!choices.Contains(value, StringComparer.Ordinal))
                {
                    Error(path, "must be one of: " + string.Join(", ", choices));
                }
            }
            void Resource(ResourceReference? value, string path)
            {
                if (value is null)
                {
                    Error(path, "required for provider authority");
                    return;
                }
                Required(value.DisplayName, path + ".displayName");
                if (value.ResourceId is not null && string.IsNullOrWhiteSpace(value.ResourceId))
                {
                    Error(path + ".resourceId", "must be null or a non-empty opaque ID");
                }
            }

            if (document is null)
            {
                Error("$", "required settings object");
                return errors;
            }
            if (document.SchemaVersion != SettingsJson.CurrentSchemaVersion)
            {
                Error("schemaVersion", $"only schema version {SettingsJson.CurrentSchemaVersion} is supported; document must not be overwritten");
            }
            if (document.Application is null)
            {
                Error("application", "required object");
            }
            else
            {
                Choice(
                    document.Application.CloseButtonBehavior,
                    "application.closeButtonBehavior",
                    "minimize-to-tray",
                    "exit");
                if (document.Application.StartMinimized is null)
                    Error("application.startMinimized", "required boolean");
                Choice(document.Application.Theme, "application.theme", "system", "light", "dark");
                if (document.Application.VerboseLogging is null)
                    Error("application.verboseLogging", "required boolean");
            }
            if (document.Plugins is null)
            {
                Error("plugins", "required object");
            }
            else if (document.Plugins.Enabled is null)
            {
                Error("plugins.enabled", "required array");
            }
            else
            {
                var enabled = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < document.Plugins.Enabled.Count; i++)
                {
                    var pluginId = document.Plugins.Enabled[i];
                    Id(pluginId, $"plugins.enabled[{i}]");
                    if (pluginId is not null && !enabled.Add(pluginId))
                    {
                        Error($"plugins.enabled[{i}]", "duplicate plugin ID '" + pluginId + "'");
                    }
                }
            }
            var profiles = new Dictionary<string, ProfileSettings>(StringComparer.Ordinal);
            if (document.Profiles is null)
            {
                Error("profiles", "required array");
            }
            else
            {
                for (var i = 0; i < document.Profiles.Count; i++)
                {
                    var profile = document.Profiles[i];
                    var path = $"profiles[{i}]";
                    if (profile is null)
                    {
                        Error(path, "required profile object");
                        continue;
                    }
                    Id(profile.Id, path + ".id");
                    Required(profile.DisplayName, path + ".displayName");
                    if (profile.Id is not null && !profiles.TryAdd(profile.Id, profile))
                    {
                        Error(path + ".id", "duplicate profile ID '" + profile.Id + "'");
                    }
                    if (profile.Bindings is not { Count: 1 })
                    {
                        Error(path + ".bindings", "must contain exactly one effect and layout binding");
                    }
                    else
                    {
                        var bindingIds = new HashSet<string>(StringComparer.Ordinal);
                        for (var b = 0; b < profile.Bindings.Count; b++)
                        {
                            var binding = profile.Bindings[b];
                            var bp = $"{path}.bindings[{b}]";
                            if (binding is null)
                            {
                                Error(bp, "required binding object");
                                continue;
                            }
                            Id(binding.Id, bp + ".id");
                            if (binding.Id is not null && !bindingIds.Add(binding.Id))
                            {
                                Error(bp + ".id", "duplicate binding ID '" + binding.Id + "'");
                            }
                            Id(binding.Plugin, bp + ".plugin");
                            Choice(binding.RenderingAuthority, bp + ".renderingAuthority", "provider", "application");
                            if (binding.DeviceScope is null)
                            {
                                Error(bp + ".deviceScope", "required object");
                            }
                            else
                            {
                                Choice(binding.DeviceScope.Kind, bp + ".deviceScope.kind", "provider-managed", "devices");
                                if (binding.DeviceScope.Kind == "devices")
                                {
                                    ValidateNames(binding.DeviceScope.DeviceIds, bp + ".deviceScope.deviceIds", true, Error);
                                }
                            }
                            if (binding.RenderingAuthority == "provider" || binding.Effect is not null)
                            {
                                Resource(binding.Effect, bp + ".effect");
                            }
                            if (binding.Layout is not null)
                            {
                                Resource(binding.Layout, bp + ".layout");
                            }
                        }
                    }
                    if (profile.Fallback is null)
                    {
                        Error(path + ".fallback", "required array (may be empty)");
                    }
                }
            }

            if (!profiles.ContainsKey("default"))
            {
                Error("profiles", "must contain the protected profile 'default'");
            }

            void ProfileReference(string? id, string path)
            {
                if (id is null || !profiles.ContainsKey(id))
                {
                    Error(path, "unknown profile '" + id + "'");
                }
            }
            if (document.Selection is null)
            {
                Error("selection", "required object");
            }
            else
            {
                if (document.Selection.DefaultProfile != "default")
                {
                    Error("selection.defaultProfile", "must reference the protected profile 'default'");
                }
                else
                {
                    ProfileReference(document.Selection.DefaultProfile, "selection.defaultProfile");
                }
                Choice(document.Selection.Unmatched, "selection.unmatched", "default-profile");
                if (document.Selection.DebounceMs is null or < 0)
                {
                    Error("selection.debounceMs", "required non-negative integer milliseconds");
                }
                ValidateNames(document.Selection.IgnoredExecutables, "selection.ignoredExecutables", false, Error);
            }
            var applicationGroups = new HashSet<string>(StringComparer.Ordinal);
            if (document.ApplicationGroups is null)
            {
                Error("applicationGroups", "required array (may be empty)");
            }
            else
            {
                for (var i = 0; i < document.ApplicationGroups.Count; i++)
                {
                    var group = document.ApplicationGroups[i];
                    var path = $"applicationGroups[{i}]";
                    if (group is null)
                    {
                        Error(path, "required application group object");
                        continue;
                    }
                    Id(group.Id, path + ".id");
                    Required(group.DisplayName, path + ".displayName");
                    ValidateNames(group.Executables, path + ".executables", false, Error);
                    if (group.Executables is not null)
                    {
                        for (var executableIndex = 0; executableIndex < group.Executables.Count; executableIndex++)
                        {
                            if (!string.IsNullOrWhiteSpace(group.Executables[executableIndex])
                                && !group.Executables[executableIndex].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                Error($"{path}.executables[{executableIndex}]", "must end in .exe");
                            }
                        }
                    }
                    if (group.Id is not null && !applicationGroups.Add(group.Id))
                    {
                        Error(path + ".id", "duplicate application group ID '" + group.Id + "'");
                    }
                }
            }
            if (document.Rules is null)
            {
                Error("rules", "required array (may be empty)");
            }
            else
            {
                var ruleIds = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < document.Rules.Count; i++)
                {
                    var rule = document.Rules[i];
                    var path = $"rules[{i}]";
                    if (rule is null)
                    {
                        Error(path, "required rule object");
                        continue;
                    }
                    Id(rule.Id, path + ".id");
                    if (rule.Id is not null && !ruleIds.Add(rule.Id))
                    {
                        Error(path + ".id", "duplicate rule ID '" + rule.Id + "'");
                    }
                    if (rule.ApplicationGroup is not null && !applicationGroups.Contains(rule.ApplicationGroup))
                    {
                        Error(path + ".applicationGroup", "unknown application group '" + rule.ApplicationGroup + "'");
                    }
                    ProfileReference(rule.Profile, path + ".profile");
                    Choice(rule.Eligibility, path + ".eligibility", "while-running", "while-focused");
                    if (rule.Enabled is null)
                    {
                        Error(path + ".enabled", "required boolean");
                    }
                    if (rule.Match?.Executables is not null)
                    {
                        Error(path + ".match.executables", "use applicationGroup for executable matching");
                    }
                    if (rule.Match?.Game is not null)
                    {
                        Required(rule.Match.Game.Launcher, path + ".match.game.launcher");
                        Required(rule.Match.Game.Id, path + ".match.game.id");
                    }
                    if (rule.ApplicationGroup is null && rule.Match?.Game is null)
                    {
                        Error(path, "applicationGroup or game matcher is required");
                    }
                    if (rule.ApplicationGroup is not null && rule.Match?.Game is not null)
                    {
                        Error(path, "applicationGroup and game matcher cannot be combined");
                    }
                }
            }

            if (document.Profiles is not null)
            {
                for (var i = 0; i < document.Profiles.Count; i++)
                {
                    var profile = document.Profiles[i];
                    if (profile?.Fallback is null)
                    {
                        continue;
                    }
                    for (var f = 0; f < profile.Fallback.Count; f++)
                    {
                        var fallback = profile.Fallback[f];
                        var path = $"profiles[{i}].fallback[{f}]";
                        if (fallback is null)
                        {
                            Error(path, "required fallback object");
                            continue;
                        }
                        Choice(fallback.Kind, path + ".kind", "native-setup", "profile");
                        if (fallback.Kind == "profile")
                        {
                            ProfileReference(fallback.Profile, path + ".profile");
                        }
                    }
                    if (profile.Id is not null && HasCycleTo(profile.Id, profiles))
                    {
                        Error($"profiles[{i}].fallback", "fallback cycle returns to profile '" + profile.Id + "'");
                    }
                }
            }
            return errors;
        }

        private static bool IsId(string? value) => value is { Length: >= 1 and <= 64 }
            && IsLetterOrDigit(value[0]) && value.All(c => IsLetterOrDigit(c) || c == '-');

        private static bool IsLetterOrDigit(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9';

        private static void ValidateNames(List<string>? names, string path, bool requireItems,
            Action<string, string> error)
        {
            if (names is null || (requireItems && names.Count == 0))
            {
                error(path, requireItems ? "required non-empty array" : "required array");
                return;
            }
            for (var i = 0; i < names.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i]))
                {
                    error($"{path}[{i}]", "required non-empty string");
                }
            }
        }

        private static bool HasCycleTo(string root, Dictionary<string, ProfileSettings> profiles)
        {
            var pending = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(root);
            while (pending.TryPop(out var id))
            {
                if (!visited.Add(id) || !profiles.TryGetValue(id, out var profile))
                {
                    continue;
                }
                foreach (var candidate in profile.Fallback ?? [])
                {
                    if (candidate?.Kind == "profile" && candidate.Profile is not null)
                    {
                        if (candidate.Profile == root)
                        {
                            return true;
                        }
                        pending.Push(candidate.Profile);
                    }
                }
            }
            return false;
        }
    }
}
