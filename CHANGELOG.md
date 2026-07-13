# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 1.19.3 - 2026-07-13
- Fix dimmable lights "forgetting" their state in HA: add a 20 s optimistic write-protection window in `DeviceStates` so the 10 s background poll no longer overwrites a value just set by an HA command
- Add `DeviceStates.storeUserState` (records `userSetAt` timestamp) used by `changeDeviceState`; `storeState` (poll path) now only writes `isOnCache`/`valueCache` when outside the optimistic window (`heatingCache` is always updated)
- Fire `homeassistant.update_entity` on `sensor.eaton_brightness` after every generated light `turn_on`/`turn_off`/`set_level` so HA re-reads the state immediately
- Lower generated `eaton_brightness` REST sensor `scan_interval` from 60 s to 10 s (matches the bridge's own poll cadence; the endpoint is cache-served so it never hits the Eaton controller)
- Add a "State & cache flow" mermaid diagram to the README and update the lights docs to the modern `template: - light:` format with `update_entity` and the 10 s interval

## 1.19.2 - 2026-07-09
- Allow to `stop` covers

## 1.19.1 - 2026-07-08
- Add `String.slugify` helper: lowercases and replaces any character outside `[a-z0-9_]` with `_`
- Apply `String.slugify` in `Device.effectiveId` so all generated HA YAML keys (`rest_command`, `light`, `cover`, `climate`, template-sensor `unique_id`) are valid HA slugs — eliminates `invalid slug` warnings in HA
- `state_attr(...)` lookups and `json_attributes:` entries are unchanged (still mixed-case `DeviceId.id`), so attribute resolution keeps working
- Migrate generated cover and light YAML from deprecated `platform: template` blocks to modern `template: - cover:` / `template: - light:` format with `default_entity_id`, `name`, and list-form actions — eliminates HA 2026.6 "Zastaralá šablona" deprecation warnings while preserving existing entity IDs
- Add UI note on covers and lights sections: all `template:` snippets must be merged under a single top-level `template:` list in `configuration.yaml`

## 1.19.0 - 2026-07-08
- Convert Entity Settings from a separate `/config` page to an inline modal on the index page — fixes opening settings via HA Ingress iframe (no navigation required, no absolute-URL 404)
- Replace `⚙ Entity Settings` link with a button that opens the modal (`openSettings()` / `closeSettings()` / backdrop click / `Esc`)
- Extract `settingsFormBody` shared helper to render the settings form on both the modal and the standalone `/config` fallback page
- Extract `saveConfigScript` with `configUrl()` — `fetch` target derived from `window.location.pathname` so it is ingress-relative
- Fix `GET /config` fallback page: change "← Back to index" from `href="/"` to `href="."` (ingress-relative)
- Pass `Settings` through `IndexParameters` so the modal is rendered fully server-side with no extra request

## 1.18.1 - 2026-07-08
- Fix unset option

## 1.18.0 - 2026-07-08
- Sort entity settings table by zone then Eaton name (U1)
- Add zone naming: local display labels for zone ids, persisted in `settings.json` under `Zones` (U2)
- Add entity id override: per-entity slug/`unique_id` override in the config page, applied to generated YAML entity names while keeping `state_attr` attribute keys unchanged (U3)
- Add `Device.effectiveId` helper: returns `IdOverride` when set, falls back to `DeviceId.id` (U3)
- Add table of contents on the index page with anchors for each non-empty section (U4)
- Replace Bootstrap 4 with Tailwind CSS (Play CDN); add dark/light theme toggle persisted in `localStorage` (U5)
- Add YAML syntax highlighting with Prism.js (language-yaml + line-numbers plugin); theme follows dark/light toggle (U6)
- Use net10.0
- Generate Yaml template for HA in up-to-date format
- Add cover as a specific type
- Add dimmer lights
- Add climate template
- Add `/health` endpoint (Feature 6): liveness check returning `Status` and `LastUpdated`
- Generate `binary_sensor.eaton_bridge` YAML snippet on the index page
- Fix `last_update` in `/sensors` to emit ISO-8601 timestamps (Feature 5)
- Generate `*_last_update` template sensors (device_class: timestamp) on the index page
- Add `--data-path` option (Feature P0): persistent data directory defaulting to `/data` (HA add-on volume) or `./data` locally
- Add `Store` module: atomic, thread-safe JSON load/save for all persisted state
- Derive `--cookies-path` and `--history-path` defaults from the data directory
- Extend `Config` with `DataConfig` carrying the resolved data directory
- Show version, data dir and port on web-server start (Feature A3)
- Expose `Version` and `Status` (ok/degraded) in `/health` response (Feature A3)
- Retry initial Eaton zone load with exponential backoff (5 s → 60 s) instead of crashing (Feature S1)
- Start HTTP server immediately so `/health` is reachable while Eaton is still unreachable (Feature S1)
- Add `EntitySetting` / `Settings` model persisted to `<data>/settings.json` (Feature C1)
- Add `GET /config` page: table of all entities with visibility toggles and display-name overrides (Feature C1)
- Add `POST /config` endpoint: saves updated settings to disk, reloads in memory (Feature C1)
- Apply settings on the index page: invisible entities are excluded and display-name overrides are used in all generated YAML (Feature C1)
- Add `Device.effectiveName` helper: returns `DisplayName` when set, falls back to `Name` (Feature C1)

## 1.17.1 - 2024-10-28
- Fix formatting float values

## 1.17.0 - 2024-10-28
- Add Heating Actuator support with sensors

## 1.16.0 - 2024-05-08
- Allow to trigger macro

## 1.15.0 - 2024-05-06
- Use net8.0

## 1.14.0 - 2023-08-11
- Use build project

## 1.13.0 - 2023-07-20
- Store current state after changing

## 1.12.0 - 2023-07-19
- Add cache for device states, update it internally and offers a cache in device state endpoint
- Add /states endpoint to show a current states

## 1.11.1 - 2023-07-19
- Read device state `open`

## 1.11.0 - 2023-07-19
- Show real sensors and switches on homepage
- Allow to trigger scene

## 1.10.0 - 2023-07-18
- Use net7.0

## 1.9.0 - 2022-11-22
- Allow to change device state (PoC)

## 1.8.0 - 2022-10-27
- Show index html
- Log http context details when run with debug verbosity

## 1.7.0 - 2022-10-27
- Change internal port to 28080

## 1.6.0 - 2022-10-27
- Update dependencies
- Prefer eaton configuration via options over `-c` option
- Debug http context in web server

## 1.5.2 - 2022-10-06
- Fix creating an Api

## 1.5.1 - 2022-10-06
- Fix creating an Uri

## 1.5.0 - 2022-10-06
- Fetch live stats from an Eaton controller (for selected devices ATM)

## 1.4.0 - 2022-10-03
- Add ingress index page for Web server

## 1.3.1 - 2022-10-03
- Fix `home:web:run` command option - remove unused `config`
- Fix `.config.dist.json` schema

## 1.3.0 - 2022-10-03
- Build for Raspberry Pi architecture (`alpine.3.16-arm64`)
- [**BC**] Remove `Google Sheets` integration
- Add Command for running a web server with sensors data (**currently dummy data**)
- Add logger
- Fix retry on unauthorized action

## 1.2.0 - 2022-09-12
- Add commands
    - `home:download:history`
    - `home:download:devices`

## 1.0.0 - 2022-05-05
- Initial implementation
