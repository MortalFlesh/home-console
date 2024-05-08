# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
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
