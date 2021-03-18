Param(
  [string]$name,
  [string]$version
)
$nameUpper = (Get-Culture).TextInfo.ToTitleCase($name)

Write-Host "Building installer for " $name " version " $version

Push-Location
Set-Location (Split-Path -parent $PSCommandPath)
Set-Location ".."
If (!(Test-Path tmp)) {
  New-Item -ItemType Directory -Force -Path tmp
}

dotnet publish -r win-x64 -c Release /p:Version="$version" server/machines/$name/$name.csproj

$heat = "C:\Program Files (x86)\WiX Toolset v3.11\bin\heat.exe"
$candle = "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"
$light = "C:\Program Files (x86)\WiX Toolset v3.11\bin\light.exe"

# Heat config
$publishdir = "server/machines/$name/bin/Release/net5.0/win-x64/publish"
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
  -o "installers/FMS Insight $nameUpper Install.msi"

$latestJson = @{"fms-insight" = $name; "version" = $version; "date" = [DateTime]::UtcNow.ToString("u") } `
  | ConvertTo-Json
[System.IO.File]::WriteAllLines("$pwd\installers\$nameUpper-latest.json", $latestJson)

Remove-Item -r tmp
Pop-Location
