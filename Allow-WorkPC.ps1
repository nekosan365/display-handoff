#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)]
    [string] $WorkPcIPv4
)
$ErrorActionPreference = 'Stop'
$peerAddress = [System.Net.IPAddress]::Parse($WorkPcIPv4)
if ($peerAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or [System.Net.IPAddress]::IsLoopback($peerAddress) -or $peerAddress.Equals([System.Net.IPAddress]::Any) -or $peerAddress.Equals([System.Net.IPAddress]::Broadcast)) {
    throw 'Specify the Windows work laptop IPv4 address.'
}
$appPath = Join-Path $PSScriptRoot 'DisplayHandoff.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'DisplayHandoff.exe was not found beside this script.' }
$settingsPath = Join-Path $env:LOCALAPPDATA 'DisplayHandoff\settings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.Role -ne 'game') { throw 'Run this on the game PC.' }
$ruleName = 'DisplayHandoff-Return'
$existingRule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
if ($existingRule) { $existingRule | Remove-NetFirewallRule }
New-NetFirewallRule -Name $ruleName -DisplayName 'Display Handoff - Work PC return' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $settings.Port -RemoteAddress $peerAddress.IPAddressToString -Program $appPath -Profile Any | Out-Null
Write-Output "Allowed only $peerAddress to TCP port $($settings.Port) for DisplayHandoff.exe."
Write-Output "To remove this rule: Remove-NetFirewallRule -Name 'DisplayHandoff-Return'"
