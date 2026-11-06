#!/usr/bin/env python3
"""Builds both weather apps, measures them, and writes a report to results/.

Usage: python3 benchmarks/measure.py [--skip-build]

Opens and closes windows for several minutes. macOS only.
"""

import argparse
import json
import os
import platform
import re
import shutil
import statistics
import subprocess
import time
from datetime import date
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
GPUI = ROOT / "weather-gpui"
AVALONIA = ROOT / "weather-avalonia"
HARNESS = HERE / "harness"
WORK = HERE / ".work"
RESULTS = HERE / "results"
PUBLISH = WORK / "publish"

LABELS = {
    "gpui": "Rust + GPUI",
    "avalonia-aot": "C# Avalonia (Native AOT)",
    "avalonia-jit": "C# Avalonia (self-contained)",
}

# The unmodified apps, used for startup, memory and idle CPU. They call the real API.
APPS = {
    "gpui": GPUI / "target/release/weather-gpui",
    "avalonia-aot": PUBLISH / "aot/WeatherApp",
    "avalonia-jit": PUBLISH / "self-contained/WeatherApp",
}

# Copies that switch cities by themselves against the local stub server.
SELF_DRIVING = {
    "gpui": WORK / "gpui/target/release/weather-gpui",
    "avalonia-aot": WORK / "self-driving/aot/WeatherApp",
    "avalonia-jit": WORK / "self-driving/self-contained/WeatherApp",
}

STARTUP_ROUNDS = 8  # The first round warms the disk cache and is dropped.
TIMELINE_RUNS = 2
TIMELINE_SECONDS = (2, 5, 8, 28, 45)
IDLE_FROM, IDLE_TO = 8, 28
SWITCH_RUNS = 3
SWITCH_SAMPLES = 100
SWITCH_TIMEOUT_SECONDS = 90


def run(command, cwd):
    subprocess.run(command, cwd=cwd, check=True, capture_output=True, text=True)


def run_with_output(command, cwd=None):
    return subprocess.run(command, cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


def dotnet_publish(project, output, *flags):
    run(
        ["mise", "exec", "--", "dotnet", "publish", str(project), "-c", "Release",
         "-r", "osx-arm64", "-o", str(output), *flags],
        AVALONIA,
    )


def build():
    print("Building...", flush=True)
    shutil.rmtree(WORK, ignore_errors=True)
    WORK.mkdir()
    app_project = AVALONIA / "src/WeatherApp"

    run(["swiftc", "-O", "-o", str(WORK / "winwait"), str(HARNESS / "winwait.swift")], HERE)

    # Unmodified apps
    run(["mise", "exec", "--", "cargo", "build", "--release"], GPUI)
    dotnet_publish(app_project, PUBLISH / "framework-dependent", "--self-contained", "false")
    dotnet_publish(app_project, PUBLISH / "self-contained", "--self-contained", "true")
    dotnet_publish(
        app_project, PUBLISH / "single-file", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
    )
    dotnet_publish(app_project, PUBLISH / "aot", "-p:PublishAot=true")

    # Self-driving GPUI copy. The patch fails loudly if the app's code has moved on.
    copy = WORK / "gpui"
    (copy / "src").mkdir(parents=True)
    shutil.copy(GPUI / "Cargo.toml", copy)
    shutil.copy(GPUI / "Cargo.lock", copy)
    shutil.copy(GPUI / "src/main.rs", copy / "src")
    run(["python3", str(HARNESS / "patch_gpui.py"), str(copy / "src/main.rs")], HERE)
    run(["mise", "exec", "--", "cargo", "build", "--release", "--manifest-path", str(copy / "Cargo.toml")], GPUI)

    # Self-driving Avalonia copies
    copy = WORK / "avalonia"
    shutil.copytree(app_project, copy, ignore=shutil.ignore_patterns("bin", "obj"))
    shutil.copy(HARNESS / "avalonia/App.axaml.cs", copy)
    shutil.copy(HARNESS / "avalonia/Bench.cs", copy)
    client = copy / "WeatherClient.cs"
    source = client.read_text()
    assert "https://api.open-meteo.com" in source
    client.write_text(source.replace("https://api.open-meteo.com", "http://127.0.0.1:8765"))
    dotnet_publish(copy, WORK / "self-driving/self-contained", "--self-contained", "true")
    shutil.rmtree(copy / "bin")
    shutil.rmtree(copy / "obj")
    dotnet_publish(copy, WORK / "self-driving/aot", "-p:PublishAot=true")


def directory_size(path):
    # Debug symbols don't ship, so they aren't counted.
    return sum(
        file.stat().st_size
        for file in path.rglob("*")
        if file.is_file() and not any(part.endswith((".dSYM", ".pdb")) for part in file.parts)
    )


def measure_sizes():
    stripped = WORK / "weather-gpui-stripped"
    shutil.copy(APPS["gpui"], stripped)
    run(["strip", str(stripped)], WORK)
    return {
        "Rust + GPUI": APPS["gpui"].stat().st_size,
        "Rust + GPUI, stripped": stripped.stat().st_size,
        "C# Native AOT": directory_size(PUBLISH / "aot"),
        "C# self-contained": directory_size(PUBLISH / "self-contained"),
        "C# self-contained, single compressed file": directory_size(PUBLISH / "single-file"),
        "C# framework-dependent (needs .NET installed)": directory_size(PUBLISH / "framework-dependent"),
    }


def to_mb(text):
    value, unit = text.split()
    return float(value) * {"B": 1 / 1024**2, "KB": 1 / 1024, "MB": 1, "GB": 1024}[unit]


def memory(pid):
    output = subprocess.run(["footprint", str(pid)], capture_output=True, text=True).stdout
    total = re.search(r"phys_footprint: ([\d.]+ \w+)", output)
    graphics = re.search(
        r"^\s*([\d.]+ (?:B|KB|MB|GB))\s.*physical footprint \(unmapped\) \(graphics\)",
        output, re.MULTILINE | re.IGNORECASE,
    )
    return {"footprint_mb": to_mb(total[1]), "graphics_mb": to_mb(graphics[1]) if graphics else 0}


def cpu_seconds(pid):
    parts = run_with_output(["ps", "-o", "cputime=", "-p", str(pid)]).split(":")
    return sum(float(part) * 60**index for index, part in enumerate(reversed(parts)))


def stop(process):
    process.terminate()
    process.wait()
    time.sleep(0.5)


def measure_startup():
    samples = {name: [] for name in APPS}
    for round_number in range(STARTUP_ROUNDS):
        for name, app in APPS.items():
            elapsed = float(run_with_output([str(WORK / "winwait"), str(app)]))
            assert elapsed > 0, f"{name}: no window appeared"
            if round_number > 0:
                samples[name].append(elapsed)
            time.sleep(0.5)
    print("Startup done", flush=True)
    return samples


def measure_timeline(app):
    runs, idle = [], []
    for _ in range(TIMELINE_RUNS):
        process = subprocess.Popen([str(app)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        started = time.time()
        samples = {}
        for second in TIMELINE_SECONDS:
            time.sleep(max(0, started + second - time.time()))
            samples[second] = memory(process.pid)
            if second == IDLE_FROM:
                idle_start = cpu_seconds(process.pid)
            if second == IDLE_TO:
                idle.append((cpu_seconds(process.pid) - idle_start) / (IDLE_TO - IDLE_FROM) * 100)
        runs.append(samples)
        stop(process)
    return runs, idle


def measure_switching():
    results = {name: [] for name in SELF_DRIVING}
    stub = subprocess.Popen(["python3", str(HARNESS / "stub.py")])
    time.sleep(1)
    try:
        for run_number in range(SWITCH_RUNS):
            for name, app in SELF_DRIVING.items():
                results[name].append(measure_one_switch_run(name, app, run_number))
            print(f"Switching round {run_number + 1}/{SWITCH_RUNS} done", flush=True)
    finally:
        stub.terminate()
    return results


def measure_one_switch_run(name, app, run_number):
    out = WORK / f"switch-{name}-{run_number}.txt"
    out.unlink(missing_ok=True)
    process = subprocess.Popen(
        [str(app)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        env={**os.environ, "BENCH_OUT": str(out)},
    )
    started = time.time()
    while not out.exists():
        if process.poll() is not None or time.time() - started > SWITCH_TIMEOUT_SECONDS:
            if process.poll() is None:
                stop(process)
            raise RuntimeError(f"{name}: self-driving run produced no results")
        time.sleep(0.2)
    time.sleep(0.2)  # Lets the write finish.
    total_cpu = cpu_seconds(process.pid)
    stop(process)

    # The first sample is the initial load, which each app times from a different point.
    samples = sorted(float(value) for value in out.read_text().split()[1:])
    assert len(samples) == SWITCH_SAMPLES, f"{name}: got {len(samples)} samples"
    return {
        "median_ms": statistics.median(samples),
        "p95_ms": samples[int(len(samples) * 0.95) - 1],
        "max_ms": samples[-1],
        "cpu_seconds_total": total_cpu,
    }


def span(values, digits=0):
    low, high = min(values), max(values)
    if round(low, digits) == round(high, digits):
        return f"{low:.{digits}f}"
    return f"{low:.{digits}f}–{high:.{digits}f}"


def report(data):
    names = list(LABELS)
    def row(title, cell):
        return f"| {title} | " + " | ".join(cell(data["apps"][name]) for name in names) + " |"

    lines = [
        f"# Benchmark results: {data['date']}",
        "",
        f"{data['machine']}, macOS {data['macos']}. Generated by `benchmarks/measure.py`.",
        "",
        "| Measure | " + " | ".join(LABELS[name] for name in names) + " |",
        "| --- |" + " --- |" * len(names),
        row("Launch to window visible, median (ms)", lambda a: f"{statistics.median(a['startup_ms']):.0f}"),
        row("City switch to rendered frame, median per run (ms)", lambda a: span([r["median_ms"] for r in a["switching"]], 1)),
        row("City switch, p95 per run (ms)", lambda a: span([r["p95_ms"] for r in a["switching"]], 1)),
        row("Memory settled at 45 s, footprint (MB)", lambda a: span([r[45]["footprint_mb"] for r in a["timeline"]])),
        row("Highest memory seen in the first 45 s (MB)", lambda a: f"{max(s['footprint_mb'] for r in a['timeline'] for s in r.values()):.0f}"),
        row("CPU when idle (%)", lambda a: span(a["idle_cpu_percent"], 2)),
        row("Total CPU for launch plus 100 switches (s)", lambda a: span([r["cpu_seconds_total"] for r in a["switching"]], 1)),
        "",
        "| Build | Size (MB) |",
        "| --- | --- |",
        *[f"| {name} | {size / 1e6:.1f} |" for name, size in data["sizes"].items()],
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-build", action="store_true", help="reuse the builds from the last run")
    args = parser.parse_args()

    if not args.skip_build:
        build()

    startup = measure_startup()
    apps = {}
    for name, app in APPS.items():
        timeline, idle = measure_timeline(app)
        apps[name] = {"startup_ms": startup[name], "timeline": timeline, "idle_cpu_percent": idle}
        print(f"{LABELS[name]}: memory and idle CPU done", flush=True)
    for name, runs in measure_switching().items():
        apps[name]["switching"] = runs

    data = {
        "date": date.today().isoformat(),
        "machine": run_with_output(["sysctl", "-n", "machdep.cpu.brand_string"]),
        "macos": platform.mac_ver()[0],
        "sizes": measure_sizes(),
        "apps": apps,
    }

    RESULTS.mkdir(exist_ok=True)
    (RESULTS / "latest.json").write_text(json.dumps(data, indent=1))
    (RESULTS / "latest.md").write_text(report(data))
    print(report(data))


if __name__ == "__main__":
    main()
