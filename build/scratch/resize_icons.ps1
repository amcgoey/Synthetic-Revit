Add-Type -AssemblyName System.Drawing

function Resize-Image {
    param (
        [string]$InputPath,
        [string]$OutputPath,
        [int]$Width,
        [int]$Height
    )
    if (-not (Test-Path $InputPath)) {
        Write-Error "Input file not found: $InputPath"
        return
    }
    
    $src = [System.Drawing.Image]::FromFile($InputPath)
    $dest = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($dest)
    
    # Configure high-quality scaling settings
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    
    $g.DrawImage($src, 0, 0, $Width, $Height)
    
    # Ensure directory exists
    $outDir = Split-Path $OutputPath
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    
    $dest.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    
    $g.Dispose()
    $dest.Dispose()
    $src.Dispose()
    
    Write-Host "Resized $InputPath to $OutputPath ($Width x $Height)"
}

# Source directory where high-res icons are generated
$srcDir = Join-Path $PSScriptRoot "highres"
# Target Assets directory (robust to both pre- and post-restructuring layouts)
$parentDir = Split-Path $PSScriptRoot -Parent
if (Test-Path (Join-Path $parentDir "SyntheticShared\Assets")) {
    $assetsDir = Join-Path $parentDir "SyntheticShared\Assets"
} else {
    $repoRoot = Split-Path $parentDir -Parent
    $assetsDir = Join-Path $repoRoot "src\SyntheticShared\Assets"
}

$icons = @(
    "autonumber",
    "autotag",
    "convert",
    "family",
    "material",
    "paint",
    "print",
    "schema",
    "scopebox",
    "settings",
    "workset"
)

foreach ($icon in $icons) {
    $inputFile = Join-Path $srcDir "$icon.png"
    if (Test-Path $inputFile) {
        $out32 = Join-Path $assetsDir "$($icon)_32.png"
        $out16 = Join-Path $assetsDir "$($icon)_16.png"
        
        Resize-Image -InputPath $inputFile -OutputPath $out32 -Width 32 -Height 32
        Resize-Image -InputPath $inputFile -OutputPath $out16 -Width 16 -Height 16
    } else {
        Write-Warning "High-res source file not found for ${icon}: $inputFile"
    }
}
