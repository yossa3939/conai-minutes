[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'meeting.wav'),
    [int]$Seconds = 3,
    [int]$SampleRate = 48000,
    [double]$Frequency = 440
)

$ErrorActionPreference = 'Stop'
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$totalSamples = $SampleRate * $Seconds
$dataBytes = $totalSamples * 2

$stream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataBytes))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$SampleRate)
    $writer.Write([int]($SampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$dataBytes)

    for ($i = 0; $i -lt $totalSamples; $i++) {
        $value = [math]::Sin(2 * [math]::PI * $Frequency * $i / $SampleRate) * 12000
        $writer.Write([int16][math]::Round($value))
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "作成しました: $OutputPath ($Seconds 秒 / $SampleRate Hz / モノラル 16bit)"
