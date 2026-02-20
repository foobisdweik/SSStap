using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SSStap;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new ViewModels.MainViewModel();
        DataContext = vm;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();

        Loaded += (_, _) =>
        {
            if (vm.LogEntries is INotifyCollectionChanged incc)
                incc.CollectionChanged += (s, e) => LogScrollViewer?.ScrollToEnd();
        };
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
