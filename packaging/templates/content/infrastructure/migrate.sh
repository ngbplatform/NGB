#!/bin/sh
set -eu

dotnet NgbApplication.Migrator.dll
dotnet NgbApplication.Migrator.dll seed-administrator
