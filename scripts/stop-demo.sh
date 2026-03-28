#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.run"

AI_PID_FILE="$RUN_DIR/ai-service.pid"
API_PID_FILE="$RUN_DIR/api-service.pid"
AI_PORT="8002"
API_PORT="40212"

stop_process() {
  local name="$1"
  local pid_file="$2"

  if [[ ! -f "$pid_file" ]]; then
    echo "$name: geen pid-file gevonden ($pid_file)."
    return
  fi

  local pid
  pid="$(cat "$pid_file")"

  if kill -0 "$pid" 2>/dev/null; then
    echo "Stop $name (pid=$pid)..."
    kill "$pid"
    sleep 1

    if kill -0 "$pid" 2>/dev/null; then
      echo "$name reageert nog, force stop..."
      kill -9 "$pid" 2>/dev/null || true
    fi

    echo "$name gestopt."
  else
    echo "$name draaide al niet (pid=$pid)."
  fi

  rm -f "$pid_file"
}

stop_by_port() {
  local name="$1"
  local port="$2"
  local pids

  pids="$(lsof -ti tcp:"$port" || true)"
  if [[ -z "$pids" ]]; then
    echo "$name: niets actief op poort $port."
    return
  fi

  echo "Stop $name via poort $port (pid: $pids)..."
  kill $pids 2>/dev/null || true
  sleep 1

  local still_running
  still_running="$(lsof -ti tcp:"$port" || true)"
  if [[ -n "$still_running" ]]; then
    echo "$name reageert nog op poort $port, force stop..."
    kill -9 $still_running 2>/dev/null || true
  fi

  echo "$name op poort $port gestopt."
}

stop_process "AI service" "$AI_PID_FILE"
stop_process "API service" "$API_PID_FILE"

stop_by_port "AI service" "$AI_PORT"
stop_by_port "API service" "$API_PORT"

echo "Klaar."
