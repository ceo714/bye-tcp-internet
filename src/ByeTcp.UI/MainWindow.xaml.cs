using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ByeTcp.UI.ViewModels;
using ByeTcp.UI.Views;

namespace ByeTcp.UI;

/// <summary>
/// Main Window with NavigationView
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        
        this.InitializeComponent();
        
        // Set initial title
        this.Title = "Bye-TCP Internet v2.0";
        
        // Set default size
        this.Activate();
        
        // Select Dashboard by default
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedItem = args.SelectedItem as NavigationViewItem;
        if (selectedItem?.Tag == null) return;
        
        var pageTag = selectedItem.Tag.ToString();
        NavigateToPage(pageTag);
    }

    private void NavigateToPage(string pageTag)
    {
        Type? pageType = pageTag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Profiles" => typeof(ProfilesPage),
            "Rules" => typeof(RulesPage),
            "Diagnostics" => typeof(DiagnosticsPage),
            "Logs" => typeof(LogsPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType != null && ContentFrame?.CurrentSourcePageType != pageType)
        {
            // Create ViewModel from DI
            var viewModel = pageTag switch
            {
                "Dashboard" => _services.GetService<DashboardViewModel>(),
                "Profiles" => _services.GetService<ProfilesViewModel>(),
                "Diagnostics" => _services.GetService<DiagnosticsViewModel>(),
                "Logs" => _services.GetService<LogsViewModel>(),
                "Settings" => _services.GetService<SettingsViewModel>(),
                _ => null
            };

            ContentFrame?.Navigate(pageType, viewModel);
        }
    }
}
