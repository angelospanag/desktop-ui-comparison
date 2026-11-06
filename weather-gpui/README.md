# weather-gpui

The Rust version of the weather app, built with [GPUI](https://www.gpui.rs/), the UI framework behind the Zed editor. It shows the current weather and a 5-day forecast for a choice of cities, using live data from [Open-Meteo](https://open-meteo.com/). It needs an internet connection but no API key.

Open-Meteo's free tier is for non-commercial use and requires attribution, so check [its terms](https://open-meteo.com/en/terms) before using this in a commercial app.

## Prerequisites

Tested on macOS (Apple Silicon) only.

- [mise](https://mise.jdx.dev/), which installs the Rust version pinned in `mise.toml`
- Xcode, for the macOS SDK that GPUI builds against

## Setup

Run commands from this folder.

```sh
mise trust && mise install
```

The commands below use `mise exec --` so they pick up the pinned Rust version. If [mise is activated in your shell](https://mise.jdx.dev/getting-started.html#activate-mise), you can leave that prefix off.

## Run

```sh
mise exec -- cargo run
```

The first build compiles all of GPUI's dependencies and takes a while; later builds are much faster. Closing the window quits the app.

There is no hot reload. To see a code change, close the app and run the command again.

## Develop

The app is a single file, `src/main.rs`.

| Task | Command |
| --- | --- |
| Check that the code compiles | `mise exec -- cargo check` |
| Format the code | `mise exec -- cargo fmt` |
| Check formatting only | `mise exec -- cargo fmt --check` |
| Lint | `mise exec -- cargo clippy --all-targets -- -D warnings` |
| Run tests | `mise exec -- cargo test` |

`cargo test` skips the test that calls the live API. To include it:

```sh
mise exec -- cargo test -- --include-ignored
```

GPUI's API changes between versions. For working code that matches the version in use, see the `examples/` directory of the downloaded crate:

```sh
ls ~/.cargo/registry/src/*/gpui-0.2.2/examples
```

## Build

```sh
# Debug build: target/debug/weather-gpui
mise exec -- cargo build

# Optimized build: target/release/weather-gpui
mise exec -- cargo build --release
```

Run the optimized build directly:

```sh
./target/release/weather-gpui
```

This produces a plain executable. Packaging it as a signed `.app` bundle for distribution isn't set up.

## Shaders

GPUI's `runtime_shaders` feature is enabled in `Cargo.toml`, so Metal shaders are compiled when the app launches rather than at build time. This means the build doesn't need Xcode's Metal Toolchain.

To precompile shaders instead:

1. Install the toolchain:

   ```sh
   xcodebuild -downloadComponent MetalToolchain
   ```

2. Remove `runtime_shaders` from the `gpui` dependency in `Cargo.toml`.

If you remove the feature without installing the toolchain, the build fails with `cannot execute tool 'metal' due to missing Metal Toolchain`.

## Upgrading Rust

```sh
mise latest rust
mise use rust@<version>
```

This updates the version pinned in `mise.toml`.
