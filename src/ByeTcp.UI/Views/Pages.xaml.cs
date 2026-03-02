using Microsoft.UI.Xaml.Controls;
using ByeTcp.UI.ViewModels;

namespace ByeTcp.UI.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage(ProfilesViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    private void GoToDiagnostics_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { }
}

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; }

    public LogsPage(LogsViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    private void GoToLogs_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) { }
}

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}

public sealed partial class RulesPage : Page
{
    public RulesPage()
    {
        this.InitializeComponent();
    }
}
