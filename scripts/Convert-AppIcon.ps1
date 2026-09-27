param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$inputImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Source).Path)
try {
    $frames = @()
    foreach ($size in @(16,24,32,48,64,128,256)) {
        $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $ratio = [Math]::Min($size / $inputImage.Width, $size / $inputImage.Height)
            $width = [single]($inputImage.Width * $ratio); $height = [single]($inputImage.Height * $ratio)
            $graphics.DrawImage($inputImage, [single](($size-$width)/2), [single](($size-$height)/2), $width, $height)
            $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{Size=$size; Bytes=$stream.ToArray()}
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [System.IO.File]::Create($Destination)
    $writer = [System.IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $output.Dispose() }
} finally { $inputImage.Dispose() }
