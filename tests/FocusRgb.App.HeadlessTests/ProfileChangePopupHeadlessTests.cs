using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class ProfileChangePopupHeadlessTests
    {
        [AvaloniaFact]
        public async Task Present_RestartsDismissTimer_WhenAnotherTransitionArrives()
        {
            var popup = new ProfileChangePopupWindow();
            try
            {
                popup.Present("Movie");
                await Task.Delay(1200, TestContext.Current.CancellationToken);
                popup.Present("Music");
                await Task.Delay(1100, TestContext.Current.CancellationToken);
                Assert.True(popup.IsVisible);
                await Task.Delay(1200, TestContext.Current.CancellationToken);
                Assert.False(popup.IsVisible);
            }
            finally
            {
                popup.Close();
            }
        }

        [AvaloniaFact]
        public void Present_ReusesPassiveWindowAndPublishesLiveAnnouncement()
        {
            var popup = new ProfileChangePopupWindow();
            try
            {
                popup.Present("Movie · Media\nUnverified");
                Assert.True(popup.IsVisible);
                Assert.False(popup.ShowActivated);
                Assert.False(popup.ShowInTaskbar);
                Assert.False(popup.IsHitTestVisible);
                Assert.False(popup.Focusable);
                Assert.True(popup.Topmost);
                popup.Present("Music · Player\nUnverified");
                var message = Assert.IsType<TextBlock>(Assert.IsType<Border>(popup.Content).Child);
                Assert.Equal("Music · Player\nUnverified", message.Text);
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
