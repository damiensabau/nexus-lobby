#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "$env:ProgramFiles\VpnClient"
)

$serviceName = 'VpnClientService'
if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Service désinstallé. Les profils restent dans $env:ProgramData\VpnClient."
