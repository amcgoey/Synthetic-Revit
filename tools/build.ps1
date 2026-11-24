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

$nativeProjectDir = Join-Path $toolsDir 'RevitCleanShim'
if (-not $SkipNative) {
    if (Test-Path -LiteralPath $nativeProjectDir -PathType Container) {
        Write-Host "[build] Native shim directory detected at $nativeProjectDir (native build step hook)."
    }
    else {
        Write-Host "[build] Skipping native shim step (tools/RevitCleanShim not present yet)."
    }
}
else {
    Write-Host "[build] -SkipNative specified; skipping native shim build."
}

Write-Host "[build] Output ready: $poolExe"
