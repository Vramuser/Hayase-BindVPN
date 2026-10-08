$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseVersion = (Get-Content -LiteralPath "$root\package.json" -Raw | ConvertFrom-Json).version
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch release version.' }
$pluginVersion = (Get-Content -LiteralPath "$root\plugin\manifest.json" -Raw | ConvertFrom-Json).version
$releaseInfo = Get-Content -LiteralPath "$root\helper\ReleaseInfo.cs" -Raw
$helperVersion = [regex]::Match($releaseInfo, 'const string Version = "([^"]+)"').Groups[1].Value
$assemblyVersion = [regex]::Match($releaseInfo, 'const string AssemblyVersion = "([^"]+)"').Groups[1].Value
if ($pluginVersion -ne $releaseVersion -or $helperVersion -ne $releaseVersion -or $assemblyVersion -ne "$releaseVersion.0") {
    throw 'Package, plugin, helper, and assembly versions must match before building.'
}
$dist = Join-Path $root "dist\v$releaseVersion"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The built-in .NET Framework C# compiler is required (64-bit Windows).' }
$helper = Join-Path $dist 'Hayase-BindVPN-Helper.exe'
$probe = Join-Path $dist 'Hayase-BindVPN-Probe.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$probe" /r:System.dll "$root\tests\NetworkProbe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Embedded probe compilation failed.' }
$sources = @('ReleaseInfo.cs', 'Wfp.cs', 'Controller.cs', 'ProtectionTest.cs', 'Api.cs', 'Program.cs') | ForEach-Object { Join-Path $root "helper\$_" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32manifest:$root\helper\app.manifest" "/resource:$probe,HayaseBindVPN.NetworkProbe" "/out:$helper" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Helper compilation failed.' }
$pluginFiles = @((Get-ChildItem -LiteralPath "$root\plugin" -File).FullName) + @("$root\LICENSE")
Compress-Archive -LiteralPath $pluginFiles -DestinationPath "$dist\Hayase-BindVPN-plugin.zip" -Force
Copy-Item -LiteralPath "$root\docs\QUICKSTART.md" -Destination "$dist\README.md" -Force
Copy-Item -LiteralPath "$root\LICENSE" -Destination "$dist\LICENSE" -Force
Copy-Item -LiteralPath "$root\CHANGELOG.md" -Destination "$dist\CHANGELOG.md" -Force
Copy-Item -LiteralPath "$root\scripts\Start-Helper.cmd" -Destination "$dist\Start-Helper.cmd" -Force
Copy-Item -LiteralPath "$root\scripts\Recover.cmd" -Destination "$dist\Recover.cmd" -Force
$packageFiles = @((Get-ChildItem -LiteralPath "$root\plugin" -File).FullName) + @(
    "$dist\Hayase-BindVPN-Helper.exe", "$dist\Hayase-BindVPN-plugin.zip", "$dist\README.md", "$dist\LICENSE", "$dist\CHANGELOG.md", "$dist\Start-Helper.cmd", "$dist\Recover.cmd"
)
# Both downloads must be directly importable: Hayase looks for manifest.json
# at the archive root and does not search inside a nested plugin ZIP.
Compress-Archive -LiteralPath $packageFiles -DestinationPath "$dist\Hayase-BindVPN-Windows.zip" -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($archivePath in @("$dist\Hayase-BindVPN-plugin.zip", "$dist\Hayase-BindVPN-Windows.zip")) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $manifestEntry = $archive.GetEntry('manifest.json')
        if (-not $manifestEntry) { throw "No root manifest.json in $archivePath" }
        if (-not $archive.GetEntry('LICENSE')) { throw "MIT license missing from $archivePath" }
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if (-not $archive.GetEntry($manifest.action.default_popup)) { throw "Plugin popup missing from $archivePath" }
    } finally { $archive.Dispose() }
}

# Use an explicit source allowlist: no research clones, local profiles, generated
# tests, binaries, configuration, or other workspace files enter the source ZIP.
$sourceArchivePath = Join-Path $dist 'Hayase-BindVPN-source.zip'
$sourceStream = [System.IO.File]::Open($sourceArchivePath, [System.IO.FileMode]::Create)
$sourceArchive = New-Object System.IO.Compression.ZipArchive($sourceStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $sourceRoots = @('.github', '.gitignore', '.gitattributes', 'package.json', 'README.md', 'LICENSE', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'docs', 'helper', 'plugin', 'scripts', 'tests')
    foreach ($name in $sourceRoots) {
        $sourcePath = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Missing source release item: $name" }
        $item = Get-Item -LiteralPath $sourcePath -Force
        $files = if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force } else { @($item) }
        foreach ($file in $files) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source archives cannot include linked files.' }
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $file.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $sourceArchive.Dispose(); $sourceStream.Dispose() }

function Get-ReleaseSHA256 {
    param([string]$LiteralPath)
    $stream = $null
    $sha256 = $null
    try {
        $stream = [System.IO.File]::OpenRead($LiteralPath)
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        # Hash the stream directly so a runner's PowerShell module configuration
        # cannot prevent checksums from being generated.
        return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        if ($null -ne $sha256) { $sha256.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

$checksums = foreach ($name in @('Hayase-BindVPN-Windows.zip', 'Hayase-BindVPN-plugin.zip', 'Hayase-BindVPN-source.zip')) {
    $hash = Get-ReleaseSHA256 -LiteralPath (Join-Path $dist $name)
    "$hash  $name"
}
[System.IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$checksums, [System.Text.Encoding]::ASCII)
Write-Output "Built release v$releaseVersion in $dist"
