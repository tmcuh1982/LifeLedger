#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_directory="$(cd "${script_directory}/.." && pwd)"
lifeledger_port="${LIFELEDGER_PORT:-5078}"
lifeledger_url="http://localhost:${lifeledger_port}"

if [[ ! "${lifeledger_port}" =~ ^[0-9]+$ ]] || (( lifeledger_port < 1 || lifeledger_port > 65535 )); then
  echo "LifeLedger: LIFELEDGER_PORT must be a number between 1 and 65535." >&2
  exit 2
fi

# A successful local health check means the requested outcome is already available.
if command -v curl >/dev/null 2>&1 && curl --fail --silent --max-time 1 "${lifeledger_url}/api/health" >/dev/null 2>&1; then
  echo "LifeLedger est déjà lancé / is already running: ${lifeledger_url}"
  exit 0
fi

# On macOS and most developer Linux installations, show the process that owns a conflicting port.
if command -v lsof >/dev/null 2>&1 && port_owner="$(lsof -nP -iTCP:"${lifeledger_port}" -sTCP:LISTEN 2>/dev/null)" && [[ -n "${port_owner}" ]]; then
  echo "LifeLedger ne peut pas démarrer : le port ${lifeledger_port} est déjà utilisé." >&2
  echo "LifeLedger cannot start: port ${lifeledger_port} is already in use." >&2
  echo "${port_owner}" >&2
  echo "Arrêtez ce programme ou lancez avec un autre port, par exemple:" >&2
  echo "LIFELEDGER_PORT=5080 ./scripts/start-local.sh" >&2
  exit 2
fi

echo "Démarrage de LifeLedger sur ${lifeledger_url}"
exec dotnet run --project "${repository_directory}/src/LifeLedger.Api" --urls "${lifeledger_url}"
