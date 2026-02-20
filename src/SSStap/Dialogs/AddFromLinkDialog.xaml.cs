using System.Windows;
using System.Windows.Controls;
using SSStap.Models;
using SSStap.Services;

namespace SSStap.Dialogs;

public partial class AddFromLinkDialog : Window
{
    public ProxyConfig? ResultConfig { get; private set; }

    private ProxyConfig? _parsedConfig; // Preserves Protocol, Obfs, ObfsParam, ProtocolParam from parse

    public AddFromLinkDialog()
    {
        InitializeComponent();
    }

    private void Parse_Click(object sender, RoutedEventArgs e)
    {
        var link = LinkBox.Text?.Trim();
        if (string.IsNullOrEmpty(link))
        {
            MessageBox.Show("Please paste an ss:// or ssr:// link.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = ProxyLinkParser.Parse(link);
        if (config == null)
        {
            MessageBox.Show("Could not parse the link. Please check the format.", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _parsedConfig = config;
        ServerBox.Text = config.Server;
        PortBox.Text = config.ServerPort.ToString();
        PasswordBox.Password = config.Password;
        MethodBox.Text = config.Method;
        RemarksBox.Text = config.Remarks;
        GroupBox.Text = config.Group;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text))
        {
            MessageBox.Show("Please enter server address or parse a link.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port (1-65535).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = _parsedConfig?.Type ?? (int)ProxyType.Shadowsocks;
        ResultConfig = new ProxyConfig
        {
            Server = ServerBox.Text.Trim(),
            ServerPort = port,
            Password = PasswordBox.Password,
            Method = string.IsNullOrWhiteSpace(MethodBox.Text) ? "aes-256-gcm" : MethodBox.Text.Trim(),
            Remarks = RemarksBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "Default Group" : GroupBox.Text.Trim(),
            Type = type,
            Protocol = _parsedConfig?.Protocol ?? "origin",
            Obfs = _parsedConfig?.Obfs ?? "plain",
            ObfsParam = _parsedConfig?.ObfsParam ?? "",
            ProtocolParam = _parsedConfig?.ProtocolParam ?? "",
        };
        DialogResult = true;
        Close();
    }
}
