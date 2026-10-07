param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'test-output'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$out\ApiTests.exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll "$root\helper\Api.cs" "$root\tests\ApiTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Helper HTTP test compilation failed.' }
if (-not $CompileOnly) {
    & "$out\ApiTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Helper HTTP tests failed.' }
}
