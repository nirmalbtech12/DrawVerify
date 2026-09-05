#!/usr/bin/env bash

PUB=$(python3 -c "import json,re;p=json.load(open('src/DrawVerify.App/appsettings.json'))['Publish']['Path'];d,r=re.match(r'^([A-Za-z]):\\\\(.*)$',p).groups();print('/mnt/'+d.lower()+'/'+r.replace('\\\\','/'))") \
&& rm -rf "$PUB" \
&& dotnet restore src/DrawVerify.App -r win-x64 \
&& dotnet publish src/DrawVerify.App -c Debug -r win-x64 --self-contained true --no-restore -o "$PUB" \
&& cmd.exe /c "C:\Users\nirma\Documents\Product\DrawVerify\Published\DrawVerify.App.exe"
