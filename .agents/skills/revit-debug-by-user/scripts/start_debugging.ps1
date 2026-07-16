param (
    [string]$Version = ""
)

# 1. Resolve target version
$workspaceRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")
$configPath = Join-Path $workspaceRoot "docs\agents\revit-config.md"
$targetVersion = $Version

if ([string]::IsNullOrEmpty($targetVersion)) {
    if (Test-Path $configPath) {
        $content = Get-Content $configPath -Raw
        if ($content -match 'CURRENT_LATEST_VERSION.*?\x60(\d+)\x60') {
            $targetVersion = $Matches[1]
        }
    }
}

if ([string]::IsNullOrEmpty($targetVersion)) {
    $targetVersion = "2026"  # fallback default
}

Write-Host "Target Revit version: $targetVersion"

# 2. Open or attach to Visual Studio DTE COM Object
$dte = $null
try {
    # Check for running instance of Visual Studio DTE
    Write-Host "Checking for existing Visual Studio instance..."
    $dte = [Runtime.InteropServices.Marshal]::GetActiveObject("VisualStudio.DTE")
    Write-Host "Attached to running Visual Studio instance."
} catch {
    Write-Host "Starting new Visual Studio instance..."
    $dte = New-Object -ComObject "VisualStudio.DTE"
}

if ($dte -eq $null) {
    Write-Error "Failed to initialize Visual Studio DTE COM object."
    exit 1
}

# Make VS window visible
$dte.MainWindow.Visible = $true

# 3. Load the Solution
$slnPath = Join-Path $workspaceRoot "src\Synthetic.sln"

if ($dte.Solution.IsOpen) {
    if ($dte.Solution.FullName.ToLower() -ne $slnPath.ToLower()) {
        Write-Host "Closing current solution..."
        $dte.Solution.Close()
        Write-Host "Opening solution: $slnPath"
        $dte.Solution.Open($slnPath)
    } else {
        Write-Host "Solution is already open."
    }
} else {
    Write-Host "Opening solution: $slnPath"
    $dte.Solution.Open($slnPath)
}

# Wait for solution to load fully
while ($dte.Solution.IsOpen -ne $true) {
    Start-Sleep -Seconds 1
}

# 4. Set Startup Project
$projectRelativePath = "Synthetic$targetVersion\Synthetic$targetVersion.csproj"
Write-Host "Setting startup project to: $projectRelativePath"
try {
    $dte.Solution.SolutionBuild.StartupProjects = $projectRelativePath
} catch {
    Write-Warning "Could not set startup project '$projectRelativePath'. It may not exist in the solution."
}

# 5. Initiate Debugging
Write-Host "Starting debugging session (F5)..."
try {
    # Go($false) starts debugging asynchronously so the script doesn't block
    $dte.Debugger.Go($false)
    Write-Host "Debugging session initiated successfully!"
} catch {
    Write-Error "Failed to start debugging session: $_"
}
