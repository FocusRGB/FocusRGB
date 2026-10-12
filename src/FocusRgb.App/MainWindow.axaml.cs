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

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        private void MoveMappingUp_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            ViewModel.MappingEditor.MoveSelected(-1);

        private void MoveMappingDown_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            ViewModel.MappingEditor.MoveSelected(1);

        private void ApplyMappings_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            ViewModel.MappingEditor.Apply();

        private void CancelMappings_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            ViewModel.MappingEditor.Cancel();
    }
}
