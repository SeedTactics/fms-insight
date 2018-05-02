Param(
  [string]$name
)
$nameUpper = (Get-Culture).TextInfo.ToTitleCase($name)

function ver ($x) { c:\python36\python.exe build/version.py $x }
$tag = $(hg id -t -r '.^')
if ($tag.StartsWith("mazak")) {
    $version = $(ver $name)
} else {
    $version = $(ver $name) + "pre." + $Env.APPVEYOR_BUILD_NUMBER
}
Write-Host "Building installer for " + $name + " version " + $version

Push-Location
Set-Location (Split-Path -parent $PSCommandPath)
Set-Location ".."
If (!(Test-Path tmp)) {
  New-Item -ItemType Directory -Force -Path tmp
}

$heat = "C:\Program Files (x86)\WiX Toolset v3.11\bin\heat.exe"
$candle = "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"
$light = "C:\Program Files (x86)\WiX Toolset v3.11\bin\light.exe"

# Heat config
$publishdir = "server/machines/$name/bin/Release/net461/publish"
$ENV:InsightPublishDir = $publishdir
$ENV:InsightProductId = (New-Guid).Guid
$ENV:InsightVersion = $version
$clientdir = "client/insight/build"
$ENV:InsightClientDir = $clientdir

# Move exe out so it is not captured by heat
Move-Item "$publishdir/BlackMaple.FMSInsight.$nameUpper.exe" tmp -Force

# Heat for simlab and client
& $heat dir $publishdir -gg -out tmp/insight-server.wsx -sfrag -sreg -srd -var env.InsightPublishDir -dr INSTALLDIR -cg InsightServerCg
& $heat dir $clientdir  -gg -out tmp/insight-client.wsx -sfrag -sreg -srd -var env.InsightClientDir -dr clientwww -cg InsightClientCg

& $candle tmp/insight-server.wsx -o tmp/insight-server.wixobj
& $candle tmp/insight-client.wsx -o tmp/insight-client.wixobj
& $candle build/$name.wsx -o tmp/insight.wixobj -ext WixUtilExtension
& $light tmp/insight-server.wixobj tmp/insight-client.wixobj tmp/insight.wixobj -ext WixUtilExtension `
  -o "installers/FMS Insight $nameUpper Install.msi"

Remove-Item -r tmp
Pop-Location
