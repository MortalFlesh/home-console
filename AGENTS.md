# AGENTS.md

## Project Overview

**home-console** is an F# console application (.NET 10) that acts as a bridge between the **Eaton xComfort** closed smart home system and **Home Assistant**. It communicates with the Eaton CTRL unit over its JSON-RPC HTTP API and exposes sensor/device data via a local web server that Home Assistant can consume.

## Repository Layout

```
src/
  Program.fs              # Entry point — registers CLI commands
  Core/                   # Core library project
    Command/              # CLI command implementations
      DownloadEatonHistory.fs
      DownloadEatonDeviceList.fs
      RunWebServer.fs
    WebServer/            # Saturn/Giraffe web server (routes, views, utils)
    Config.fs             # Config parsing (JSON schema-driven via FSharp.Data)
    Console.fs            # Shared console helpers
  Eaton/                  # Eaton API client library
    Api.fs                # HTTP client (cookie auth, JSON-RPC calls)
    Types.fs              # Domain types (EatonConfig, ApiError, Device types)
    Utils.fs
    JsonRpc/              # JSON-RPC request/response helpers
  Utils/                  # Shared utilities
    Cache.fs              # Thread-safe ConcurrentDictionary-based cache
build/                    # FAKE build scripts (F# make)
  Build.fs                # Entry point for `fake build`
  Targets.fs              # Build targets (Clean, Build, Release, Tests, …)
  SafeBuildHelpers.fs     # SAFE stack helpers (not currently active)
  Utils.fs
.github/workflows/        # CI: checks.yaml (build+lint on push/PR/schedule), pr-check.yaml
```

## Tech Stack

| Concern | Library |
|---|---|
| Language / runtime | F# / .NET 10 |
| Package manager | Paket (`paket.dependencies`, `paket.references`) |
| Build automation | FAKE (`build/`) |
| CLI framework | `Feather.ConsoleApplication` |
| Error handling | `Feather.ErrorHandling` (AsyncResult) |
| Web server | Saturn + Giraffe |
| HTTP client / JSON | `FSharp.Data` |
| Linting | FSharpLint (`fsharplint.json`) |

## Build & Run

```sh
# Restore tools (paket, fake, fsharplint)
dotnet tool restore

# Build (runs lint, compiles, outputs to bin/console)
./build.sh          # macOS / Linux
./build.cmd         # Windows

# Watch mode during development
./build.sh -t watch

# Run the compiled binary
bin/console help
bin/console list
```

Build targets defined in `build/Targets.fs`: `Clean`, `Build`, `Lint`, `Tests`, `Release` (produces cross-platform binaries in `dist/`).

Release targets: `RaspberryPiHassioAddon`, `OSX`, `Windows` (see `Build.fs`).

## CLI Commands

| Command | Description |
|---|---|
| `home:download:history` | Download history XML from the Eaton controller |
| `home:download:devices` | Download the devices list from the Eaton controller |
| `home:web:run` | Start web server on port 28080; exposes `/sensors` as JSON |

### `home:web:run` options

| Option | Default | Description |
|---|---|---|
| `--host` | — | IP of the Eaton CTRL unit (e.g. `192.168.1.9`) |
| `--name` | `admin` | Login name |
| `--password` | — | Login password |
| `--cookies-path` | `./eaton-cookies.json` | Path for persisted session cookies |
| `--history-path` | `./eaton-history` | Directory for downloaded history files |
| `--config` | — | Path to a JSON config file (alternative to individual options) |

## Eaton API

The Eaton controller exposes a JSON-RPC 2.0 endpoint at `http://<host>/remote/json-rpc`. Authentication is cookie-based (`JSESSIONID`). Cookies are serialised to disk (`eaton-cookies.json`) and reloaded on startup.

Key JSON-RPC methods:
- `StatusControlFunction/controlDevice` — turn on/off or dim a device
- `StatusControlFunction/getDevices` — list devices with their current state in a zone
- `StatusControlFunction/getDashboard` — dashboard summary for a zone
- `SceneFunction/triggerScene` — trigger a scene or macro

Zone IDs follow the pattern `hz_N` (e.g. `hz_1`, `hz_3`).

## Configuration File

Alternative to CLI flags — a JSON file validated against `src/Core/schema/config.json`:

```json
{
  "eaton": {
    "host": "192.168.1.9",
    "credentials": {
      "username": "admin",
      "password": "...",
      "path": "./eaton-cookies.json"
    },
    "history": {
      "download": "./eaton-history"
    }
  }
}
```

## Code Conventions

- Namespaces: `MF.HomeConsole`, `MF.Eaton`, `MF.Utils`
- PascalCase for types, modules, record fields, union cases
- CamelCase for parameters and private/internal values
- 4-space indentation
- Async operations use `asyncResult { }` computation expression (`Feather.ErrorHandling`)
- `[<RequireQualifiedAccess>]` on all modules that should be called with their module name
- C#-style indexer access (`x[i]` not `x.[i]`)
- No partial functions (`List.head`, `List.item` etc. on potentially empty collections)

## CI

- **checks.yaml** — runs `./build.sh` (or `.cmd`) on macOS, Ubuntu, Windows against .NET 10 on every PR and on a nightly cron schedule
- **pr-check.yaml** — blocks PRs that contain fixup commits

## Local Network Context

| IP | Device |
|---|---|
| 192.168.1.1 | Router |
| 192.168.1.5 | Home Assistant |
| 192.168.1.9 | Eaton CTRL |
| 192.168.1.10 | NAS |
