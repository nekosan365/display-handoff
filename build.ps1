param([string] $OutputPath = (Join-Path $PSScriptRoot 'DisplayHandoff.exe'))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePaths = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ "/out:$OutputPath" "/win32manifest:$PSScriptRoot\src\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:Microsoft.CSharp.dll $sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built: $OutputPath"
