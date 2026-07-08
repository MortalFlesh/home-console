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

## Web GUI routing (Home Assistant Ingress)

The GUI ([src/Core/WebServer/View.fs](../../src/Core/WebServer/View.fs)) is served **inside
an HA Ingress `<iframe>`** under a path prefix like `/api/hassio_ingress/<token>/`. Absolute
URLs (leading `/`) escape that prefix and break. Follow these rules:

- **Never navigate between GUI screens with a route/link.** Secondary screens (settings,
  editors, detail views) **must be rendered as modals** on the page that opens them —
  render the modal body server-side into the parent page (the data is already loaded) and
  toggle it with `classList.add/remove('hidden')`. No page load = no ingress prefix problem.
  Provide close via a button, backdrop click, and `Esc`.
- **Entry points are buttons, not `<a href>`.** Open a modal with
  `button [ _onclick "openX()" ]`, not `a [ _href "/x" ]`.
- **All in-app URLs must be ingress-relative** (no leading `/`). For `fetch`, derive the
  path from `window.location.pathname` (see `configUrl()` in `View.fs`), e.g. strip a
  trailing screen segment + slash then append the endpoint. Use `_href "."` for any
  unavoidable "back to index" link.
- **Exception — generated HA YAML URLs stay absolute.** The `resource:` / `state_resource:`
  values built from `CurrentHost` (`host:port`) are called **server-to-server by Home
  Assistant**, not through the browser/ingress — leave them as full `http://host:port/...`.
- Keep any `GET` HTML route (e.g. `/config`) as a **non-ingress fallback** only; the modal
  is the primary UX. If kept, fix its links to be relative too.

Reference pattern (extract the screen body into a reusable `XmlNode list`, render it into a
hidden overlay on the parent page, and toggle with JS):

```fsharp
// entry point — button, not an <a href>
button [ _class "..."; _onclick "openSettings()" ] [ str "⚙ Entity Settings" ]

// modal overlay, hidden by default, rendered server-side into the parent page
div [ _id "settingsModal"; _class "hidden fixed inset-0 z-50 flex items-start justify-center bg-black/50 p-4 overflow-y-auto"; _onclick "onModalBackdrop(event)" ] [
    div [ _class "bg-white dark:bg-gray-900 rounded-lg shadow-xl w-full max-w-5xl my-8 p-6" ] [
        div [ _class "flex justify-between items-center mb-4" ] [
            h2 [ _class "text-2xl font-bold" ] [ str "Entity Settings" ]
            button [ _class "..."; _onclick "closeSettings()" ] [ str "✕ Close" ]
        ]
        yield! settingsFormBody parameters   // the shared screen body
    ]
]
```

```js
function openSettings() { document.getElementById('settingsModal').classList.remove('hidden'); document.body.classList.add('overflow-hidden'); }
function closeSettings() { document.getElementById('settingsModal').classList.add('hidden'); document.body.classList.remove('overflow-hidden'); }
function onModalBackdrop(e) { if (e.target && e.target.id === 'settingsModal') closeSettings(); }
document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeSettings(); });

// ingress-safe endpoint URL for fetch (strip a trailing screen segment + slash, then append)
function configUrl() {
    var p = window.location.pathname.replace(/\/config\/?$/, '').replace(/\/$/, '');
    return p + '/config';
}
// then: fetch(configUrl(), { method: 'POST', ... })  — never fetch('/config', ...)
```

## F# conventions (enforced by lint)

- `[<RequireQualifiedAccess>]` on modules meant to be called qualified.
- C#-style indexers `x[i]`, not `x.[i]`.
- No partial functions (`List.head`/`List.item` on possibly-empty lists).
- Async flows use the `asyncResult { }` computation expression (`Feather.ErrorHandling`).
- Namespaces: `MF.HomeConsole`, `MF.Eaton`, `MF.Utils`.
