# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
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
