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
