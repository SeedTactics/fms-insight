Param( [string]$version)
Write-Host "Building FMS Insight reverse proxy version $version"

Push-Location
Set-Location (Split-Path -parent $PSCommandPath)

If (Test-Path build) {
  Remove-Item -r build
}
New-Item -ItemType Directory -Force -Path build

dotnet publish -r win10-x64 --self-contained -c Release /p:Version=$version /p:PublishSingleFile=true

Copy-Item bin/Release/netcoreapp3.1/win10-x64/publish/reverse-proxy.exe build/reverse-proxy.exe

$candle = "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe"
$light = "C:\Program Files (x86)\WiX Toolset v3.11\bin\light.exe"

$ENV:ProxyProductId = (New-Guid).Guid
$ENV:ProxyVersion = $version

& $candle reverse-proxy.wsx -o build/reverse-proxy.wixobj -ext WixUtilExtension
& $light build/reverse-proxy.wixobj -ext WixUtilExtension `
  -o "build/FMS Insight Reverse Proxy Install.msi"

Pop-Location