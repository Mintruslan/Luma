$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetFolder = Join-Path $PSScriptRoot '..\src\Assets'
New-Item -ItemType Directory -Force -Path $assetFolder | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 256,256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(13,11,29))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(191,159,255)),14
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.FillEllipse($background,4,4,248,248)
$graphics.DrawLine($pen,68,101,68,68)
$graphics.DrawLine($pen,68,68,101,68)
$graphics.DrawLine($pen,155,68,188,68)
$graphics.DrawLine($pen,188,68,188,101)
$graphics.DrawLine($pen,188,155,188,188)
$graphics.DrawLine($pen,188,188,155,188)
$graphics.DrawLine($pen,101,188,68,188)
$graphics.DrawLine($pen,68,188,68,155)
$coral = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(222,164,244))
$graphics.FillEllipse($coral,112,112,32,32)
$stream = New-Object System.IO.MemoryStream
$bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
$data = $stream.ToArray()
$output = [System.IO.File]::Create((Join-Path $assetFolder 'luma.ico'))
$writer = New-Object System.IO.BinaryWriter $output
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$data.Length)
$writer.Write([uint32]22)
$writer.Write($data)
$writer.Dispose()
$graphics.Dispose()
$background.Dispose()
$pen.Dispose()
$coral.Dispose()
$bitmap.Dispose()
$stream.Dispose()
