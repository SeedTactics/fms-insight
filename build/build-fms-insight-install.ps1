Push-Location

$heat = "C:\Program Files (x86)\WiX Toolset v3.11\bin\heat.exe"
$candle = "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"
$light = "C:\Program Files (x86)\WiX Toolset v3.11\bin\light.exe"

$scriptdir = Split-Path -parent $PSCommandPath
Set-Location $scriptdir

dotnet publish ../server/src/fms-insight -c Release -f net461
$publishdir = "../server/src/fms-insight/bin/Release/net461/publish"
$ENV:FmsInsightPublishDir = $publishdir

Push-Location
Set-Location "../client/insight"
& yarn install
& yarn build
Pop-Location
$clientdir = "../client/insight/build"
$ENV:FmsInsightClientDir = $clientdir

& $heat dir $publishdir -gg -out tmp/fms-insight-server.wsx -sfrag -sreg -srd -var env.FmsInsightPublishDir -dr INSTALLDIR -cg FmsInsightServerCg
& $heat dir $clientdir  -gg -out tmp/fms-insight-client.wsx -sfrag -sreg -srd -var env.FmsInsightClientDir -dr WWW -cg FmsInsightClientCg

& $candle tmp/fms-insight-server.wsx -o tmp/fms-insight-server.wixobj
& $candle tmp/fms-insight-client.wsx -o tmp/fms-insight-client.wixobj
& $candle fms-insight.wsx -o tmp/fms-insight.wixobj
& $light tmp/fms-insight-server.wixobj tmp/fms-insight-client.wixobj tmp/fms-insight.wixobj -o FMSInsightServer.msm

Remove-Item -r tmp

Pop-Location