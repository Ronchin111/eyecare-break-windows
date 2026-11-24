param([string]$Output = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $target) { throw 'Output already exists. Choose a new empty output path.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler is required on Windows.' }
New-Item -ItemType Directory -Path $target | Out-Null
$exe = Join-Path $target 'EyeCareBreak.exe'
$arguments = @('/nologo','/target:winexe','/optimize+','/platform:anycpu','/utf8output',('/win32icon:' + (Join-Path $root 'native\icon\eye-care.ico')),('/out:' + $exe),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'native') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination (Join-Path $target 'assets') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Join-Path $target 'docs') -Recurse
foreach ($name in @('LICENSE','THIRD_PARTY_NOTICES.md','README.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $target $name) }
[IO.File]::WriteAllText((Join-Path $target 'Open-EyeCare.cmd'), "@echo off`r`nstart `"`" `"%~dp0EyeCareBreak.exe`" --settings`r`n", [Text.Encoding]::ASCII)
Write-Output "Built: $exe"
