# Benchmarks

Compares the Rust and C# weather apps on startup time, UI response, memory, CPU and size. The C# app is measured in two forms: Native AOT and self-contained.

## Results

Measured on 6 November 2026 on an Apple M3 Max running macOS 27.2.

| Measure | Rust + GPUI | C# Native AOT | C# self-contained |
| --- | --- | --- | --- |
| Launch to window visible, median | 198 ms | 232 ms | 546 ms |
| City switch to rendered frame, median per run | 14.1–15.1 ms | 13.0–16.2 ms | 14.6–15.6 ms |
| City switch, p95 per run | 16.7–17.8 ms | 16.8–18.5 ms | 16.5–16.8 ms |
| Memory settled at 45 s | 37 MB | 55 MB | 109 MB |
| CPU when idle | 1.30–1.35% | 0.05% | 0.40–0.45% |
| Total CPU for launch plus 100 switches | 0.8 s | 1.4 s | 3.4 s |
| Size on disk | 8.1 MB | 39.4 MB | 117.7 MB |

Other sizes: the stripped Rust binary is 6.3 MB. A self-contained C# build as a single compressed file is 49.4 MB, and a framework-dependent C# build is 31.1 MB but needs .NET installed.

### What the results show

- **Responsiveness is a tie.** Once running, all three builds show a new city in about one frame at 60 Hz (16.7 ms). The gaps are within the variation between runs.
- **Startup is the only delay a person would notice.** The self-contained C# build takes over half a second to show its window. Native AOT brings that close to Rust.
- **Rust uses the least memory and disk.** It settles at about two-thirds of the Native AOT build's memory and a third of the self-contained build's. Its size is about a fifth of Native AOT's.
- **Rust uses the most CPU when idle.** All three are low, but GPUI uses about 3 times as much as self-contained C# and over 25 times as much as Native AOT. Native AOT is the lowest overall.
- **Self-contained C# does the most work to get going.** Its total CPU is about 4 times Rust's, and this total includes launch. Native AOT cuts it to under twice Rust's.

### Choosing between them

For an app of this size, Native AOT C# gives up little in practice compared with Rust, and Avalonia adds ready-made controls and headless UI testing. Rust and GPUI make more sense when download size and memory use matter most. Self-contained C# is the simplest to build for release, but it has the slowest startup and the largest output.

These results don't compare development effort or how each framework behaves with a larger UI.

### How far to trust these numbers

- These measurements came from earlier versions of the scripts here, using the same app code and method, before the project's folders were reorganized. `measure.py` itself has not yet completed a full run, so the first run may need fixes.
- The "Highest memory" row in [`results/latest.md`](results/latest.md) isn't a reliable comparison, so it's left out above. Graphics memory briefly rises by about 200 MB in all three builds, and the sampling only caught that for Rust. See [Limits](#limits).
- `results/latest.json` holds the samples behind this table, except that launch time has only the median of 7 launches.

## Dependency sizes

Measured by hand on 6 November 2026, on Apple Silicon. `measure.py` doesn't produce these numbers. What ships is far smaller than what development needs on disk.

| | Rust + GPUI | C# Avalonia |
| --- | --- | --- |
| Direct dependencies | 3 crates (`gpui`, `serde`, `ureq`) | 5 NuGet packages |
| All dependencies | 431 crates at runtime, 459 with build-only crates | 27 packages |
| Downloaded dependencies | 205 MB of source | 1,225 MB of packages |
| Toolchain | 1.4 GB | 1.6 GB (mise's .NET folder) |
| Build output | 2.4 GB for a clean debug build, 3.6 GB with release added | 1.5 GB in the current `bin` and `obj` folders |
| Shipped app | 8.1 MB | 39.4 MB (Native AOT) or 117.7 MB (self-contained) |

Notes on these figures:

- **Rust dependency count:** `Cargo.lock` lists 725 packages because it covers every platform. Only the numbers above get compiled on a Mac.
- **Rust build output:** the cache holds compiled copies of every crate, with debug info in debug builds. Each profile keeps its own copy, and running `cargo test` or `cargo clippy` adds more. The largest single file seen in a debug build was the 168 MB compiled `gpui` library. It grows until you run `cargo clean`: this project's folder reached 5.1 GB.
- **C# downloads:** at least 1,086 MB of the total is native libraries for Windows, Linux and WebAssembly, which NuGet downloads even though a Mac build doesn't ship them. SkiaSharp's Windows package alone is 365 MB.
- **C# build output:** this isn't a clean-build figure. The folders include several publish variants and the test project.
- **Shared space:** the toolchains and download caches are shared with other projects on the machine that use the same versions.

### What's inside the shipped app

The Rust binary links everything statically into one file, so it has no separate dependency files to measure.

| Part of the C# app | Native AOT | Self-contained |
| --- | --- | --- |
| App executable | 19.8 MB, with the .NET runtime and Avalonia's .NET code compiled in | 0.2 MB |
| .NET runtime | Included above | 86.6 MB |
| Avalonia's .NET libraries | Included above | 11.1 MB |
| CommunityToolkit.Mvvm | Included above | 0.1 MB |
| Skia, native graphics library | 15.2 MB | 15.2 MB |
| HarfBuzz, native text shaping library | 2.9 MB | 2.9 MB |
| Avalonia's native macOS library | 1.5 MB | 1.5 MB |
| **Total** | **39.4 MB** | **117.7 MB** |

Native AOT saves space mainly by compiling in only the runtime code the app uses. Skia alone is almost twice the size of the entire Rust binary, because GPUI draws directly with Metal and doesn't ship a separate graphics library like Skia.

## Run

Run this from the repository root. It needs macOS, Xcode, Python 3 and mise, with both apps set up as their READMEs describe.

```sh
python3 benchmarks/measure.py
```

It takes about 10 minutes, including the builds. Windows open and close repeatedly and may take focus, so avoid covering them, because hidden windows can slow frame rendering. Close other heavy apps for steadier results.

To reuse the builds from the last run, add `--skip-build`. Don't use it after changing either app.

## Output

| Path | Contents |
| --- | --- |
| `results/latest.md` | Summary tables |
| `results/latest.json` | Every sample |
| `.work/` | Builds and temporary files, safe to delete |

Each run overwrites the previous files in `results/`. It doesn't update the [Results](#results) section above, so copy over any new numbers by hand.

## What's measured

| Measure | Method | Build |
| --- | --- | --- |
| Launch to window visible | Time from launch until macOS lists the window as on screen, using `harness/winwait.swift`. The first of 8 rounds is dropped to warm the disk cache. | Unmodified app |
| Memory | The macOS `footprint` tool, sampled 2, 5, 8, 28 and 45 seconds after launch, in 2 runs | Unmodified app |
| Idle CPU | CPU time used between 8 and 28 seconds after launch | Unmodified app |
| City switch to rendered frame | Time from changing the city until the frame after the new data is drawn, over 100 switches in each of 3 runs, against the local stub server in `harness/stub.py` | Self-driving copy |
| Total CPU | CPU time at the end of a self-driving run, which includes launch | Self-driving copy |
| Size | Total size of the files that ship, leaving out debug symbols | Unmodified app |

The self-driving copies exist only in `.work/`. `measure.py` creates them from the apps' current code using `harness/patch_gpui.py` and the files in `harness/avalonia/`. If the GPUI app's code changes in a way the patch doesn't expect, the build stops with an error saying the patch needs updating.

## Limits

- **No real input:** the self-driving copies change the city directly, so the results leave out mouse input and real network time.
- **Small differences:** GPUI and Avalonia expose different frame hooks, and results are tied to the display's refresh rate. Treat small gaps in the switching numbers as a tie.
- **Startup:** it stops at the window appearing, not at the forecast showing, and the unmodified apps call the real API during this step. The GPUI app compiles its shaders at launch because of its `runtime_shaders` feature.
- **Memory:** graphics memory can briefly rise by about 200 MB during rendering and then drop, so compare settled values and treat peaks as approximate.
- **Scope:** results come from one machine and a very small app, so they may not hold for larger apps.
