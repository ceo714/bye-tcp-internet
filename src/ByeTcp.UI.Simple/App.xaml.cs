using Microsoft.UI.Xaml;

namespace ByeTcp.UI.Simple;

public partial class App : Application
{
    private Window? mainWindow;

    public App() => this.UnhandledException += OnUnhandledException;

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        mainWindow = new MainWindow();
        mainWindow.Activate();
    }
}
