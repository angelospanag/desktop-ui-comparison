"""Turns a copy of the GPUI app's main.rs into a version that switches cities by itself.

Usage: python3 patch_gpui.py <path to the copy of main.rs>
"""

import sys

path = sys.argv[1]
source = open(path).read()


def replace(old, new):
    global source
    assert old in source, f"The app's code has changed, so this patch needs updating:\n{old}"
    source = source.replace(old, new, 1)


replace("https://api.open-meteo.com/v1/forecast", "http://127.0.0.1:8765/v1/forecast")

replace(
    "    fetch: Option<Task<()>>,\n}",
    "    fetch: Option<Task<()>>,\n"
    "    bench_start: Option<std::time::Instant>,\n"
    "    bench_samples: Vec<f64>,\n}",
)

replace(
    "            fetch: None,\n",
    "            fetch: None,\n"
    "            bench_start: Some(std::time::Instant::now()),\n"
    "            bench_samples: Vec::new(),\n",
)

replace(
    "    fn render_body(&self)",
    """    fn bench_step(&mut self, cx: &mut Context<Self>) {
        self.bench_start = Some(std::time::Instant::now());
        let next = (self.selected + 1) % CITIES.len();
        self.load(next, cx);
    }

    fn bench_record(&mut self, ms: f64, cx: &mut Context<Self>) {
        self.bench_samples.push(ms);
        // One initial load plus 100 switches.
        if self.bench_samples.len() == 101 {
            let lines: Vec<String> = self.bench_samples.iter().map(|v| v.to_string()).collect();
            std::fs::write(std::env::var("BENCH_OUT").unwrap(), lines.join("\\n")).unwrap();
            return;
        }
        cx.spawn(async move |this, cx| {
            cx.background_executor().timer(Duration::from_millis(50)).await;
            this.update(cx, |this, cx| this.bench_step(cx)).ok();
        })
        .detach();
    }

    fn render_body(&self)""",
)

replace(
    "fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {\n",
    """fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        if matches!(self.state, LoadState::Loaded(_)) {
            if let Some(start) = self.bench_start.take() {
                let entity = cx.entity();
                window.on_next_frame(move |_, cx| {
                    let ms = start.elapsed().as_secs_f64() * 1000.0;
                    entity.update(cx, |this, cx| this.bench_record(ms, cx));
                });
            }
        }
""",
)

open(path, "w").write(source)
