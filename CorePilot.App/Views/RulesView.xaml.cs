using CorePilot.App.ViewModels;

namespace CorePilot.App.Views;

public partial class RulesView : System.Windows.Controls.UserControl
{
    public RulesView()
    {
        InitializeComponent();
        DataContext = new RulesViewModel();
    }
}
