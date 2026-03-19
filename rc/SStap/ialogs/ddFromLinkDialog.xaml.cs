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
            MessageBox.Show("\lease paste an ss:// or ssr:// link."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = ProxyLinkParser.Parse(link);
        if (config == null)
        {
            MessageBox.Show("\ould not parse the link. Please check the format."\ "\arse Error"\ MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("\lease enter server address or parse a link."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("\lease enter a valid port (1-65535)."\ "\alidation"\ MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = _parsedConfig?.Type ?? (int)ProxyType.Shadowsocks;
        ResultConfig = new ProxyConfig
        {
            Server = ServerBox.Text.Trim(),
            ServerPort = port,
            Password = PasswordBox.Password,
            Method = string.IsNullOrWhiteSpace(MethodBox.Text) ? "\es-256-gcm"\: MethodBox.Text.Trim(),
            Remarks = RemarksBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "\efault Group"\: GroupBox.Text.Trim(),
            Type = type,
            Protocol = _parsedConfig?.Protocol ?? "\rigin"\
            Obfs = _parsedConfig?.Obfs ?? "\lain"\
            ObfsParam = _parsedConfig?.ObfsParam ?? "\