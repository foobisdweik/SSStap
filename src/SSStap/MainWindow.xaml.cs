using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SSStap;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void AddProxyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu menu)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
