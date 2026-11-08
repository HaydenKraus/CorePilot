using System.IO;
using System.Windows;
using System.Windows.Input;
using CorePilot.App.Localization;
using Window = System.Windows.Window;

namespace CorePilot.App.Controls;

public partial class RunTaskDialog : Window
{
    public RunTaskDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PathBox.Focus();
    }

    public string ExecutablePath => PathBox.Text.Trim();

    public string? Arguments => string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();

    public string? WorkingDirectory =>
        string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance["runTask.path"],
            Filter = "*.exe|*.exe|*.*|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
            {
                WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            ErrorText.Text = LocalizationService.Instance["runTask.invalidPath"];
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
