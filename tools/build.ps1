param(
    [string]$Configuration = 'Release',
    [switch]$SkipNative
)

$ErrorActionPreference = 'Stop'

function Resolve-DotNetSdk {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramW6432)) {
        $candidates += (Join-Path $env:ProgramW6432 'dotnet')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += (Join-Path $env:ProgramFiles 'dotnet')
    }

    foreach ($root in ($candidates | Select-Object -Unique)) {
        $exe = Join-Path $root 'dotnet.exe'
        if (Test-Path -LiteralPath $exe -PathType Leaf) {
            return @{
                Root = $root
                Exe  = $exe
            }
        }
    }

    $cmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return @{
            Root = (Split-Path -Parent $cmd.Source)
            Exe  = $cmd.Source
        }
    }

    throw "Could not locate dotnet.exe under %LOCALAPPDATA%\Microsoft\dotnet, %ProgramFiles%\dotnet, or PATH."
}

function Resolve-MsvcBuildToolsPath {
    $vswhereCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $vswhereCandidates += (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $vswhereCandidates += (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    }

    $vswhereExe = $vswhereCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($null -eq $vswhereExe) {
        throw "Could not locate vswhere.exe to discover MSVC BuildTools."
    }

    $installPath = & $vswhereExe -all -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($installPath)) {
        throw "vswhere.exe did not find a Visual Studio / BuildTools installation with Microsoft.VisualStudio.Component.VC.Tools.x86.x64."
    }

    return $installPath.Trim()
}

function Import-VcVars64 {
    param([string]$InstallPath)

    $vcvars = Join-Path $InstallPath 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path -LiteralPath $vcvars -PathType Leaf)) {
        throw "vcvars64.bat not found at '$vcvars'."
    }

    Write-Host "[build] Loading MSVC x64 environment from $vcvars..."
    $envOutput = cmd.exe /d /c "call `"$vcvars`" >nul 2>&1 && set"
    if ($LASTEXITCODE -ne 0) {
        throw "vcvars64.bat failed with exit code $LASTEXITCODE."
    }

    foreach ($line in $envOutput) {
        if ($line -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
        }
    }
}

function Resolve-SignToolExe {
    $kitRoots = @()
    foreach ($regPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots'
    )) {
        if (Test-Path -LiteralPath $regPath) {
            try {
                $props = Get-ItemProperty -LiteralPath $regPath -ErrorAction Stop
                if (-not [string]::IsNullOrWhiteSpace($props.KitsRoot10)) {
                    $kitRoots += $props.KitsRoot10
                }
            }
            catch {
                # Continue probing fallback locations.
            }
        }
    }

    $kitRoots += 'C:\Program Files (x86)\Windows Kits\10\'
    $candidates = @()

    foreach ($root in ($kitRoots | Select-Object -Unique)) {
        $trimmed = $root.TrimEnd('\')
        $binDir = Join-Path $trimmed 'bin'
        if (-not (Test-Path -LiteralPath $binDir -PathType Container)) {
            continue
        }

        $versioned = Get-ChildItem -Path $binDir -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
            Sort-Object {
                $v = $null
                if ([version]::TryParse($_.Name, [ref]$v)) { $v } else { [version]'0.0.0.0' }
            } -Descending

        foreach ($dir in $versioned) {
            $exe = Join-Path $dir.FullName 'x64\signtool.exe'
            if (Test-Path -LiteralPath $exe -PathType Leaf) {
                $candidates += $exe
            }
        }

        $directExe = Join-Path $binDir 'x64\signtool.exe'
        if (Test-Path -LiteralPath $directExe -PathType Leaf) {
            $candidates += $directExe
        }
    }

    $resolved = $candidates | Select-Object -First 1
    if ($null -eq $resolved) {
        throw "Could not locate x64 signtool.exe under KitsRoot10 or C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64."
    }

    return $resolved
}

function Invoke-AuthenticodeSign {
    param(
        [string]$SignToolExe,
        [string]$FilePath
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Cannot sign missing artifact: '$FilePath'."
    }

    Write-Host "[build] Signing $FilePath (CN=Synthetic, SHA256)..."
    & $SignToolExe sign /n "Synthetic" /fd SHA256 /a $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool.exe failed for '$FilePath' with exit code $LASTEXITCODE."
    }

    $sig = Get-AuthenticodeSignature -FilePath $FilePath
    if ($sig.Status -ne 'Valid') {
        throw "Authenticode signature verification failed for '$FilePath': Status=$($sig.Status) ($($sig.StatusMessage))."
    }
}

$dotnet = Resolve-DotNetSdk
$env:DOTNET_ROOT = $dotnet.Root
$pathEntries = @($env:PATH -split ';')
if ($pathEntries.Count -eq 0 -or ($pathEntries[0].TrimEnd('\') -ine $dotnet.Root.TrimEnd('\'))) {
    $env:PATH = "$($dotnet.Root);$env:PATH"
}

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path $toolsDir 'RevitWorkerPool.sln'
$poolProjectPath = Join-Path $toolsDir 'RevitWorkerPool\RevitWorkerPool.csproj'
$poolOutDir = Join-Path $toolsDir 'bin\RevitWorkerPool'
$probeProjectPath = Join-Path $toolsDir 'RevitCleanShim.Probes\Net48Probe\Net48Probe.csproj'
$probeOutDir = Join-Path $toolsDir 'bin\Probes\Net48Probe'

Write-Host "[build] Using dotnet: $($dotnet.Exe) (DOTNET_ROOT=$($env:DOTNET_ROOT))"
Write-Host "[build] Building $solutionPath ($Configuration)..."

& $dotnet.Exe build $solutionPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[build] Publishing RevitWorkerPool to $poolOutDir..."
if (Test-Path -LiteralPath $poolOutDir) {
    Remove-Item -LiteralPath $poolOutDir -Recurse -Force
}

& $dotnet.Exe publish $poolProjectPath -c $Configuration --no-restore --self-contained false -o $poolOutDir --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$poolExe = Join-Path $poolOutDir 'RevitWorkerPool.exe'
if (-not (Test-Path -LiteralPath $poolExe -PathType Leaf)) {
    throw "Expected output executable was not produced: $poolExe"
}

if (Test-Path -LiteralPath $probeProjectPath -PathType Leaf) {
    Write-Host "[build] Building Net48Probe ($Configuration) to $probeOutDir..."
    if (Test-Path -LiteralPath $probeOutDir) {
        Remove-Item -LiteralPath $probeOutDir -Recurse -Force
    }

    & $dotnet.Exe build $probeProjectPath -c $Configuration -o $probeOutDir --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $probeExe = Join-Path $probeOutDir 'Net48Probe.exe'
    if (-not (Test-Path -LiteralPath $probeExe -PathType Leaf)) {
        throw "Expected Net48Probe executable was not produced: $probeExe"
    }
}

$nativeProjectDir = Join-Path $toolsDir 'RevitCleanShim'
$detoursSrcDir = Join-Path $toolsDir 'third_party\detours\src'

if (-not $SkipNative) {
    if (-not (Test-Path -LiteralPath $nativeProjectDir -PathType Container)) {
        throw "Native shim directory not found: $nativeProjectDir"
    }
    if (-not (Test-Path -LiteralPath $detoursSrcDir -PathType Container)) {
        throw "Vendored Detours source directory not found: $detoursSrcDir"
    }

    $msvcInstallPath = Resolve-MsvcBuildToolsPath
    Import-VcVars64 -InstallPath $msvcInstallPath

    $nativeObjRoot = Join-Path $toolsDir 'bin\obj\RevitCleanShim'
    $detoursObjDir = Join-Path $nativeObjRoot 'detours'
    $shimObjDir = Join-Path $nativeObjRoot 'shim'
    $launcherObjDir = Join-Path $nativeObjRoot 'launcher'
    New-Item -ItemType Directory -Force -Path $detoursObjDir, $shimObjDir, $launcherObjDir | Out-Null

    $detoursLib = Join-Path $detoursObjDir 'detours.lib'
    Write-Host "[build] Compiling Detours static library (x64 /MT)..."
    & cl.exe /nologo /W4 /WX /we4777 /we4800 /O2 /MT /Gy /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0601 /DDETOUR_DEBUG=0 `
        "/I$detoursSrcDir" "/Fo$detoursObjDir/" /c `
        (Join-Path $detoursSrcDir 'detours.cpp') `
        (Join-Path $detoursSrcDir 'disasm.cpp') `
        (Join-Path $detoursSrcDir 'modules.cpp') `
        (Join-Path $detoursSrcDir 'image.cpp') `
        (Join-Path $detoursSrcDir 'creatwth.cpp')
    if ($LASTEXITCODE -ne 0) {
        throw "Detours compilation failed with exit code $LASTEXITCODE."
    }

    & lib.exe /nologo "/OUT:$detoursLib" `
        (Join-Path $detoursObjDir 'detours.obj') `
        (Join-Path $detoursObjDir 'disasm.obj') `
        (Join-Path $detoursObjDir 'modules.obj') `
        (Join-Path $detoursObjDir 'image.obj') `
        (Join-Path $detoursObjDir 'creatwth.obj')
    if ($LASTEXITCODE -ne 0) {
        throw "Detours static library link failed with exit code $LASTEXITCODE."
    }

    $shimDllOut = Join-Path $poolOutDir 'RevitCleanShim.dll'
    $shimDefPath = Join-Path $nativeProjectDir 'RevitCleanShim.def'
    Write-Host "[build] Compiling RevitCleanShim.dll (x64 /O2 /MT /EHsc /LD)..."
    & cl.exe /nologo /O2 /MT /EHsc /LD /std:c++17 /W4 /WX /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0601 `
        "/I$detoursSrcDir" "/I$nativeProjectDir" "/Fo$shimObjDir/" `
        (Join-Path $nativeProjectDir 'PathFilter.cpp') `
        (Join-Path $nativeProjectDir 'RevitCleanShim.cpp') `
        /link "/DEF:$shimDefPath" "/OUT:$shimDllOut" "/IMPLIB:$(Join-Path $shimObjDir 'RevitCleanShim.lib')" "/PDB:$(Join-Path $shimObjDir 'RevitCleanShim.pdb')" `
        $detoursLib kernel32.lib shell32.lib ole32.lib
    if ($LASTEXITCODE -ne 0) {
        throw "RevitCleanShim.dll build failed with exit code $LASTEXITCODE."
    }

    $launcherDllOut = Join-Path $poolOutDir 'RevitCleanLauncher.dll'
    Write-Host "[build] Compiling RevitCleanLauncher.dll (x64 /O2 /MT /EHsc /LD)..."
    & cl.exe /nologo /O2 /MT /EHsc /LD /std:c++17 /W4 /WX /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0601 `
        "/I$detoursSrcDir" "/Fo$launcherObjDir/" `
        (Join-Path $nativeProjectDir 'RevitCleanLauncher.cpp') `
        /link "/OUT:$launcherDllOut" "/IMPLIB:$(Join-Path $launcherObjDir 'RevitCleanLauncher.lib')" "/PDB:$(Join-Path $launcherObjDir 'RevitCleanLauncher.pdb')" `
        $detoursLib kernel32.lib
    if ($LASTEXITCODE -ne 0) {
        throw "RevitCleanLauncher.dll build failed with exit code $LASTEXITCODE."
    }

    $signToolExe = Resolve-SignToolExe
    Write-Host "[build] Using signtool: $signToolExe"
    Invoke-AuthenticodeSign -SignToolExe $signToolExe -FilePath $shimDllOut
    Invoke-AuthenticodeSign -SignToolExe $signToolExe -FilePath $launcherDllOut
    Invoke-AuthenticodeSign -SignToolExe $signToolExe -FilePath $poolExe

    $testsBinDir = Join-Path $toolsDir "RevitWorkerPool.Tests\bin\$Configuration\net8.0-windows"
    if (Test-Path -LiteralPath $testsBinDir -PathType Container) {
        Copy-Item -LiteralPath $shimDllOut, $launcherDllOut -Destination $testsBinDir -Force
    }
}
else {
    Write-Host "[build] -SkipNative specified; skipping native shim build and signing."
}

Write-Host "[build] Output ready: $poolExe"
