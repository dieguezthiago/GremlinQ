using System.Windows;
using GremlinQ.Core.Models;

namespace GremlinQ.App;

public partial class AddEditConnectionWindow : Window
{
    private readonly ConnectionProfile? _original;
    private bool _keyChanged;

    public AddEditConnectionWindow(ConnectionProfile? profile = null)
    {
        _original = profile;
        InitializeComponent();

        if (profile is not null)
        {
            Title = "Edit Connection";
            TxtName.Text = profile.Name;
            TxtHost.Text = profile.Host;
            TxtPort.Text = profile.Port.ToString();
            ChkSsl.IsChecked = profile.EnableSsl;
            TxtDatabase.Text = profile.Database;
            TxtCollection.Text = profile.Collection;
            PwdKey.Password = new string('●', 8);
            TxtKeyHint.Visibility = Visibility.Visible;
        }
        else
        {
            Title = "New Connection";
            TxtPort.Text = "443";
            ChkSsl.IsChecked = true;
        }

        // Subscribe after the initial placeholder set so it doesn't trigger _keyChanged.
        PwdKey.PasswordChanged += PwdKey_PasswordChanged;
    }

    public ConnectionProfile? Result { get; private set; }

    private void PwdKey_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _keyChanged = true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtHost.Text))
        {
            MessageBox.Show("Host is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtPort.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("Port must be a number between 1 and 65535.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var key = _original is not null && !_keyChanged
            ? _original.Key
            : PwdKey.Password;

        Result = new ConnectionProfile
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = TxtName.Text.Trim(),
            Host = TxtHost.Text.Trim(),
            Port = port,
            EnableSsl = ChkSsl.IsChecked == true,
            Database = TxtDatabase.Text.Trim(),
            Collection = TxtCollection.Text.Trim(),
            Key = key
        };

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}