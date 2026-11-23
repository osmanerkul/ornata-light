$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [float]$x,
        [float]$y,
        [float]$w,
        [float]$h,
        [float]$r
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2.0, [Math]::Min($w, $h))
    if ($d -le 0.5) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h)))
        return $path
    }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-KeycapIconBitmap {
    param([int]$Size)

    $s = [float]$Size
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        # 1. Rounded-square dark carbon tile
        $pad = [float][Math]::Max(1.0, $s * 0.0625)
        $tileW = $s - ($pad * 2.0)
        $tileH = $s - ($pad * 2.0)
        $tileR = [float][Math]::Max(2.5, $s * 0.21)
        $tileRect = New-Object System.Drawing.RectangleF($pad, $pad, $tileW, $tileH)
        $tilePath = New-RoundedRectPath -x $pad -y $pad -w $tileW -h $tileH -r $tileR
        try {
            $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                $tileRect,
                [System.Drawing.Color]::FromArgb(255, 29, 32, 40),
                [System.Drawing.Color]::FromArgb(255, 14, 16, 21),
                90.0
            )
            try {
                $g.FillPath($tileBrush, $tilePath)
            } finally {
                $tileBrush.Dispose()
            }

            $rimPen = New-Object System.Drawing.Pen(
                [System.Drawing.Color]::FromArgb(255, 60, 66, 82),
                [float][Math]::Max(1.0, $s * 0.035)
            )
            try {
                $g.DrawPath($rimPen, $tilePath)
            } finally {
                $rimPen.Dispose()
            }
        } finally {
            $tilePath.Dispose()
        }

        # Keycap geometry
        $kx = [float]($s * 0.175)
        $ky = [float]($s * 0.165)
        $kw = [float]($s * 0.65)
        $kh = [float]($s * 0.65)
        $kr = [float][Math]::Max(2.0, $s * 0.13)

        # 2. Soft luminous white backlight glow behind/under the keycap
        $glowLayers = @(
            @{ Expand = [float]($s * 0.10); ShiftY = [float]($s * 0.025); Alpha = 28 },
            @{ Expand = [float]($s * 0.06); ShiftY = [float]($s * 0.020); Alpha = 58 },
            @{ Expand = [float]($s * 0.03); ShiftY = [float]($s * 0.015); Alpha = 115 }
        )
        foreach ($layer in $glowLayers) {
            $ex = $layer.Expand
            $glowPath = New-RoundedRectPath -x ($kx - $ex) -y ($ky - $ex + $layer.ShiftY) -w ($kw + 2.0 * $ex) -h ($kh + 2.0 * $ex) -r ($kr + $ex * 0.6)
            try {
                $glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($layer.Alpha, 255, 255, 255))
                try {
                    $g.FillPath($glowBrush, $glowPath)
                } finally {
                    $glowBrush.Dispose()
                }
            } finally {
                $glowPath.Dispose()
            }
        }

        # 3. Sculpted dark keycap skirt
        $skirtRect = New-Object System.Drawing.RectangleF($kx, $ky, $kw, $kh)
        $skirtPath = New-RoundedRectPath -x $kx -y $ky -w $kw -h $kh -r $kr
        try {
            $skirtBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                $skirtRect,
                [System.Drawing.Color]::FromArgb(255, 43, 48, 59),
                [System.Drawing.Color]::FromArgb(255, 24, 27, 35),
                90.0
            )
            try {
                $g.FillPath($skirtBrush, $skirtPath)
            } finally {
                $skirtBrush.Dispose()
            }

            $skirtPen = New-Object System.Drawing.Pen(
                [System.Drawing.Color]::FromArgb(200, 255, 255, 255),
                [float][Math]::Max(1.0, $s * 0.03)
            )
            try {
                $g.DrawPath($skirtPen, $skirtPath)
            } finally {
                $skirtPen.Dispose()
            }
        } finally {
            $skirtPath.Dispose()
        }

        # Luminous white underglow bar along bottom edge of keycap
        $uPad = [float]($s * 0.04)
        $uH = [float][Math]::Max(1.5, $s * 0.055)
        $uPath = New-RoundedRectPath -x ($kx + $uPad) -y ($ky + $kh - $uH - [float]($s * 0.015)) -w ($kw - 2.0 * $uPad) -h $uH -r ($uH * 0.5)
        try {
            $uBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
            try {
                $g.FillPath($uBrush, $uPath)
            } finally {
                $uBrush.Dispose()
            }
        } finally {
            $uPath.Dispose()
        }

        # Beveled top dish
        $dx = [float]($s * 0.24)
        $dy = [float]($s * 0.20)
        $dw = [float]($s * 0.52)
        $dh = [float]($s * 0.47)
        $dr = [float][Math]::Max(1.5, $s * 0.095)
        $dishRect = New-Object System.Drawing.RectangleF($dx, $dy, $dw, $dh)
        $dishPath = New-RoundedRectPath -x $dx -y $dy -w $dw -h $dh -r $dr
        try {
            $dishBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                $dishRect,
                [System.Drawing.Color]::FromArgb(255, 25, 28, 36),
                [System.Drawing.Color]::FromArgb(255, 34, 38, 49),
                90.0
            )
            try {
                $g.FillPath($dishBrush, $dishPath)
            } finally {
                $dishBrush.Dispose()
            }

            $dishPen = New-Object System.Drawing.Pen(
                [System.Drawing.Color]::FromArgb(75, 255, 255, 255),
                [float][Math]::Max(1.0, $s * 0.02)
            )
            try {
                $g.DrawPath($dishPen, $dishPath)
            } finally {
                $dishPen.Dispose()
            }
        } finally {
            $dishPath.Dispose()
        }

        # 4. Glowing white LED light-bar centered on the keycap face
        $barW = [float]($s * 0.30)
        $barH = [float][Math]::Max(2.0, $s * 0.085)
        $barX = [float](($s - $barW) * 0.5)
        $barY = [float]($dy + ($dh - $barH) * 0.5)

        $haloPad = [float][Math]::Max(1.0, $s * 0.03)
        $barHaloPath = New-RoundedRectPath -x ($barX - $haloPad) -y ($barY - $haloPad) -w ($barW + 2.0 * $haloPad) -h ($barH + 2.0 * $haloPad) -r (($barH + 2.0 * $haloPad) * 0.5)
        try {
            $barHaloBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 255, 255, 255))
            try {
                $g.FillPath($barHaloBrush, $barHaloPath)
            } finally {
                $barHaloBrush.Dispose()
            }
        } finally {
            $barHaloPath.Dispose()
        }

        $barPath = New-RoundedRectPath -x $barX -y $barY -w $barW -h $barH -r ($barH * 0.5)
        try {
            $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
            try {
                $g.FillPath($barBrush, $barPath)
            } finally {
                $barBrush.Dispose()
            }
        } finally {
            $barPath.Dispose()
        }

        return $bmp
    } catch {
        $bmp.Dispose()
        throw
    } finally {
        $g.Dispose()
    }
}

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$icoPath = Join-Path $dir 'OrnataLight.ico'
$sizes = @(16, 32, 48, 256)
$frames = @()

foreach ($sz in $sizes) {
    $bmp = New-KeycapIconBitmap -Size $sz
    try {
        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,@{ Size = $sz; Bytes = $ms.ToArray() }
        } finally {
            $ms.Dispose()
        }
    } finally {
        $bmp.Dispose()
    }
}

$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR header (6 bytes)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { [byte]0 } else { [byte]$f.Size }
        $bw.Write($dim)                    # bWidth
        $bw.Write($dim)                    # bHeight
        $bw.Write([byte]0)                 # bColorCount
        $bw.Write([byte]0)                 # bReserved
        $bw.Write([uint16]1)               # wPlanes
        $bw.Write([uint16]32)              # wBitCount
        $bw.Write([uint32]$f.Bytes.Length) # dwBytesInRes
        $bw.Write([uint32]$offset)         # dwImageOffset
        $offset += $f.Bytes.Length
    }

    foreach ($f in $frames) {
        $bw.Write($f.Bytes)
    }
} finally {
    $bw.Dispose()
    $fs.Dispose()
}

$icoInfo = Get-Item $icoPath
Write-Output ("Generated {0} ({1} bytes, frames: {2})" -f $icoInfo.FullName, $icoInfo.Length, ($sizes -join ','))
