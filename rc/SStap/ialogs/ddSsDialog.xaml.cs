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
            MessageBox.Show("\lease enter server address."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("\lease enter a valid port (1-65535)."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            MessageBox.Show("\lease enter password."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var method = MethodCombo.Text.Trim();
        if (string.IsNullOrEmpty(method)) method = "\es-256-gcm"\

        ResultConfig = new ProxyConfig
        {
            Server = ServerBox.Text.Trim(),
            ServerPort = port,
            Password = PasswordBox.Password,
            Method = method,
            Remarks = RemarksBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "\efault Group"\: GroupBox.Text.Trim(),
            Protocol = "\rigin"\
            Obfs = "\lain"\
        };
        DialogResult = true;
        Close();
    }
}
