//! Live weather from the Open-Meteo API (free, no API key needed).
//!
//! Shows the usual pattern for loading data in GPUI: do the slow work on a background
//! thread, then update the view's state on the main thread and call `cx.notify()`.

use std::time::Duration;

use gpui::{
    AnyElement, App, Application, Bounds, Context, SharedString, Task, TitlebarOptions, Window,
    WindowBounds, WindowOptions, div, prelude::*, px, rgb, size,
};
use serde::Deserialize;

const API_URL: &str = "https://api.open-meteo.com/v1/forecast";

struct City {
    name: &'static str,
    latitude: f64,
    longitude: f64,
}

const CITIES: [City; 5] = [
    City {
        name: "Athens",
        latitude: 37.98,
        longitude: 23.73,
    },
    City {
        name: "London",
        latitude: 51.51,
        longitude: -0.13,
    },
    City {
        name: "New York",
        latitude: 40.71,
        longitude: -74.01,
    },
    City {
        name: "Tokyo",
        latitude: 35.68,
        longitude: 139.69,
    },
    City {
        name: "Sydney",
        latitude: -33.87,
        longitude: 151.21,
    },
];

// These structs mirror the parts of the API response that the app uses.
#[derive(Debug, Deserialize)]
struct Forecast {
    current: Current,
    daily: Daily,
}

#[derive(Debug, Deserialize)]
struct Current {
    temperature_2m: f32,
    apparent_temperature: f32,
    relative_humidity_2m: f32,
    wind_speed_10m: f32,
    weather_code: u8,
}

// The API returns daily values as parallel arrays: index N in each one is the same day.
#[derive(Debug, Deserialize)]
struct Daily {
    time: Vec<String>,
    weather_code: Vec<u8>,
    temperature_2m_max: Vec<f32>,
    temperature_2m_min: Vec<f32>,
}

/// Blocks until the request finishes, so this must not run on the UI thread.
fn fetch_forecast(latitude: f64, longitude: f64) -> Result<Forecast, ureq::Error> {
    let agent: ureq::Agent = ureq::Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(10)))
        .build()
        .into();

    agent
        .get(API_URL)
        .query("latitude", latitude.to_string())
        .query("longitude", longitude.to_string())
        .query(
            "current",
            "temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code",
        )
        .query(
            "daily",
            "weather_code,temperature_2m_max,temperature_2m_min",
        )
        .query("timezone", "auto")
        .query("forecast_days", "5")
        .call()?
        .body_mut()
        .read_json()
}

/// Translates the WMO weather codes that Open-Meteo returns.
fn describe(code: u8) -> &'static str {
    match code {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 | 48 => "Fog",
        51..=57 => "Drizzle",
        61..=67 => "Rain",
        71..=77 => "Snow",
        80..=82 => "Rain showers",
        85 | 86 => "Snow showers",
        95..=99 => "Thunderstorm",
        _ => "Unknown",
    }
}

enum LoadState {
    Loading,
    Loaded(Forecast),
    Failed(SharedString),
}

struct WeatherApp {
    selected: usize,
    state: LoadState,
    // Dropping a GPUI task cancels it. Replacing this field when the user picks another
    // city means a slow response for the previous city can't overwrite the new one.
    fetch: Option<Task<()>>,
}

impl WeatherApp {
    fn new(cx: &mut Context<Self>) -> Self {
        let mut app = Self {
            selected: 0,
            state: LoadState::Loading,
            fetch: None,
        };
        app.load(0, cx);
        app
    }

    fn load(&mut self, index: usize, cx: &mut Context<Self>) {
        self.selected = index;
        self.state = LoadState::Loading;
        cx.notify();

        let City {
            latitude,
            longitude,
            ..
        } = CITIES[index];

        // Runs on a background thread, so the window stays responsive during the request.
        let request = cx
            .background_executor()
            .spawn(async move { fetch_forecast(latitude, longitude) });

        // Runs on the main thread. It waits for the request, then updates the view.
        // `this` is a weak handle because the window may be gone by the time it finishes.
        self.fetch = Some(cx.spawn(async move |this, cx| {
            let result = request.await;
            this.update(cx, |this, cx| {
                this.state = match result {
                    Ok(forecast) => LoadState::Loaded(forecast),
                    Err(error) => LoadState::Failed(error.to_string().into()),
                };
                cx.notify();
            })
            .ok();
        }));
    }

    fn render_body(&self) -> AnyElement {
        match &self.state {
            LoadState::Loading => message("Loading…").into_any_element(),
            LoadState::Failed(error) => div()
                .flex()
                .flex_col()
                .gap_1()
                .child(
                    div()
                        .text_color(rgb(0xf38ba8))
                        .child("Couldn't load the forecast"),
                )
                .child(div().text_sm().child(error.clone()))
                .child(
                    div()
                        .text_sm()
                        .opacity(0.7)
                        .child("Select a city to try again."),
                )
                .into_any_element(),
            LoadState::Loaded(forecast) => render_forecast(forecast).into_any_element(),
        }
    }
}

impl Render for WeatherApp {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let city_buttons = CITIES.iter().enumerate().map(|(index, city)| {
            div()
                .id(index)
                .px_3()
                .py_1()
                .rounded_lg()
                .text_sm()
                .cursor_pointer()
                .bg(rgb(0x313244))
                .hover(|style| style.bg(rgb(0x45475a)))
                .when(index == self.selected, |this| {
                    this.bg(rgb(0x89b4fa)).text_color(rgb(0x1e1e2e))
                })
                .child(city.name)
                .on_click(cx.listener(move |this, _, _, cx| this.load(index, cx)))
        });

        div()
            .size_full()
            .flex()
            .flex_col()
            .gap_4()
            .p_5()
            .bg(rgb(0x1e1e2e))
            .text_color(rgb(0xcdd6f4))
            .child(div().flex().flex_wrap().gap_2().children(city_buttons))
            .child(div().flex_1().child(self.render_body()))
            // Open-Meteo's free tier requires attribution.
            .child(
                div()
                    .text_xs()
                    .opacity(0.6)
                    .child("Weather data by Open-Meteo.com"),
            )
    }
}

fn render_forecast(forecast: &Forecast) -> impl IntoElement {
    let current = &forecast.current;
    let daily = &forecast.daily;

    let days = (0..daily.time.len()).map(|day| {
        div()
            .flex()
            .justify_between()
            .gap_4()
            .py_1()
            .text_sm()
            .child(div().w_24().child(daily.time[day].clone()))
            .child(div().flex_1().child(describe(daily.weather_code[day])))
            .child(format!(
                "{:.0}° / {:.0}°",
                daily.temperature_2m_min[day], daily.temperature_2m_max[day]
            ))
    });

    div()
        .flex()
        .flex_col()
        .gap_4()
        .child(
            div()
                .flex()
                .flex_col()
                .gap_1()
                .child(
                    div()
                        .text_3xl()
                        .font_weight(gpui::FontWeight::BOLD)
                        .child(format!("{:.1}°C", current.temperature_2m)),
                )
                .child(describe(current.weather_code))
                .child(div().text_sm().opacity(0.7).child(format!(
                    "Feels like {:.1}°C · Humidity {:.0}% · Wind {:.1} km/h",
                    current.apparent_temperature,
                    current.relative_humidity_2m,
                    current.wind_speed_10m
                ))),
        )
        .child(div().flex().flex_col().children(days))
}

fn message(text: &'static str) -> impl IntoElement {
    div().opacity(0.7).child(text)
}

fn main() {
    Application::new().run(|cx: &mut App| {
        let bounds = Bounds::centered(None, size(px(480.), px(420.)), cx);

        cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                titlebar: Some(TitlebarOptions {
                    title: Some("GPUI Weather".into()),
                    ..Default::default()
                }),
                ..Default::default()
            },
            |_window, cx| cx.new(WeatherApp::new),
        )
        .expect("failed to open window");

        cx.on_window_closed(|cx| {
            if cx.windows().is_empty() {
                cx.quit();
            }
        })
        .detach();

        cx.activate(true);
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_api_response() {
        // Trimmed from a real Open-Meteo response.
        let json = r#"{
            "latitude": 37.96,
            "current": {
                "time": "2026-11-06T08:00",
                "temperature_2m": 10.2,
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
        }"#;

        let forecast: Forecast = serde_json::from_str(json).unwrap();

        assert_eq!(forecast.current.temperature_2m, 10.2);
        assert_eq!(forecast.daily.time.len(), 2);
        assert_eq!(describe(forecast.daily.weather_code[1]), "Rain");
    }

    #[test]
    #[ignore = "calls the live API"]
    fn fetches_live_forecast() {
        let forecast = fetch_forecast(CITIES[0].latitude, CITIES[0].longitude).unwrap();

        assert_eq!(forecast.daily.time.len(), 5);
        assert_eq!(forecast.daily.weather_code.len(), 5);
        assert_eq!(forecast.daily.temperature_2m_max.len(), 5);
        assert_eq!(forecast.daily.temperature_2m_min.len(), 5);
    }
}
