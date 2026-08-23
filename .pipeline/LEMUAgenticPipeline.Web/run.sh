#!/bin/bash
# Run from repo root or from this dir
set -e
PORT=${1:-5000}
echo "→ Killing old LEMUAgenticPipeline.Web on :$PORT ..."
fuser -k $PORT/tcp 2>/dev/null || true
pkill -f "LEMUAgenticPipeline.Web.*$PORT" 2>/dev/null || true
sleep 1
exec dotnet run --project "$(dirname "$0")" --urls "http://localhost:$PORT"
