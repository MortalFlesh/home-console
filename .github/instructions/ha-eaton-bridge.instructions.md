---
applyTo: "src/**/*.fs"
---

# HA ↔ Eaton bridge — implementation notes

Repo-specific recipe and gotchas for extending the Home Assistant ↔ Eaton bridge.
Feature-level design lives in [plan.md](../../plan.md); general conventions live in
[AGENTS.md](../../AGENTS.md). This file captures the *how* so each entity type is added
consistently.

## Recipe: add a new HA-facing entity type

Follow these steps in order. Each Eaton "device" is a main device with `Children`; the
child devices (`..._u0`, `..._w`, …) are what map to HA entities.

1. **Predicate** — add `Device.isX` in [../../src/Eaton/Types.fs](../../src/Eaton/Types.fs)
   (`Device` module). If the new type overlaps an existing one, **exclude it from the
   others** (e.g. dimmers must be excluded from `isSwitch`, shutters from `isSwitch` for
   covers). Order matters in the `match`.

2. **State / read-back** — in the `DeviceStates` module in
   [../../src/Eaton/Api.fs](../../src/Eaton/Api.fs):
   - Add a cache with `create()` and store into it from `storeState` (called by both the
     poll loop `startLoadingState` and `changeDeviceState`).
   - Extend `CurrentState` if a new per-device value is needed; populate it in
     `getDeviceStates` (the raw value is `parsed.Value`; `decimalValue` is already parsed).
   - Expose `loadXState key` (scalar) and `allXStates ()` (aggregated), mirroring
     `loadIsOnState` / `allIsOnStates`.

3. **Endpoint(s)** — in [../../src/Core/WebServer/WebServer.fs](../../src/Core/WebServer/WebServer.fs):
   - Write an `Action<_>`: `IO -> Config -> HttpContext -> AsyncResult<'Response, ApiError>`.
   - Register it inside `run` under the `GET`/`POST` `choose [...]` lists, wired through
     `handleJsonAction` (JSON) or `handleHtmlAction` (HTML).
   - **Prefer an aggregated endpoint** (all devices in one response, keyed by device id —
     like `/sensors` and `/states`) so HA polls once for many entities. Add a scalar
     `/x/{zone}/{device}` variant only where HA needs a `state_resource`.

4. **YAML generator** — add `HaYaml.xLines currentHost devices` in
   [../../src/Core/WebServer/HaYaml.fs](../../src/Core/WebServer/HaYaml.fs). Iterate
   `device.Children`. Return `string list`.

5. **View row + wiring** — add `xRow` in
   [../../src/Core/WebServer/View.fs](../../src/Core/WebServer/View.fs) (mirror `coversRow`)
   and wire it into `index` after the existing rows.

6. **Build & lint** — run `./build.sh`; it runs FSharpLint and **fails the build on lint
   violations**. Fix them before considering the change done.

## Bidirectional (read-back) requirement

Every generated entity must reflect real device state, not just send commands — the Eaton
system can be operated physically or from the Eaton app. Switches already read back via
`state_resource` + `is_on_template`. New entity types must do the same via an aggregated
read-back sensor (see recipe step 3). The 10 s poll loop `DeviceStates.startLoadingState`
keeps the caches fresh. The only accepted exception is a value the Eaton API genuinely does
not report (e.g. possibly shutter position) — document such limitations in
[todo.md](../../todo.md).

## Gotchas

- **Two device-id forms.** `DeviceId` may be `xCo:9214125_u0` (control / short) or
  `hdm:xComfort Adapter:9214125_u0` (state read). Normalize with
  `DeviceId.shortId` / `ShortDeviceId`. Use `DeviceId.id` (`_`-replaced) for HA attribute
  keys, `ShortDeviceId.value` for the `device` field in request bodies.
- **`ZoneId` / `SceneId` / `DeviceId`** are single-case unions — unwrap with their
  `.value` helpers, don't pattern-match inline everywhere.
- **JSON-RPC calls** go through `RPC.Request.create` / `createWithoutParameters` →
  `RPC.call config`. Params are a boxed list wrapped in `Dto`. All API functions end with
  `|> retryOnUnauthorized io config` to survive session-cookie expiry.
- **`changeDeviceState`** already supports `Density`, `On`, `Off`, `Open`, `Close`; the
  parser (`ChangeDeviceState.parse`) currently accepts only `density`/`on`/`off`/`open`/
  `close`. Extend both the `DeviceState` union + `DeviceState.value` + the parser when
  adding a new command (e.g. `SetTemperature`).
- **`last_update` is not a real timestamp today** — the heating branch emits `HH:mm:ss`
  and the stat branch emits a `TimeSpan.ToString()`. Emit ISO-8601 (`DateTimeOffset` →
  `.ToString("o")`) if you want HA `device_class: timestamp` to work (Feature 5 in plan.md).
- **JSON provider schemas** live in `src/Eaton/schema/*.json`; adding a response field means
  updating the sample so the type provider picks it up.

## F# conventions (enforced by lint)

- `[<RequireQualifiedAccess>]` on modules meant to be called qualified.
- C#-style indexers `x[i]`, not `x.[i]`.
- No partial functions (`List.head`/`List.item` on possibly-empty lists).
- Async flows use the `asyncResult { }` computation expression (`Feather.ErrorHandling`).
- Namespaces: `MF.HomeConsole`, `MF.Eaton`, `MF.Utils`.
