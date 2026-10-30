using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class FocusTerminologyHeadlessTests
    {
        [AvaloniaFact]
        public void Navigation_UsesFocusTerminology()
        {
            var window = new MainWindow();
            window.Show();
            try
            {
                var tabs = window.GetLogicalDescendants().OfType<TabItem>();
                Assert.Contains(tabs, tab => Equals(tab.Header, "Focus"));
                Assert.DoesNotContain(tabs, tab => Equals(tab.Header, "Home"));
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }
    }
}
