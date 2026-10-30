using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App
{
    /// <summary>A single passive selection announcement; never an output confirmation.</summary>
    public sealed class ProfileChangePopupWindow : Window
    {
        private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly TimeProvider _clock;
        private readonly Border _panel;
        private long _shownAt;
        private TimeSpan _displayDuration = TimeSpan.FromSeconds(3);
        private (string? Size, string? Background, int? Duration)? _appearance;

        public ProfileChangePopupWindow() : this(TimeProvider.System) { }

        internal ProfileChangePopupWindow(TimeProvider clock)
        {
            _clock = clock;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            WindowDecorations = WindowDecorations.None;
            IsHitTestVisible = false;
            Focusable = false;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            _panel = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = _message };
            Content = _panel;
            Configure(new NotificationSettings());
            AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
            _timer.Tick += (_, _) => UpdateDismissal();
            Closed += (_, _) => _timer.Stop();
        }

        public void Configure(NotificationSettings? settings)
        {
            settings ??= new NotificationSettings();
            var appearance = (settings.Size, settings.Background, settings.DurationSeconds);
            if (_appearance == appearance) return;
            _appearance = appearance;
            Width = settings.Size switch { "small" => 320, "large" => 520, _ => 420 };
            _message.FontSize = settings.Size switch { "small" => 12, "large" => 18, _ => 14 };
            _panel.Padding = new Thickness(settings.Size switch { "small" => 12, "large" => 24, _ => 18 });
            var light = settings.Background == "light";
            Background = Brushes.Transparent;
            _panel.Background = new SolidColorBrush(Color.Parse(settings.Background switch
            {
                "light" => "#F3F4F6", "blue" => "#2452A1", _ => "#1F2937"
            }));
            _panel.BorderBrush = light ? Brushes.DarkSlateGray : Brushes.LightGray;
            _message.Foreground = light ? Brushes.Black : Brushes.White;
            _displayDuration = TimeSpan.FromSeconds(settings.DurationSeconds is 2 or 3 or 5 ? settings.DurationSeconds.Value : 3);
        }

        internal void UpdateDismissal()
        {
            var elapsed = _clock.GetElapsedTime(_shownAt);
            if (elapsed >= _displayDuration + TimeSpan.FromMilliseconds(150)) Dismiss();
            else if (elapsed >= _displayDuration) Opacity = 0;
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
            _shownAt = _clock.GetTimestamp();
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
