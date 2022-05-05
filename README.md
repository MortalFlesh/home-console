HOME console
============

[![Check](https://github.com/MortalFlesh/home-console/actions/workflows/checks.yaml/badge.svg)](https://github.com/MortalFlesh/home-console/actions/workflows/checks.yaml)

> Console application to help with home automations.

## Run statically

First compile
```sh
fake build target release
```

Then run
```sh
dist/home-console help
```

List commands
```sh
dist/home-console list
```

todo - show help

### Google Sheets Api
You need to create credentials for your google sheets (see https://console.cloud.google.com/apis/api/sheets.googleapis.com/overview).
And place them as `credentials.json` to the `credentials/credentials.json` and then, on the first run, you will be prompted to authorize app by your google account (_link for this will be shown in the terminal_). After that token for authorization will be stored in `credentials/token.json`.

---
### Development

First run `./build.sh` or `./build.sh -t watch`

List commands
```sh
bin/console list
```

Run tests locally
```sh
fake build target Tests
```
