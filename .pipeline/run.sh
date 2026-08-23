#!/bin/bash
# Run LEMU Agentic Pipeline Web — kills old instance on same port first
# Web lives at .pipeline/LEMUAgenticPipeline.Web
set -e
PORT=${1:-5000}
echo "→ Killing old LEMUAgenticPipeline.Web on :$PORT ..."
fuser -k $PORT/tcp 2>/dev/null || true
pkill -f "LEMUAgenticPipeline.Web.*$PORT" 2>/dev/null || true
pkill -f "dotnet.*LEMUAgenticPipeline.Web" 2>/dev/null || true
sleep 1
if ss -tlnp 2>/dev/null | grep -q ":$PORT "; then
  echo "✗ Port $PORT still in use — trying lsof"
  lsof -ti:$PORT 2>/dev/null | xargs kill -9 2>/dev/null || true
  sleep 1
fi
echo "→ Starting http://localhost:$PORT/pipeline ..."
exec dotnet run --project "$(dirname "$0")/LEMUAgenticPipeline.Web" --urls "http://localhost:$PORT"
