#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCK_ROOT="${SCRIPT_DIR}/site/.lunet"
LOCK_DIR="${SCRIPT_DIR}/site/.lunet/.build-lock"

search_logs() {
    local pattern="$1"
    shift

    if command -v rg >/dev/null 2>&1; then
        rg -n -e "$pattern" "$@"
    else
        grep -n -E "$pattern" "$@"
    fi
}

clean_docs_outputs() {
    rm -rf "${SCRIPT_DIR}/site/.lunet/build/www"
}

cd "${SCRIPT_DIR}"
mkdir -p "${LOCK_ROOT}"
while ! mkdir "${LOCK_DIR}" 2>/dev/null; do
    sleep 1
done

LUNET_LOG=""
cleanup() {
    if [ -n "${LUNET_LOG}" ] && [ -f "${LUNET_LOG}" ]; then
        rm -f "${LUNET_LOG}"
    fi

    rmdir "${LOCK_DIR}" 2>/dev/null || true
}

trap cleanup EXIT

dotnet tool restore
clean_docs_outputs

cd site
LUNET_LOG="$(mktemp)"
dotnet tool run lunet --stacktrace build 2>&1 | tee "${LUNET_LOG}"

if search_logs 'ERR lunet' "${LUNET_LOG}" >/dev/null; then
    echo "Lunet reported site build errors."
    exit 1
fi
