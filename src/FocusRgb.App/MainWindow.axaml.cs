using Avalonia.Controls;
using FocusRgb.Contracts;

namespace FocusRgb.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Plugin contract {ContractVersion.Current}";
    }
}
