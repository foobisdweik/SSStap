using System.Windows;
using System.Windows.Controls;
using SSStap.Models;

namespace SSStap.Dialogs;

public partial class AddSsDialog : Window
{
    public ProxyConfig? ResultConfig { get; private set; }

    public AddSsDialog()
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
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            MessageBox.Show("Please enter password.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var method = MethodCombo.Text.Trim();
        if (string.IsNullOrEmpty(method)) method = "aes-256-gcm";

        ResultConfig = new ProxyConfig
        {
            Server = ServerBox.Text.Trim(),
            ServerPort = port,
            Password = PasswordBox.Password,
            Method = method,
            Remarks = RemarksBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "Default Group" : GroupBox.Text.Trim(),
            Protocol = "origin",
            Obfs = "plain",
        };
        DialogResult = true;
        Close();
    }
}
