using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WeatherApp;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(WeatherApp.Tests.TestAppBuilder))]

namespace WeatherApp.Tests;

public static class TestAppBuilder
{
    // Skia with headless drawing off renders real frames, so tests can capture them.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

public class WeatherTests
{
    // Trimmed from a real Open-Meteo response.
    private static string Json(double temperature = 10.2) => $$"""
        {
          "latitude": 37.96,
          "current": {
            "time": "2026-11-06T08:00",
            "temperature_2m": {{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
            "apparent_temperature": 8.1,
            "relative_humidity_2m": 73,
            "wind_speed_10m": 6.7,
            "weather_code": 1
          },
          "daily": {
            "time": ["2026-11-06", "2026-11-07"],
            "weather_code": [2, 61],
            "temperature_2m_max": [15.9, 15.0],
            "temperature_2m_min": [9.0, 7.9]
          }
        }
        """;

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static MainViewModel ViewModel(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new WeatherClient(new HttpClient(new StubHandler(respond))));

    private static async Task WaitUntil(Func<bool> condition, int timeoutSeconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static void ClickCity(Window window, string name)
    {
        var item = window.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(i => (i.DataContext as City)?.Name == name);
        var center = new Point(item.Bounds.Width / 2, item.Bounds.Height / 2);
        var point = item.TranslatePoint(center, window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> VisibleText(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text is not null)
            .Select(t => t.Text!)
            .ToList();

    [AvaloniaFact]
    public async Task Shows_forecast_in_the_window()
    {
        var viewModel = ViewModel(_ => Task.FromResult(Ok(Json())));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        await WaitUntil(() => viewModel.Forecast is not null);
        Dispatcher.UIThread.RunJobs();

        var text = VisibleText(window);
        Assert.Contains(text, t => t.StartsWith("10") && t.EndsWith("2°C"));
        Assert.Contains("Mainly clear", text);
        Assert.Contains("2026-11-07", text);
        Assert.Contains("Rain", text);
        Assert.DoesNotContain("Loading…", text);
    }

    [AvaloniaFact]
    public async Task Clicking_a_city_requests_its_coordinates()
    {
        var urls = new List<string>();
        var viewModel = ViewModel(request =>
        {
            urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Ok(Json()));
        });
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Forecast is not null);
        Dispatcher.UIThread.RunJobs();

        ClickCity(window, "London");

        await WaitUntil(() => urls.Count == 2);
        Assert.Equal("London", viewModel.SelectedCity?.Name);
        Assert.Contains("latitude=51.51&longitude=-0.13", urls[1]);
    }

    [AvaloniaFact]
    public async Task A_slow_response_cannot_overwrite_a_newer_city()
    {
        var slowAthens = new TaskCompletionSource<HttpResponseMessage>();
        var viewModel = ViewModel(request =>
            request.RequestUri!.Query.Contains("latitude=37.98")
                ? slowAthens.Task
                : Task.FromResult(Ok(Json(temperature: 20))));

        viewModel.SelectedCity = viewModel.Cities[1];
        await WaitUntil(() => viewModel.Forecast is not null);

        slowAthens.SetResult(Ok(Json(temperature: -5)));
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        Assert.StartsWith("20", viewModel.Forecast!.Temperature);
        Assert.False(viewModel.IsLoading);
    }

    [AvaloniaFact]
    public async Task Shows_an_error_when_the_request_fails_and_recovers_on_refresh()
    {
        var fail = true;
        var viewModel = ViewModel(_ => Task.FromResult(
            fail ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok(Json())));

        await WaitUntil(() => viewModel.Error is not null);
        Assert.Null(viewModel.Forecast);
        Assert.False(viewModel.IsLoading);

        fail = false;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Error);
        Assert.NotNull(viewModel.Forecast);
    }

    [AvaloniaFact]
    public async Task Shows_an_error_when_the_response_is_missing_fields()
    {
        var viewModel = ViewModel(_ => Task.FromResult(Ok("""{ "current": {} }""")));

        await WaitUntil(() => viewModel.Error is not null);

        Assert.Null(viewModel.Forecast);
    }

    // Exclude with: dotnet test --filter "Category!=Live"
    [AvaloniaFact]
    [Trait("Category", "Live")]
    public async Task Live_api_data_reaches_the_window()
    {
        var viewModel = new MainViewModel(new WeatherClient(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }));
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await WaitUntil(() => viewModel.Forecast is not null || viewModel.Error is not null, timeoutSeconds: 15);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(viewModel.Error);

        var athens = viewModel.Forecast!;
        ClickCity(window, "Tokyo");
        await WaitUntil(() => viewModel.Forecast is not null || viewModel.Error is not null, timeoutSeconds: 15);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(viewModel.Error);
        Assert.Equal("Tokyo", viewModel.SelectedCity?.Name);
        Assert.Equal(5, viewModel.Forecast!.Days.Count);
        Assert.NotSame(athens, viewModel.Forecast);

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(AppContext.BaseDirectory, "weather-live.png"), new PngBitmapEncoderOptions());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request).WaitAsync(cancellationToken);
    }
}
