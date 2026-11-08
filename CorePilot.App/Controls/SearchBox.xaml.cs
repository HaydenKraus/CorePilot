using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace CorePilot.App.Controls;

public partial class SearchBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualStateChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty, OnVisualStateChanged));

    public SearchBox()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncVisualState();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SearchBox)d).SyncVisualState();

    private void SyncVisualState()
    {
        if (PlaceholderText is null || ClearButton is null)
        {
            return;
        }

        var empty = string.IsNullOrEmpty(Text);
        PlaceholderText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Text = string.Empty;
        Input.Focus();
    }
}
