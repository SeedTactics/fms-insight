Param(
  [string]$name
)
$nameUpper = (Get-Culture).TextInfo.ToTitleCase($name)

function ver ($x) { c:\python36\python.exe build/version.py $x }
$tag = $(hg id -t -r '.^')
if ($tag.StartsWith($name)) {
    $version = $(ver $name)
    Set-AppveyorBuildVariable $("BMS_INSTALLER_" + $name + "_RELEASE") "true"
} else {
    # WiX doesn't support semver, so don't increment
    $version = $(ver $name --no-increment) + "." + $Env:APPVEYOR_BUILD_NUMBER
}
Write-Host "Building installer for " $name " version " $version

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

# Move exe out so it is not captured by heat
Move-Item "$publishdir/BlackMaple.FMSInsight.$nameUpper.exe" tmp -Force

# Heat for server
& $heat dir $publishdir -gg -out tmp/insight-server.wsx -sfrag -sreg -srd -var env.InsightPublishDir -dr INSTALLDIR -cg InsightServerCg

& $candle tmp/insight-server.wsx -o tmp/insight-server.wixobj
& $candle build/$name.wsx -o tmp/insight.wixobj -ext WixUtilExtension
& $light tmp/insight-server.wixobj tmp/insight.wixobj -ext WixUtilExtension `
  -o "installers/$version/FMS Insight $nameUpper Install.msi"

Remove-Item -r tmp
Pop-Location
