param([switch]$Integration)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'test-output'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$out\NetworkProbe.exe" /r:System.dll "$root\tests\NetworkProbe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$out\NativeTests.exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll "$root\helper\ReleaseInfo.cs" "$root\helper\Wfp.cs" "$root\helper\Api.cs" "$root\helper\Controller.cs" "$root\helper\ProtectionTest.cs" "$root\tests\NativeTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
if ($Integration) { & "$out\NativeTests.exe" --integration "$out\NetworkProbe.exe" }
else { & "$out\NativeTests.exe" }
if ($LASTEXITCODE -ne 0) { throw 'Native checks failed.' }
