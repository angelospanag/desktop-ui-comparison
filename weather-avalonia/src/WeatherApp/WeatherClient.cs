using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeatherApp;

public sealed record City(string Name, double Latitude, double Longitude);

// These records mirror the parts of the API response that the app uses.
public sealed record Forecast(
    [property: JsonPropertyName("current")] CurrentWeather Current,
    [property: JsonPropertyName("daily")] DailyWeather Daily);

public sealed record CurrentWeather(
    [property: JsonPropertyName("temperature_2m")] double Temperature,
    [property: JsonPropertyName("apparent_temperature")] double FeelsLike,
    [property: JsonPropertyName("relative_humidity_2m")] double Humidity,
    [property: JsonPropertyName("wind_speed_10m")] double WindSpeed,
    [property: JsonPropertyName("weather_code")] int WeatherCode);

// The API returns daily values as parallel arrays: index N in each one is the same day.
public sealed record DailyWeather(
    [property: JsonPropertyName("time")] string[] Dates,
    [property: JsonPropertyName("weather_code")] int[] WeatherCodes,
    [property: JsonPropertyName("temperature_2m_max")] double[] MaxTemperatures,
    [property: JsonPropertyName("temperature_2m_min")] double[] MinTemperatures);

// Generates the JSON code at build time, which Native AOT builds require.
// The Respect* options stop a missing or null field silently becoming null or 0.
[JsonSourceGenerationOptions(
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(Forecast))]
internal sealed partial class WeatherJson : JsonSerializerContext;

public sealed class WeatherClient(HttpClient http)
{
    private const string Fields =
        "current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code" +
        "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
        "&timezone=auto&forecast_days=5";

    public async Task<Forecast> GetForecastAsync(City city, CancellationToken cancellationToken)
    {
        // Invariant formatting keeps "37.98" from becoming "37,98" under locales such as Greek.
        var url = FormattableString.Invariant(
            $"https://api.open-meteo.com/v1/forecast?latitude={city.Latitude}&longitude={city.Longitude}&{Fields}");

        var forecast = await http.GetFromJsonAsync(url, WeatherJson.Default.Forecast, cancellationToken);
        return forecast ?? throw new JsonException("The API returned an empty response.");
    }

    /// <summary>Translates the WMO weather codes that Open-Meteo returns.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "Unknown",
    };
}
