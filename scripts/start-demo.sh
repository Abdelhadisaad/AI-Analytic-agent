#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
AI_DIR="$ROOT_DIR/services/ai-service-python"
RUN_DIR="$ROOT_DIR/.run"

AI_HOST="127.0.0.1"
AI_PORT="8002"
AI_URL="http://$AI_HOST:$AI_PORT"

API_HOST="127.0.0.1"
API_PORT="40212"
API_URL="http://$API_HOST:$API_PORT"

AI_PID_FILE="$RUN_DIR/ai-service.pid"
API_PID_FILE="$RUN_DIR/api-service.pid"
AI_LOG_FILE="$RUN_DIR/ai-service.log"
API_LOG_FILE="$RUN_DIR/api-service.log"

mkdir -p "$RUN_DIR"

is_running() {
  local pid="$1"
  kill -0 "$pid" 2>/dev/null
}

start_ai_service() {
  if [[ -f "$AI_PID_FILE" ]]; then
    local pid
    pid="$(cat "$AI_PID_FILE")"
    if is_running "$pid"; then
      echo "AI service draait al (pid=$pid)."
      return
    fi
  fi

  if [[ ! -x "$AI_DIR/.venv/bin/python" ]]; then
    echo "FOUT: Python venv niet gevonden op $AI_DIR/.venv/bin/python"
    echo "Maak eerst de venv en installeer dependencies."
    exit 1
  fi

  echo "Start AI service op $AI_URL ..."
  (
    cd "$AI_DIR"
    nohup .venv/bin/python -m uvicorn app.main:app --host "$AI_HOST" --port "$AI_PORT" > "$AI_LOG_FILE" 2>&1 &
    echo $! > "$AI_PID_FILE"
  )
}

start_api_service() {
  if [[ -f "$API_PID_FILE" ]]; then
    local pid
    pid="$(cat "$API_PID_FILE")"
    if is_running "$pid"; then
      echo "API service draait al (pid=$pid)."
      return
    fi
  fi

  echo "Start ASP.NET app op $API_URL ..."
  (
    cd "$ROOT_DIR"
    nohup env ASPNETCORE_URLS="$API_URL" dotnet run --project src/Analytics.Api > "$API_LOG_FILE" 2>&1 &
    echo $! > "$API_PID_FILE"
  )
}

wait_for_http() {
  local url="$1"
  local name="$2"
  local retries=60

  for _ in $(seq 1 "$retries"); do
    if curl -fsS "$url" >/dev/null 2>&1; then
      echo "$name is klaar: $url"
      return 0
    fi
    sleep 1
  done

  echo "FOUT: $name startte niet op tijd."
  return 1
}

start_ai_service
start_api_service

wait_for_http "$AI_URL/docs" "AI service"
wait_for_http "$API_URL" "API"

echo "Open browser: $API_URL"
open "$API_URL"

echo
echo "Demo draait."
echo "- AI log:  $AI_LOG_FILE"
echo "- API log: $API_LOG_FILE"
echo "Stoppen: kill \"$(cat "$AI_PID_FILE")\" \"$(cat "$API_PID_FILE")\""
