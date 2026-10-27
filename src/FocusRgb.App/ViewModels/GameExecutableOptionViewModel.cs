using System.ComponentModel;

namespace FocusRgb.App.ViewModels
{
    public sealed class GameExecutableOptionViewModel(string name, bool selected) : INotifyPropertyChanged
    {
        private bool _isSelected = selected;
        public string Name { get; } = name;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
