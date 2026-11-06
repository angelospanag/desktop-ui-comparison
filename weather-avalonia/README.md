# weather-avalonia

The C# version of the weather app, built with [Avalonia UI](https://avaloniaui.net/). It shows the current weather and a 5-day forecast for a choice of cities, using live data from [Open-Meteo](https://open-meteo.com/). It needs an internet connection but no API key.

Open-Meteo's free tier is for non-commercial use and requires attribution, so check [its terms](https://open-meteo.com/en/terms) before using this in a commercial app.

Tested on macOS (Apple Silicon) only. Avalonia also targets Windows and Linux, but those haven't been tried.

## Setup

Run these from this folder. [mise](https://mise.jdx.dev/) installs the .NET SDK version pinned in `mise.toml`:

```sh
mise trust && mise install
```

The commands below use `mise exec --` so they pick up the pinned version. If mise is activated in your shell, you can leave that prefix off.

## Run

```sh
mise exec -- dotnet run --project src/WeatherApp
```

## Test

The tests run the real window without showing it on screen, using Avalonia's headless mode.

```sh
# All tests, including one that calls the live API
mise exec -- dotnet test WeatherApp.slnx

# Skip the live API test
mise exec -- dotnet test WeatherApp.slnx --filter "Category!=Live"
```

The live test saves what the window rendered to `tests/WeatherApp.Tests/bin/Debug/net10.0/weather-live.png`.

## Build for release

Each command targets Apple Silicon Macs. To target another system, change `osx-arm64`, for example to `win-x64` or `linux-x64`.

```sh
# Native AOT: smallest and fastest to start
mise exec -- dotnet publish src/WeatherApp -c Release -r osx-arm64 -p:PublishAot=true -o publish/aot

# Self-contained: includes the .NET runtime, with no compile-ahead step
mise exec -- dotnet publish src/WeatherApp -c Release -r osx-arm64 --self-contained true -o publish/self-contained
```

Run the result with `./publish/aot/WeatherApp`. Native AOT builds have to be compiled on the system they target. The `.dSYM` folder in the AOT output contains debug symbols and doesn't need to ship.

These commands produce plain executables. Packaging them as a signed `.app` bundle or an installer isn't set up.

## Code layout

| File | Purpose |
| --- | --- |
| `src/WeatherApp/WeatherClient.cs` | API call and JSON models |
| `src/WeatherApp/MainViewModel.cs` | App state and loading logic |
| `src/WeatherApp/MainWindow.axaml` | The UI |
| `tests/WeatherApp.Tests/WeatherTests.cs` | Tests |
