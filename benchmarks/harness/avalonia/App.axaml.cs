// Replaces the app's App.axaml.cs in the self-driving copy, to start the benchmark.

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
            var viewModel = new MainViewModel(new WeatherClient(http));
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await Bench.Run(window, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
