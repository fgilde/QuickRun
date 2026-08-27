# Cuts the two GitHub screenshots down to what they are actually showing.
#
# A full browser window scaled into a landing-page card makes the Run button about four pixels
# wide, which is the one thing the picture is there for. The gallery keeps the whole window - the
# context is worth having when someone is looking at screenshots on purpose - and the hero gets the
# part that carries the point.
#
#   pwsh scripts/crop-screenshots.ps1
[CmdletBinding()]
param([string] $Dir = "$PSScriptRoot/../site/public/screenshots")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Crop {
    param([string] $Source, [string] $Target, [int] $X, [int] $Y, [int] $Width, [int] $Height)

    $image = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)

    try {
        # Clamp, so a crop written for one capture cannot throw on a slightly different one.
        $width = [Math]::Min($Width, $image.Width - $X)
        $height = [Math]::Min($Height, $image.Height - $Y)

        $cut = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($cut)

        try {
            $graphics.DrawImage($image,
                (New-Object System.Drawing.Rectangle 0, 0, $width, $height),
                (New-Object System.Drawing.Rectangle $X, $Y, $width, $height),
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $graphics.Dispose()
        }

        $cut.Save($Target, [System.Drawing.Imaging.ImageFormat]::Png)
        $cut.Dispose()
        "{0} -> {1}x{2}" -f (Split-Path $Target -Leaf), $width, $height
    }
    finally {
        $image.Dispose()
    }
}

# The branch row and the Run button beside it - the whole reason the extension exists, at a size
# where it reads. Only that row: the header above it ends in GitHub own buttons, and a crop through
# the middle of those looks like a mistake rather than a detail.
Crop "$Dir/github-button.png" "$Dir/github-button-close.png" 20 268 790 64

# The confirmation window on its own, with the commands and the warning legible.
Crop "$Dir/github-confirm.png" "$Dir/github-confirm-close.png" 22 268 726 524
