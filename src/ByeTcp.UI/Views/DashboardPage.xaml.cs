using Microsoft.UI.Xaml.Controls;
using ByeTcp.UI.ViewModels;

namespace ByeTcp.UI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}
