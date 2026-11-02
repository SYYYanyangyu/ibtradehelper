#!/bin/sh
set -eu
mkdir -p /data
if [ "$(id -u)" = "0" ]; then
  chown -R app:app /data
  exec runuser -u app -- dotnet /app/IBTrade.Api.dll
fi
exec dotnet /app/IBTrade.Api.dll
