#!/usr/bin/env bash
set -euo pipefail

TARGET_RUNTIME_ID="${TARGET_RUNTIME_ID:-win-x64}"
NATIVE_RUNTIME="native/out/runtime/$TARGET_RUNTIME_ID/native"
NATIVE_ARTIFACT="artifacts/DrawVerify-native-occt-8.0.0-$TARGET_RUNTIME_ID.zip"

if [[ -d "$NATIVE_RUNTIME" && ! -f "$NATIVE_ARTIFACT" ]]; then
  if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -File native/scripts/manage-native-artifact.ps1 \
      -Mode Pack \
      -RuntimeId "$TARGET_RUNTIME_ID" \
      -Configuration Debug
  else
    echo "PowerShell is required to package the native OCCT artifact: $NATIVE_ARTIFACT" >&2
    exit 1
  fi
fi

if [[ ! -f "$NATIVE_RUNTIME/DrawVerifyOcctNative.dll" ]]; then
  echo "Native OCCT runtime is missing: $NATIVE_RUNTIME/DrawVerifyOcctNative.dll" >&2
  echo "Restore or stage the win-x64 native artifact before publishing." >&2
  exit 1
fi

PUB=$(python3 -c "import json,re;p=json.load(open('src/DrawVerify.App/appsettings.json'))['Publish']['Path'];d,r=re.match(r'^([A-Za-z]):\\\\(.*)$',p).groups();print('/mnt/'+d.lower()+'/'+r.replace('\\\\','/'))") \
&& rm -rf "$PUB" \
&& dotnet restore src/DrawVerify.App -r "$TARGET_RUNTIME_ID" \
&& dotnet publish src/DrawVerify.App -c Debug -r "$TARGET_RUNTIME_ID" --self-contained true --no-restore -o "$PUB" \
&& mkdir -p "$PUB/native" \
&& cp -a "$NATIVE_RUNTIME"/. "$PUB/native/" \
&& if [[ -f "data/database/drawverify.db" && ! -f "$PUB/drawverify.db" ]]; then cp "data/database/drawverify.db" "$PUB/drawverify.db"; fi \
&& cmd.exe /c "C:\Users\nirma\Documents\Product\DrawVerify\Published\DrawVerify.App.exe"
