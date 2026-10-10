using Avalonia.Controls;
using FocusRgb.Contracts;

namespace FocusRgb.App
{
    /// <summary>Placeholder settings window until the tray shell arrives in slice 10.</summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = $"Plugin contract {ContractVersion.Current}";
        }
    }
}
