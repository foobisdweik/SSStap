using System.Windows;
using SSStap.Models;

namespace SSStap.Dialogs;

public partial class AddSocks5Dialog : Window
{
    public ProxyConfig? ResultConfig { get; private set; }

    public AddSocks5Dialog()
    {
        InitializeComponent();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text))
        {
            MessageBox.Show("Please enter server address.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port (1-65535).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultConfig = new ProxyConfig
        {
            Server = ServerBox.Text.Trim(),
            ServerPort = port,
            Username = UsernameBox.Text.Trim(),
            Password = PasswordBox.Text,
            Remarks = RemarksBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "Default Group" : GroupBox.Text.Trim(),
        };
        DialogResult = true;
        Close();
    }
}
