using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class ProfileChangePopupHeadlessTests
    {
        [AvaloniaFact]
        public void Present_RestartsDismissTimer_WhenAnotherTransitionArrives()
        {
            var clock = new Clock();
            var popup = new ProfileChangePopupWindow(clock);
            try
            {
                popup.Present("Movie");
                clock.Advance(2500);
                popup.Present("Music");
                clock.Advance(2500);
                popup.UpdateDismissal();
                Assert.True(popup.IsVisible);
                clock.Advance(650);
                popup.UpdateDismissal();
                Assert.False(popup.IsVisible);
            }
            finally
            {
                popup.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData("small", "dark", 2, 320)]
        [InlineData("medium", "light", 3, 420)]
        [InlineData("large", "blue", 5, 520)]
        public void Appearance_IsOpaqueAndDismissesOnlyAfterConfiguredDuration(string size, string background, int seconds, int width)
        {
            var clock = new Clock();
            var popup = new ProfileChangePopupWindow(clock);
            try
            {
                popup.Configure(new NotificationSettings { Size = size, Background = background, DurationSeconds = seconds });
                popup.Present("Focus: Test\nProfile: Test\nOutput: SignalRGB");
                var border = Assert.IsType<Border>(popup.Content);
                Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(popup.Background).Color.A);
                Assert.Contains(WindowTransparencyLevel.Transparent, popup.TransparencyLevelHint);
                Assert.Equal(12, border.CornerRadius.TopLeft);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color.A);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color.A);
                Assert.Equal(1, border.BorderThickness.Left);
                Assert.Equal(width, popup.Width);
                Assert.Equal(background == "light" ? Colors.Black : Colors.White,
                    Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<TextBlock>(border.Child).Foreground).Color);
                clock.Advance(seconds * 1000 - 1);
                popup.UpdateDismissal();
                Assert.True(popup.IsVisible);
                Assert.Equal(1, popup.GetBaseValue(Avalonia.Visual.OpacityProperty).Value);
                clock.Advance(1);
                popup.UpdateDismissal();
                Assert.Equal(0, popup.GetBaseValue(Avalonia.Visual.OpacityProperty).Value);
                Assert.True(popup.IsVisible);
                clock.Advance(150);
                popup.UpdateDismissal();
                Assert.False(popup.IsVisible);
            }
            finally { popup.Close(); }
        }

        private sealed class Clock : TimeProvider
        {
            private long _milliseconds;
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => _milliseconds;
            public void Advance(int milliseconds) => _milliseconds += milliseconds;
        }

        [AvaloniaTheory]
        [InlineData("SignalRGB")]
        [InlineData("None")]
        public void Present_ReusesPassiveWindowAndPublishesLiveAnnouncement(string outputName)
        {
            var popup = new ProfileChangePopupWindow();
            try
            {
                popup.Present($"Focus: Media\nProfile: Movie\nOutput: {outputName}");
                Assert.True(popup.IsVisible);
                Assert.False(popup.ShowActivated);
                Assert.False(popup.ShowInTaskbar);
                Assert.False(popup.IsHitTestVisible);
                Assert.False(popup.Focusable);
                Assert.True(popup.Topmost);
                popup.Present($"Focus: Player\nProfile: Music\nOutput: {outputName}");
                var message = Assert.IsType<TextBlock>(Assert.IsType<Border>(popup.Content).Child);
                Assert.Equal($"Focus: Player\nProfile: Music\nOutput: {outputName}", message.Text);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(message));
                Assert.Equal(message.Text, AutomationProperties.GetName(message));
                popup.Dismiss();
                Assert.False(popup.IsVisible);
            }
            finally
            {
                popup.Close();
            }
        }
    }
}
