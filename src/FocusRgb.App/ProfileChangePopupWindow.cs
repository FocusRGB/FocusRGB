using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace FocusRgb.App
{
    /// <summary>A single passive selection announcement; never an output confirmation.</summary>
    public sealed class ProfileChangePopupWindow : Window
    {
        private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private DateTimeOffset _shownAt;

        public ProfileChangePopupWindow()
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            WindowDecorations = WindowDecorations.None;
            IsHitTestVisible = false;
            Focusable = false;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            var panel = new Border { Padding = new Thickness(18), CornerRadius = new CornerRadius(12), Child = _message };
            panel.Bind(BackgroundProperty, this.GetResourceObservable("ThemeControlMidBrush"));
            _message.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ThemeForegroundBrush"));
            Content = panel;
            AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
            _timer.Tick += (_, _) =>
            {
                var elapsed = DateTimeOffset.UtcNow - _shownAt;
                if (elapsed >= TimeSpan.FromSeconds(2)) Dismiss();
                else if (elapsed >= TimeSpan.FromMilliseconds(1850)) Opacity = 0;
            };
            Closed += (_, _) => _timer.Stop();
        }

        public void Present(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            _message.Text = text;
            AutomationProperties.SetName(_message, text);
            if (!IsVisible)
            {
                if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
                {
                    // Exclude mouse input and activation at the native boundary, before showing.
                    var style = GetWindowLongPtr(handle.Handle, -20).ToInt64();
                    SetWindowLongPtr(handle.Handle, -20, new IntPtr(style | 0x08000000 | 0x00000020 | 0x00000080));
                }
                Opacity = 0;
                Show();
            }
            if (Screens.Primary is { } screen)
            {
                var area = screen.WorkingArea;
                Position = new PixelPoint(area.X + (area.Width - (int)(Width * screen.Scaling)) / 2,
                    area.Bottom - (int)((Math.Max(Bounds.Height, 90) + 32) * screen.Scaling));
            }
            _shownAt = DateTimeOffset.UtcNow;
            Opacity = 1;
            _timer.Start();
        }

        public void Dismiss()
        {
            _timer.Stop();
            Hide();
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    }
}
