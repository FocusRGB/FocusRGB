using Avalonia.Controls;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App
{
    /// <summary>Hosts the preview settings shell before runtime services are connected.</summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = MainWindowViewModel.CreatePreview();
        }
    }
}
