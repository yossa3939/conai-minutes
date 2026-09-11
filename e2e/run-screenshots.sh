#!/usr/bin/env bash
set -uo pipefail

cd "$(dirname "$0")/.."

# Windows の Git Bash では $(pwd) が /c/Users/... 形式になり、Windows ネイティブの
# ConAI.Web.exe と Chromium には通じない。cygpath -m で C:/Users/... 形式にする。
REPO_ROOT="$(pwd)"
if command -v cygpath > /dev/null 2>&1; then
    REPO_ROOT="$(cygpath -m "$REPO_ROOT")"
fi

PORT=5098
BASE_URL="http://127.0.0.1:${PORT}"
DATA_ROOT="${REPO_ROOT}/e2e/.data"
OUTPUT_DIR="e2e/.output"
SHOT_DIR="${OUTPUT_DIR}/screenshots"
APP_LOG="$(pwd)/e2e/.screenshots.log"

rm -rf "$DATA_ROOT" "$SHOT_DIR"
mkdir -p "$DATA_ROOT" "$OUTPUT_DIR" "$SHOT_DIR"

cat > "${OUTPUT_DIR}/screenshots-cli.json" <<'JSON'
{
  "browser": {
    "browserName": "chromium",
    "isolated": true
  },
  "outputMode": "stdout",
  "allowUnrestrictedFileAccess": true
}
JSON

sed "s#__REPO_ROOT__#${REPO_ROOT}#g" e2e/screenshots.js > "${OUTPUT_DIR}/screenshots.js"

dotnet build ConAI.sln -c Debug || exit 1

APP_EXE="src/ConAI.Web/bin/Debug/net10.0/ConAI.Web.exe"
if [ ! -f "$APP_EXE" ]; then
    APP_EXE="src/ConAI.Web/bin/Debug/net10.0/ConAI.Web"
fi

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="$BASE_URL" \
Storage__DataRoot="$DATA_ROOT" \
Gemini__Provider=Fake \
Auth__AllowSelfRegistration=true \
"$APP_EXE" > "$APP_LOG" 2>&1 &
APP_PID=$!

cleanup() {
    playwright-cli close > /dev/null 2>&1
    kill "$APP_PID" > /dev/null 2>&1
    wait "$APP_PID" > /dev/null 2>&1
}
trap cleanup EXIT

for _ in $(seq 1 60); do
    if curl -sf "$BASE_URL/healthz" > /dev/null; then
        break
    fi
    sleep 1
done

if ! curl -sf "$BASE_URL/healthz" > /dev/null; then
    echo "アプリが起動しませんでした。$APP_LOG を確認してください。"
    exit 1
fi

playwright-cli open --config="${OUTPUT_DIR}/screenshots-cli.json" "$BASE_URL" || exit 1
playwright-cli run-code --filename="${OUTPUT_DIR}/screenshots.js"
