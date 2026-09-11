#!/usr/bin/env bash
set -uo pipefail

cd "$(dirname "$0")/.."

# Windows の Git Bash では $(pwd) が /c/Users/... 形式になり、Windows ネイティブの
# ConAI.Web.exe、Chromium、CLI のサーバプロセスには通じない。cygpath -m で C:/Users/... 形式にする。
REPO_ROOT="$(pwd)"
if command -v cygpath > /dev/null 2>&1; then
    REPO_ROOT="$(cygpath -m "$REPO_ROOT")"
fi

PORT=5099
BASE_URL="http://127.0.0.1:${PORT}"
DATA_ROOT="${REPO_ROOT}/e2e/.data"
OUTPUT_DIR="e2e/.output"
APP_LOG="$(pwd)/e2e/.app.log"

if [ ! -f e2e/fixtures/meeting.wav ]; then
    echo "偽マイク用の WAV がありません。先に次を実行してください。"
    echo "  pwsh -File e2e/fixtures/make-wav.ps1"
    exit 1
fi

rm -rf "$DATA_ROOT" "$OUTPUT_DIR"
mkdir -p "$DATA_ROOT" "$OUTPUT_DIR/tests"

# __REPO_ROOT__ を絶対パスに置換した設定とスクリプトを e2e/.output/ に生成する（Step 1 の 5）
sed "s#__REPO_ROOT__#${REPO_ROOT}#g" e2e/playwright-cli.json > "$OUTPUT_DIR/playwright-cli.json"
for script in e2e/tests/*.js; do
    sed "s#__REPO_ROOT__#${REPO_ROOT}#g" "$script" > "$OUTPUT_DIR/tests/$(basename "$script")"
done

dotnet build ConAI.sln -c Debug || exit 1

APP_EXE="src/ConAI.Web/bin/Debug/net10.0/ConAI.Web.exe"
if [ ! -f "$APP_EXE" ]; then
    APP_EXE="src/ConAI.Web/bin/Debug/net10.0/ConAI.Web"
fi

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="$BASE_URL" \
Storage__DataRoot="$DATA_ROOT" \
Gemini__Provider=Fake \
Gemini__LiveModel=fake-live \
Gemini__LiveTranslateModel=fake-translate \
Gemini__GenerateModel=fake-generate \
Gemini__SelectModel=fake-select \
Notifications__Provider=Fake \
Notifications__PublicBaseUrl="$BASE_URL" \
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

playwright-cli open --config="$OUTPUT_DIR/playwright-cli.json" "$BASE_URL" || exit 1

status=0
for script in "$OUTPUT_DIR"/tests/*.js; do
    name="e2e/tests/$(basename "$script")"
    echo "--- $name ---"
    if playwright-cli run-code --filename="$script"; then
        echo "PASS $name"
    else
        echo "FAIL $name"
        status=1
    fi
done

exit "$status"
