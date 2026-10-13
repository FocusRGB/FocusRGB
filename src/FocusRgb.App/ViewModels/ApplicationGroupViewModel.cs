using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusRgb.App.ViewModels
{
    /// <summary>A named, reusable group of executable names used by mappings.</summary>
    public sealed class ApplicationGroupViewModel : INotifyPropertyChanged
    {
        private string _displayName;
        private string? _selectedExecutable;

        public ApplicationGroupViewModel(string id, string displayName, IEnumerable<string> executables)
        {
            Id = id;
            _displayName = displayName;
            Executables = new ObservableCollection<string>(executables);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public ObservableCollection<string> Executables { get; }

        public string DisplayName
        {
            get => _displayName;
            set => Set(ref _displayName, value);
        }

        public string? SelectedExecutable
        {
            get => _selectedExecutable;
            set => Set(ref _selectedExecutable, value);
        }

        public void AddExecutable(string path)
        {
            var executable = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(executable)
                || Executables.Contains(executable, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            Executables.Add(executable);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Executables)));
        }

        public void RemoveSelectedExecutable()
        {
            if (SelectedExecutable is null || !Executables.Remove(SelectedExecutable))
            {
                return;
            }

            SelectedExecutable = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Executables)));
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
