using System.Text.Json;
using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Tests
{
    /// <summary>Checks persisted intent and native fallback documents before file storage uses them.</summary>
    public sealed class PersistedDocumentTests
    {
        private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 12, 13, 0, 0, TimeSpan.Zero);

        [Fact]
        public void StateJson_LoadsFirstRunState_WithoutAppliedOutput()
        {
            var state = StateJson.CreateFirstRun("default", UpdatedAt);

            var result = StateJson.Load(StateJson.Serialize(state));

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.State!.SchemaVersion);
            Assert.Equal("manual", result.State.Mode);
            Assert.Equal("default", result.State.ManualProfile);
            Assert.Equal("default", result.State.LastSelectedProfile);
            Assert.Equal(UpdatedAt, result.State.UpdatedAt);
        }

        [Theory]
        [InlineData("schemaVersion", "2")]
        [InlineData("mode", "\"sometimes\"")]
        [InlineData("manualProfile", "null")]
        [InlineData("lastSelectedProfile", "\"\"")]
        [InlineData("updatedAt", "null")]
        public void StateJson_RejectsInvalidField_WithFieldPath(string field, string value)
        {
            var json = JsonDocument.Parse(StateJson.Serialize(StateJson.CreateFirstRun("default", UpdatedAt)));
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json.RootElement.GetRawText())!;
            values[field] = JsonDocument.Parse(value).RootElement.Clone();

            var result = StateJson.Load(JsonSerializer.Serialize(values));

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Path == field);
        }

        [Fact]
        public void StateJson_ReportsUnknownProfileReferences_AgainstCurrentSettings()
        {
            var state = new SelectionStateDocument
            {
                SchemaVersion = 1,
                Mode = "automatic",
                ManualProfile = "missing-manual",
                LastSelectedProfile = "missing-last",
                UpdatedAt = UpdatedAt
            };

            var errors = StateJson.ValidateProfileReferences(
                state,
                SettingsJson.LoadDefaults().Settings!);

            Assert.Equal(["manualProfile", "lastSelectedProfile"], errors.Select(error => error.Path));
        }

        [Fact]
        public void StateJson_PreservesUnknownFields_WhenRoundTripped()
        {
            var result = StateJson.Load("""
                {
                  "schemaVersion": 1,
                  "mode": "manual",
                  "manualProfile": "default",
                  "lastSelectedProfile": "default",
                  "updatedAt": "2026-10-12T13:00:00Z",
                  "futureIntent": { "keep": true }
                }
                """);

            var roundTrip = JsonDocument.Parse(StateJson.Serialize(result.State!));

            Assert.True(result.IsSuccess);
            Assert.True(roundTrip.RootElement.GetProperty("futureIntent").GetProperty("keep").GetBoolean());
        }

        [Fact]
        public void NativeSetupJson_LoadsEmptyDefaultSnapshot()
        {
            var nativeSetup = NativeSetupJson.CreateDefault();

            var result = NativeSetupJson.Load(NativeSetupJson.Serialize(nativeSetup));

            Assert.True(result.IsSuccess);
            Assert.Equal("app-default", result.NativeSetup!.Origin);
            Assert.Null(result.NativeSetup.AppliedAt);
            Assert.Empty(result.NativeSetup.Bindings!);
        }

        [Theory]
        [InlineData("""{"schemaVersion":2,"origin":"app-default","appliedAt":null,"bindings":[]}""", "schemaVersion")]
        [InlineData("""{"schemaVersion":1,"origin":"other","appliedAt":null,"bindings":[]}""", "origin")]
        [InlineData("""{"schemaVersion":1,"origin":"app-default","appliedAt":"2026-10-12T13:00:00Z","bindings":[]}""", "appliedAt")]
        [InlineData("""{"schemaVersion":1,"origin":"user-applied","appliedAt":null,"bindings":[]}""", "appliedAt")]
        [InlineData("""{"schemaVersion":1,"origin":"app-default","appliedAt":null,"bindings":null}""", "bindings")]
        public void NativeSetupJson_RejectsInvalidDocument_WithFieldPath(string json, string path)
        {
            var result = NativeSetupJson.Load(json);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Path == path);
        }

        [Fact]
        public void NativeSetupJson_RejectsProviderAuthorityBinding()
        {
            var result = NativeSetupJson.Load("""
                {
                  "schemaVersion": 1,
                  "origin": "user-applied",
                  "appliedAt": "2026-10-12T13:00:00Z",
                  "bindings": [{
                    "id": "main",
                    "plugin": "wled",
                    "renderingAuthority": "provider",
                    "deviceScope": { "kind": "devices", "deviceIds": ["desk"] },
                    "effect": null,
                    "layout": null
                  }]
                }
                """);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors, error => error.Path == "bindings[0].renderingAuthority");
        }
    }
}
