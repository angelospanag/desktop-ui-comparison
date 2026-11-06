# Benchmark results: 2026-11-06

Apple M3 Max, macOS 27.2. Measured with the same app code and method as `benchmarks/measure.py`, using earlier versions of its scripts from before the folders moved.

| Measure | Rust + GPUI | C# Avalonia (Native AOT) | C# Avalonia (self-contained) |
| --- | --- | --- | --- |
| Launch to window visible, median (ms) | 198 | 232 | 546 |
| City switch to rendered frame, median per run (ms) | 14.1–15.1 | 13.0–16.2 | 14.6–15.6 |
| City switch, p95 per run (ms) | 16.7–17.8 | 16.8–18.5 | 16.5–16.8 |
| Memory settled at 45 s, footprint (MB) | 37 | 55 | 109 |
| Highest memory seen in the first 45 s (MB) | 236 | 57 | 111 |
| CPU when idle (%) | 1.30–1.35 | 0.05 | 0.40–0.45 |
| Total CPU for launch plus 100 switches (s) | 0.8 | 1.4 | 3.4 |

| Build | Size (MB) |
| --- | --- |
| Rust + GPUI | 8.1 |
| Rust + GPUI, stripped | 6.3 |
| C# Native AOT | 39.4 |
| C# self-contained | 117.7 |
| C# self-contained, single compressed file | 49.4 |
| C# framework-dependent (needs .NET installed) | 31.1 |
