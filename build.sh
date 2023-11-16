#!/usr/bin/env bash

set -euo pipefail

dotnet fsi build.fsx $@
