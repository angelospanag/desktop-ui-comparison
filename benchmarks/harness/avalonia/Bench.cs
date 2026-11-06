// Switches cities repeatedly and records the time from each switch to a rendered frame.

using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;

namespace WeatherApp;

public static class Bench
{
    public static async Task Run(Window window, MainViewModel viewModel)
    {
        var samples = new List<double>();
        var stopwatch = Stopwatch.StartNew();
        await Loaded(window, viewModel);
        samples.Add(stopwatch.Elapsed.TotalMilliseconds);

        // One initial load plus 100 switches.
        var index = 0;
        while (samples.Count < 101)
        {
            await Task.Delay(50);
            index = (index + 1) % viewModel.Cities.Count;
            stopwatch.Restart();
            viewModel.SelectedCity = viewModel.Cities[index];
            await Loaded(window, viewModel);
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        File.WriteAllText(Environment.GetEnvironmentVariable("BENCH_OUT")!, string.Join("\n", samples));
    }

    private static async Task Loaded(Window window, MainViewModel viewModel)
    {
        await Forecast(viewModel);

        // The first frame draws the new data. The second confirms that frame has finished.
        await NextFrame(window);
        await NextFrame(window);
    }

    private static Task Forecast(MainViewModel viewModel)
    {
        if (viewModel.Forecast is not null)
        {
            return Task.CompletedTask;
        }

        var loaded = new TaskCompletionSource();
        PropertyChangedEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Forecast) && viewModel.Forecast is not null)
            {
                viewModel.PropertyChanged -= handler;
                loaded.SetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        return loaded.Task;
    }

    private static Task NextFrame(Window window)
    {
        var frame = new TaskCompletionSource();
        window.RequestAnimationFrame(_ => frame.SetResult());
        return frame.Task;
    }
}
