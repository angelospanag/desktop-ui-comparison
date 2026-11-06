# Desktop weather app comparison

The same small weather app built twice, to compare two ways of writing a native desktop UI. It shows the current weather and a 5-day forecast for a choice of cities, using live data from [Open-Meteo](https://open-meteo.com/).

| Folder | Contents |
| --- | --- |
| [`weather-gpui/`](weather-gpui/README.md) | Rust with [GPUI](https://www.gpui.rs/) |
| [`weather-avalonia/`](weather-avalonia/README.md) | C# with [Avalonia UI](https://avaloniaui.net/) |
| [`benchmarks/`](benchmarks/README.md) | Builds and measures both apps |

## Results in brief

Both apps respond equally fast once running. The C# figures below use Native AOT, the better of the two C# builds measured.

| Measure | Rust + GPUI | C# Native AOT |
| --- | --- | --- |
| Launch to window visible | 198 ms | 232 ms |
| City switch to rendered frame | 14–15 ms | 13–16 ms |
| Memory once settled | 37 MB | 55 MB |
| CPU when idle | 1.3% | 0.05% |
| Size on disk | 8.1 MB | 39.4 MB |

These come from one machine and a very small app. The [full results](benchmarks/README.md#results) include the self-contained C# build and explain the limits of these numbers.

## Getting started

Each app pins its own toolchain with [mise](https://mise.jdx.dev/), so run its commands from inside its folder. Each app's README covers how to run, test and build it.

To run the benchmarks, from this folder:

```sh
python3 benchmarks/measure.py
```

Everything has been tested on macOS (Apple Silicon) only.
