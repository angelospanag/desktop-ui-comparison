using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WeatherApp;

/// <summary>Display-ready text for the window to bind to.</summary>
public sealed record ForecastView(
    string Temperature,
    string Condition,
    string Details,
    IReadOnlyList<DayRow> Days);

public sealed record DayRow(string Date, string Condition, string Range);

public partial class MainViewModel : ObservableObject
{
    private readonly WeatherClient _client;
    private CancellationTokenSource? _currentLoad;

    public IReadOnlyList<City> Cities { get; } =
    [
        new("Athens", 37.98, 23.73),
        new("London", 51.51, -0.13),
        new("New York", 40.71, -74.01),
        new("Tokyo", 35.68, 139.69),
        new("Sydney", -33.87, 151.21),
    ];

    // [ObservableProperty] generates the code that tells bound controls to update.
    [ObservableProperty]
    public partial City? SelectedCity { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial ForecastView? Forecast { get; set; }

    public MainViewModel(WeatherClient client)
    {
        _client = client;
        SelectedCity = Cities[0]; // Triggers the first load, through OnSelectedCityChanged.
    }

    // Called by the code that [ObservableProperty] generates when the selection changes.
    partial void OnSelectedCityChanged(City? value) => _ = LoadAsync();

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    private async Task LoadAsync()
    {
        if (SelectedCity is not { } city)
        {
            return;
        }

        // Cancel any earlier request so a slow response can't overwrite this newer one.
        _currentLoad?.Cancel();
        var load = new CancellationTokenSource();
        _currentLoad = load;

        IsLoading = true;
        Error = null;
        Forecast = null;

        try
        {
            // HttpClient doesn't block the UI thread, and `await` returns to it afterwards.
            var forecast = await _client.GetForecastAsync(city, load.Token);
            load.Token.ThrowIfCancellationRequested();
            Forecast = ToView(forecast);
        }
        catch (OperationCanceledException) when (load.Token.IsCancellationRequested)
        {
            return; // A newer load has replaced this one and now owns the state.
        }
        catch (Exception exception)
        {
            // This is the top of the load, so any failure belongs on screen.
            Error = exception.Message;
        }

        IsLoading = false;
    }

    private static ForecastView ToView(Forecast forecast)
    {
        var current = forecast.Current;
        var daily = forecast.Daily;

        var days = daily.Dates
            .Zip(daily.WeatherCodes, daily.MinTemperatures)
            .Zip(daily.MaxTemperatures, (day, max) => new DayRow(
                day.First,
                WeatherClient.Describe(day.Second),
                $"{day.Third:F0}° / {max:F0}°"))
            .ToList();

        return new ForecastView(
            $"{current.Temperature:F1}°C",
            WeatherClient.Describe(current.WeatherCode),
            $"Feels like {current.FeelsLike:F1}°C · Humidity {current.Humidity:F0}% · Wind {current.WindSpeed:F1} km/h",
            days);
    }
}
