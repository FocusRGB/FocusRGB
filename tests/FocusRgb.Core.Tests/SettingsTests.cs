using System.Text.Json.Nodes;
using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Tests
{
    /// <summary>Checks configuration boundaries before later slices persist or apply lighting.</summary>
    public sealed class SettingsTests
    {
        private static readonly string[] ProfileIds = ["default", "programming", "movie", "music"];
        private static readonly string[] EffectNames = ["Aurora", "Aurora", "Screen Ambient", "Logarithmic Visualizer"];
        private static readonly string[] RuleOrder = ["programming", "movie", "music"];

        [Fact]
        public void LoadDefaults_LoadsAcceptanceMappings_WithNoProviderLayout()
        {
            var result = SettingsJson.LoadDefaults();
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            var profiles = Assert.IsType<List<ProfileSettings>>(result.Settings!.Profiles);
            Assert.Equal(ProfileIds, profiles.Select(p => p.Id));
            Assert.Equal(EffectNames,
                profiles.Select(p => p.Bindings![0].Effect!.DisplayName));
            Assert.All(profiles, profile =>
            {
                var binding = Assert.Single(profile.Bindings!);
                Assert.Null(binding.Layout);
                Assert.Null(binding.Effect!.ResourceId);
                Assert.Equal("provider", binding.RenderingAuthority);
                Assert.Equal("native-setup", Assert.Single(profile.Fallback!).Kind);
            });
            var rules = Assert.IsType<List<RuleSettings>>(result.Settings.Rules);
            var groups = Assert.IsType<List<ApplicationGroupSettings>>(result.Settings.ApplicationGroups);
            Assert.Equal(RuleOrder, rules.Select(r => r.Id));
            Assert.Equal(RuleOrder, groups.Select(group => group.Id));
            Assert.Equal("default", result.Settings.Selection!.DefaultProfile);
            Assert.Equal("default-profile", result.Settings.Selection.Unmatched);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, result.Settings.SchemaVersion);
            Assert.Equal("minimize-to-tray", result.Settings.Application!.CloseButtonBehavior);
            Assert.False(result.Settings.Application.StartMinimized);
            Assert.Equal("system", result.Settings.Application.Theme);
            Assert.False(result.Settings.Application.VerboseLogging);
            Assert.Empty(result.Settings.Plugins!.Enabled!);
            Assert.Equal(["Code.exe", "devenv.exe"], groups[0].Executables);
            Assert.Equal(["PotPlayerMini64.exe", "PotPlayerMini.exe", "stremio-shell-ng.exe"],
                groups[1].Executables);
            Assert.Equal(["Microsoft.Media.Player.exe", "wmplayer.exe", "Spotify.exe"],
                groups[2].Executables);
            Assert.Equal(RuleOrder, rules.Select(rule => rule.ApplicationGroup));
        }

        [Theory]
        [InlineData("profiles.1.id", "default", "profiles[1].id")]
        [InlineData("profiles.0.id", "Bad ID", "profiles[0].id")]
        [InlineData("profiles.0.bindings.0.plugin", "PluginName", "profiles[0].bindings[0].plugin")]
        [InlineData("profiles.0.bindings.0.renderingAuthority", "unknown", "profiles[0].bindings[0].renderingAuthority")]
        [InlineData("profiles.0.bindings.0.deviceScope.kind", "all", "profiles[0].bindings[0].deviceScope.kind")]
        [InlineData("profiles.0.bindings.0.effect.displayName", "", "profiles[0].bindings[0].effect.displayName")]
        [InlineData("profiles.0.bindings.0.effect.resourceId", " ", "profiles[0].bindings[0].effect.resourceId")]
        [InlineData("rules.0.profile", "missing", "rules[0].profile")]
        [InlineData("rules.0.applicationGroup", "missing", "rules[0].applicationGroup")]
        [InlineData("rules.1.id", "programming", "rules[1].id")]
        [InlineData("rules.0.eligibility", "WhileFocused", "rules[0].eligibility")]
        [InlineData("selection.defaultProfile", "missing", "selection.defaultProfile")]
        [InlineData("selection.unmatched", "keep-current", "selection.unmatched")]
        [InlineData("selection.unmatched", "anything", "selection.unmatched")]
        [InlineData("profiles.0.fallback.0.kind", "random", "profiles[0].fallback[0].kind")]
        [InlineData("application.closeButtonBehavior", "hide", "application.closeButtonBehavior")]
        [InlineData("application.theme", "blue", "application.theme")]
        public void Load_RejectsInvalidValue_WithFieldPath(string field, string value, string errorPath)
        {
            var root = Defaults();
            Set(root, field, JsonValue.Create(value));
            AssertError(root, errorPath);
        }

        [Theory]
        [InlineData("schemaVersion", "schemaVersion")]
        [InlineData("application", "application")]
        [InlineData("application.startMinimized", "application.startMinimized")]
        [InlineData("application.verboseLogging", "application.verboseLogging")]
        [InlineData("plugins", "plugins")]
        [InlineData("plugins.enabled", "plugins.enabled")]
        [InlineData("selection", "selection")]
        [InlineData("profiles", "profiles")]
        [InlineData("applicationGroups", "applicationGroups")]
        [InlineData("rules", "rules")]
        [InlineData("profiles.0.bindings", "profiles[0].bindings")]
        [InlineData("profiles.0.bindings.0.effect", "profiles[0].bindings[0].effect")]
        [InlineData("profiles.0.bindings.0.deviceScope", "profiles[0].bindings[0].deviceScope")]
        [InlineData("rules.0.enabled", "rules[0].enabled")]
        public void Load_RejectsMissingField_WithoutPartialSettings(string field, string errorPath)
        {
            var root = Defaults();
            Set(root, field, null);
            AssertError(root, errorPath);
        }

        [Theory]
        [InlineData("profiles.0", "profiles[0]")]
        [InlineData("profiles.0.bindings.0", "profiles[0].bindings[0]")]
        [InlineData("profiles.0.fallback.0", "profiles[0].fallback[0]")]
        [InlineData("applicationGroups.0", "applicationGroups[0]")]
        [InlineData("rules.0", "rules[0]")]
        [InlineData("applicationGroups.0.executables.0", "applicationGroups[0].executables[0]")]
        public void Load_RejectsNullArrayEntry_WithFieldPath(string field, string errorPath)
        {
            var root = Defaults();
            Set(root, field, null);
            AssertError(root, errorPath);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("{\"schemaVersion\":\"1\"}")]
        public void Load_ReturnsErrors_WhenJsonIsMalformedOrWrongShape(string json)
        {
            var result = SettingsJson.Load(json);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Settings);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void Load_AcceptsCommentsAndTrailingCommas_ForHandEditedSettings()
        {
            var json = SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!);
            json = json.Replace("{", "{/* FocusRGB settings */", StringComparison.Ordinal)
                .Replace("\n}", ",\n}", StringComparison.Ordinal);

            var result = SettingsJson.Load(json);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Serialize_OmitsUnusedLegacyPriorityAndGameFields()
        {
            var json = SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!);

            Assert.DoesNotContain("\"priority\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"game\"", json, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public void Load_RejectsUnsupportedVersion_WithoutMigrationOrPersistence(int version)
        {
            var root = Defaults();
            root["schemaVersion"] = version;
            AssertError(root, "schemaVersion");
        }

        [Fact]
        public void Load_MigratesSchemaOneApplicationAndPluginPreferencesToDocumentedDefaults()
        {
            var root = Defaults();
            root["schemaVersion"] = 1;
            root.AsObject().Remove("application");

            var result = SettingsJson.Load(root.ToJsonString());

            Assert.True(result.IsSuccess);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, result.Settings!.SchemaVersion);
            Assert.Equal("minimize-to-tray", result.Settings.Application!.CloseButtonBehavior);
            Assert.False(result.Settings.Application.StartMinimized);
            Assert.Equal("system", result.Settings.Application.Theme);
            Assert.False(result.Settings.Application.VerboseLogging);
            Assert.Empty(result.Settings.Plugins!.Enabled!);
        }

        [Fact]
        public void Load_MigratesSchemaTwoWithPluginsDisabledByDefault()
        {
            var root = Defaults();
            root["schemaVersion"] = 2;
            root.AsObject().Remove("plugins");

            var result = SettingsJson.Load(root.ToJsonString());

            Assert.True(result.IsSuccess);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, result.Settings!.SchemaVersion);
            Assert.Empty(result.Settings.Plugins!.Enabled!);
        }

        [Fact]
        public void Load_MigratesSchemaThreeInlineExecutablesToReusableGroups()
        {
            var root = Defaults();
            root["schemaVersion"] = 3;
            root["plugins"]!["enabled"] = JsonNode.Parse("""["fake-output"]""");
            var executablesById = root["applicationGroups"]!.AsArray().ToDictionary(
                group => group!["id"]!.GetValue<string>(),
                group => group!["executables"]!.DeepClone());
            root.AsObject().Remove("applicationGroups");
            foreach (var node in root["rules"]!.AsArray())
            {
                var rule = node!.AsObject();
                var id = rule["applicationGroup"]!.GetValue<string>();
                rule.Remove("applicationGroup");
                rule["match"] = new JsonObject { ["executables"] = executablesById[id] };
            }

            var result = SettingsJson.Load(root.ToJsonString());

            Assert.True(result.IsSuccess);
            var group = Assert.Single(result.Settings!.ApplicationGroups!, item => item.Id == "programming");
            Assert.Equal(["Code.exe", "devenv.exe"], group.Executables);
            Assert.Equal("programming", result.Settings.Rules![0].ApplicationGroup);
            Assert.Null(result.Settings.Rules[0].Match);
            Assert.Equal(["fake-output"], result.Settings.Plugins!.Enabled);
        }

        [Fact]
        public void Load_RejectsDuplicateEnabledPlugins()
        {
            var root = Defaults();
            root["plugins"]!["enabled"] = JsonNode.Parse("""["fake-output","fake-output"]""");

            AssertError(root, "plugins.enabled[1]");
        }

        [Fact]
        public void Load_RejectsInvalidEnabledPluginId()
        {
            var root = Defaults();
            root["plugins"]!["enabled"] = JsonNode.Parse("""["Bad ID"]""");

            AssertError(root, "plugins.enabled[0]");
        }

        [Fact]
        public void Load_RejectsNegativeDebounce_ButAcceptsEmptyApplicationGroup()
        {
            var root = Defaults();
            Set(root, "selection.debounceMs", JsonValue.Create(-1));
            AssertError(root, "selection.debounceMs");

            Set(root, "selection.debounceMs", JsonValue.Create(2000));
            Set(root, "applicationGroups.0.executables", new JsonArray());
            Assert.True(SettingsJson.Load(root.ToJsonString()).IsSuccess);
        }

        [Fact]
        public void Load_RejectsApplicationGroupEntryWithoutExeSuffix()
        {
            var root = Defaults();
            Set(root, "applicationGroups.0.executables.0", JsonValue.Create("Code"));

            AssertError(root, "applicationGroups[0].executables[0]");
        }

        [Fact]
        public void Load_RejectsMoreThanOneBindingPerProfile()
        {
            var root = Defaults();
            var bindings = (JsonArray)root["profiles"]![0]!["bindings"]!;
            bindings.Add(bindings[0]!.DeepClone());
            AssertError(root, "profiles[0].bindings");
            bindings.RemoveAt(1);
            Assert.True(SettingsJson.Load(root.ToJsonString()).IsSuccess);
        }

        [Fact]
        public void Load_RejectsUnknownFallbackProfile()
        {
            var root = Defaults();
            Set(root, "profiles.0.fallback.0", JsonNode.Parse("""{"kind":"profile","profile":"missing"}"""));
            AssertError(root, "profiles[0].fallback[0].profile");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Load_RejectsFallbackCycles_WhenDirectOrIndirect(bool direct)
        {
            var root = Defaults();
            Set(root, "profiles.0.fallback.0", JsonNode.Parse(direct
                ? """{"kind":"profile","profile":"default"}"""
                : """{"kind":"profile","profile":"programming"}"""));
            if (!direct)
            {
                Set(root, "profiles.1.fallback.0", JsonNode.Parse("""{"kind":"profile","profile":"default"}"""));
            }
            AssertError(root, "profiles[0].fallback");
        }

        [Fact]
        public void Load_AcceptsAcyclicFallback_ButRejectsEmptyOutputProfile()
        {
            var root = Defaults();
            Set(root, "profiles.0.fallback.0", JsonNode.Parse("""{"kind":"profile","profile":"programming"}"""));
            Set(root, "profiles.1.bindings", new JsonArray());
            Set(root, "rules", new JsonArray());
            AssertError(root, "profiles[1].bindings");
        }

        [Fact]
        public void Load_RejectsRemovingOrReassigningProtectedDefaultProfile()
        {
            var missingDefault = Defaults();
            ((JsonArray)missingDefault["profiles"]!).RemoveAt(0);
            AssertError(missingDefault, "profiles");

            var reassignedDefault = Defaults();
            Set(reassignedDefault, "selection.defaultProfile", JsonValue.Create("programming"));
            AssertError(reassignedDefault, "selection.defaultProfile");
        }

        [Fact]
        public void Load_AcceptsApplicationAuthority_AndReservedGameMatcher()
        {
            var root = Defaults();
            Set(root, "profiles.0.bindings.0.renderingAuthority", JsonValue.Create("application"));
            Set(root, "profiles.0.bindings.0.effect", null);
            Set(root, "rules.0.applicationGroup", null);
            Set(root, "rules.0.match", JsonNode.Parse("""{"game":{"launcher":"steam","id":"570"}}"""));
            Assert.True(SettingsJson.Load(root.ToJsonString()).IsSuccess);
        }

        [Fact]
        public void Load_ValidatesReservedDeviceScope_WhenDevicesAreSpecified()
        {
            var root = Defaults();
            Set(root, "profiles.0.bindings.0.deviceScope", JsonNode.Parse("""{"kind":"devices","deviceIds":[]}"""));
            AssertError(root, "profiles[0].bindings[0].deviceScope.deviceIds");
            Set(root, "profiles.0.bindings.0.deviceScope.deviceIds", JsonNode.Parse("""["opaque/Device#1"]"""));
            Assert.True(SettingsJson.Load(root.ToJsonString()).IsSuccess);
        }

        [Fact]
        public void Serialize_PreservesUnknownFields_AndOpaqueResourceIds()
        {
            var root = Defaults();
            root["futureSetting"] = JsonNode.Parse("""{"enabled":true,"values":[1,2]}""");
            root["selection"]!["futureSelection"] = "keep";
            root["profiles"]![0]!["bindings"]![0]!["futureBinding"] = 12;
            root["profiles"]![0]!["bindings"]![0]!["effect"]!["futureResource"] = false;
            const string resourceId = " Provider/ID #Ä%2F ";
            Set(root, "profiles.0.bindings.0.effect.resourceId", JsonValue.Create(resourceId));
            var result = SettingsJson.Load(root.ToJsonString());
            Assert.True(result.IsSuccess);
            var roundTrip = JsonNode.Parse(SettingsJson.Serialize(result.Settings!))!;
            Assert.True(JsonNode.DeepEquals(root["futureSetting"], roundTrip["futureSetting"]));
            Assert.Equal("keep", roundTrip["selection"]!["futureSelection"]!.GetValue<string>());
            var binding = roundTrip["profiles"]![0]!["bindings"]![0]!;
            Assert.Equal(12, binding["futureBinding"]!.GetValue<int>());
            Assert.False(binding["effect"]!["futureResource"]!.GetValue<bool>());
            Assert.Equal(resourceId, binding["effect"]!["resourceId"]!.GetValue<string>());
        }

        [Fact]
        public void CreateDefaultNativeSetup_IsIndependent_WhenProviderSettingsChange()
        {
            var native = SettingsJson.CreateDefaultNativeSetup();
            var root = Defaults();
            Set(root, "profiles.0.bindings.0.effect.displayName", JsonValue.Create("User effect"));
            Assert.True(SettingsJson.Load(root.ToJsonString()).IsSuccess);
            Assert.Equal(1, native.SchemaVersion);
            Assert.Equal("app-default", native.Origin);
            Assert.Null(native.AppliedAt);
            Assert.Empty(native.Bindings!);
        }

        private static JsonNode Defaults() => JsonNode.Parse(
            SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;

        private static void AssertError(JsonNode root, string path)
        {
            var result = SettingsJson.Load(root.ToJsonString());
            Assert.False(result.IsSuccess);
            Assert.Null(result.Settings);
            Assert.Contains(result.Errors, error => error.Path == path && error.Message.Length > 0);
        }

        private static void Set(JsonNode root, string field, JsonNode? value)
        {
            var parts = field.Split('.');
            var parent = root;
            foreach (var part in parts[..^1])
            {
                parent = int.TryParse(part, out var index) ? parent[index]! : parent[part]!;
            }
            if (int.TryParse(parts[^1], out var lastIndex))
            {
                parent[lastIndex] = value;
            }
            else
            {
                parent[parts[^1]] = value;
            }
        }
    }
}
