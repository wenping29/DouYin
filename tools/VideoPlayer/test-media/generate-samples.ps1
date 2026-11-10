$ErrorActionPreference = 'Stop'

$width = 160
$height = 90
$frameRate = 12
$frameCount = $frameRate * 4
$stride = ($width * 3 + 3) -band (-bnot 3)
$frameSize = $stride * $height
$outputDirectory = $PSScriptRoot

function New-Chunk {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][byte[]]$Payload
    )

    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes($Id))
    $writer.Write([uint32]$Payload.Length)
    $writer.Write($Payload)
    if (($Payload.Length -band 1) -ne 0) {
        $writer.Write([byte]0)
    }

    $writer.Flush()
    $chunk = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,$chunk
}

function New-ListChunk {
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][byte[][]]$Chunks
    )

    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes($Type))
    foreach ($chunk in $Chunks) {
        $writer.Write($chunk)
    }

    $writer.Flush()
    $payload = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,(New-Chunk -Id 'LIST' -Payload $payload)
}

function New-AviHeaderChunk {
    param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][scriptblock]$WriteFields)

    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    & $WriteFields $writer
    $writer.Flush()
    $payload = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,(New-Chunk -Id $Id -Payload $payload)
}

$mainHeader = New-AviHeaderChunk 'avih' {
    param($writer)
    $writer.Write([uint32](1000000 / $frameRate))
    $writer.Write([uint32]($frameSize * $frameRate))
    $writer.Write([uint32]0)
    $writer.Write([uint32]0x10)
    $writer.Write([uint32]$frameCount)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$frameSize)
    $writer.Write([uint32]$width)
    $writer.Write([uint32]$height)
    1..4 | ForEach-Object { $writer.Write([uint32]0) }
}

$streamHeader = New-AviHeaderChunk 'strh' {
    param($writer)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('vids'))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('DIB '))
    $writer.Write([uint32]0)
    $writer.Write([uint16]0)
    $writer.Write([uint16]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$frameRate)
    $writer.Write([uint32]0)
    $writer.Write([uint32]$frameCount)
    $writer.Write([uint32]$frameSize)
    $writer.Write([uint32]::MaxValue)
    $writer.Write([uint32]0)
    $writer.Write([int16]0)
    $writer.Write([int16]0)
    $writer.Write([int16]$width)
    $writer.Write([int16]$height)
}

$bitmapHeader = New-AviHeaderChunk 'strf' {
    param($writer)
    $writer.Write([uint32]40)
    $writer.Write([int32]$width)
    $writer.Write([int32]$height)
    $writer.Write([uint16]1)
    $writer.Write([uint16]24)
    $writer.Write([uint32]0)
    $writer.Write([uint32]$frameSize)
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)
}

$streamList = New-ListChunk -Type 'strl' -Chunks ([byte[][]]@($streamHeader, $bitmapHeader))
$headerList = New-ListChunk -Type 'hdrl' -Chunks ([byte[][]]@($mainHeader, $streamList))
$aviFrames = [System.Collections.Generic.List[byte[]]]::new()
$y4m = [System.IO.File]::Create((Join-Path $outputDirectory 'sample.y4m'))
$y4mHeader = [System.Text.Encoding]::ASCII.GetBytes(
    "YUV4MPEG2 W${width} H${height} F${frameRate}:1 Ip A1:1 C420jpeg`n")
$y4m.Write($y4mHeader, 0, $y4mHeader.Length)

$palette = @(
    @(255, 255, 255), @(255, 255, 0), @(0, 255, 255),
    @(0, 255, 0), @(255, 0, 255), @(255, 0, 0), @(0, 0, 255)
)
$chromaWidth = [int]($width / 2)
$chromaHeight = [int]($height / 2)
$chromaSize = $chromaWidth * $chromaHeight

try {
    for ($frame = 0; $frame -lt $frameCount; $frame++) {
        $rgb = [byte[]]::new($width * $height * 3)
        $yPlane = [byte[]]::new($width * $height)
        $uPlane = [byte[]]::new($chromaSize)
        $vPlane = [byte[]]::new($chromaSize)
        $aviFrame = [byte[]]::new($frameSize)
        $movingX = ($frame * 4) % ($width - 24)

        for ($y = 0; $y -lt $height; $y++) {
            for ($x = 0; $x -lt $width; $x++) {
                $bar = [Math]::Min(6, [int]($x * 7 / $width))
                $color = $palette[$bar]
                if ($x -ge $movingX -and $x -lt ($movingX + 24) -and $y -ge 32 -and $y -lt 58) {
                    $color = @(32, 32, 32)
                }

                $red = $color[0]
                $green = $color[1]
                $blue = $color[2]
                $pixel = ($y * $width) + $x
                $rgbOffset = $pixel * 3
                $rgb[$rgbOffset] = [byte]$red
                $rgb[$rgbOffset + 1] = [byte]$green
                $rgb[$rgbOffset + 2] = [byte]$blue

                $yValue = [Math]::Max(0, [Math]::Min(255, [int][Math]::Round(16 + 0.257 * $red + 0.504 * $green + 0.098 * $blue)))
                $yPlane[$pixel] = [byte]$yValue

                if (($x % 2) -eq 0 -and ($y % 2) -eq 0) {
                    $sumU = 0.0
                    $sumV = 0.0
                    for ($dy = 0; $dy -lt 2; $dy++) {
                        for ($dx = 0; $dx -lt 2; $dx++) {
                            $sampleX = $x + $dx
                            $sampleY = $y + $dy
                            $sampleBar = [Math]::Min(6, [int]($sampleX * 7 / $width))
                            $sampleColor = $palette[$sampleBar]
                            if ($sampleX -ge $movingX -and $sampleX -lt ($movingX + 24) -and $sampleY -ge 32 -and $sampleY -lt 58) {
                                $sampleColor = @(32, 32, 32)
                            }

                            $r = $sampleColor[0]
                            $g = $sampleColor[1]
                            $b = $sampleColor[2]
                            $sumU += 128 - 0.148 * $r - 0.291 * $g + 0.439 * $b
                            $sumV += 128 + 0.439 * $r - 0.368 * $g - 0.071 * $b
                        }
                    }

                    $chromaIndex = [int](($y / 2) * $chromaWidth + ($x / 2))
                    $uPlane[$chromaIndex] = [byte][Math]::Max(0, [Math]::Min(255, [int][Math]::Round($sumU / 4)))
                    $vPlane[$chromaIndex] = [byte][Math]::Max(0, [Math]::Min(255, [int][Math]::Round($sumV / 4)))
                }
            }
        }

        for ($row = 0; $row -lt $height; $row++) {
            $sourceOffset = ($height - 1 - $row) * $width * 3
            $destinationOffset = $row * $stride
            for ($column = 0; $column -lt ($width * 3); $column++) {
                $aviFrame[$destinationOffset + $column] = $rgb[$sourceOffset + $column]
            }
        }

        $aviFrames.Add((New-Chunk -Id '00db' -Payload $aviFrame))
        $frameMarker = [System.Text.Encoding]::ASCII.GetBytes("FRAME`n")
        $y4m.Write($frameMarker, 0, $frameMarker.Length)
        $y4m.Write($yPlane, 0, $yPlane.Length)
        $y4m.Write($uPlane, 0, $uPlane.Length)
        $y4m.Write($vPlane, 0, $vPlane.Length)
    }
}
finally {
    $y4m.Dispose()
}

$moviStream = [System.IO.MemoryStream]::new()
$moviWriter = [System.IO.BinaryWriter]::new($moviStream)
$moviWriter.Write([System.Text.Encoding]::ASCII.GetBytes('movi'))
$indexEntries = [System.IO.MemoryStream]::new()
$indexWriter = [System.IO.BinaryWriter]::new($indexEntries)
$offset = 4

foreach ($frameChunk in $aviFrames) {
    $moviWriter.Write($frameChunk)
    $indexWriter.Write([System.Text.Encoding]::ASCII.GetBytes('00db'))
    $indexWriter.Write([uint32]0x10)
    $indexWriter.Write([uint32]$offset)
    $indexWriter.Write([uint32]$frameSize)
    $offset += $frameChunk.Length
}

$moviWriter.Flush()
$indexWriter.Flush()
$moviPayload = $moviStream.ToArray()
$indexPayload = $indexEntries.ToArray()
$moviWriter.Dispose()
$indexWriter.Dispose()
$moviStream.Dispose()
$indexEntries.Dispose()

$moviList = New-Chunk -Id 'LIST' -Payload $moviPayload
$indexChunk = New-Chunk -Id 'idx1' -Payload $indexPayload
$aviBodyStream = [System.IO.MemoryStream]::new()
$aviBodyWriter = [System.IO.BinaryWriter]::new($aviBodyStream)
$aviBodyWriter.Write([System.Text.Encoding]::ASCII.GetBytes('AVI '))
$aviBodyWriter.Write($headerList)
$aviBodyWriter.Write($moviList)
$aviBodyWriter.Write($indexChunk)
$aviBodyWriter.Flush()
$aviBody = $aviBodyStream.ToArray()
$aviBodyWriter.Dispose()
$aviBodyStream.Dispose()

$aviFile = [System.IO.File]::Create((Join-Path $outputDirectory 'sample-uncompressed.avi'))
$aviWriter = [System.IO.BinaryWriter]::new($aviFile)
$aviWriter.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
$aviWriter.Write([uint32]$aviBody.Length)
$aviWriter.Write($aviBody)
$aviWriter.Dispose()
$aviFile.Dispose()

Write-Output "Generated $frameCount frames at ${width}x${height}, ${frameRate} fps:"
Get-Item (Join-Path $outputDirectory 'sample-uncompressed.avi'), (Join-Path $outputDirectory 'sample.y4m') |
    Select-Object Name, Length
