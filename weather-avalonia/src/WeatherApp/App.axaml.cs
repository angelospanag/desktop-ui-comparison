using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace WeatherApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new WeatherClient(http)),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
