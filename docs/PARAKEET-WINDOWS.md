# Install Parakeet for Windows

Chit-Chat runs speech recognition on your computer once the model is installed. Use the
[English Parakeet v2 download](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8)
for English dictation or the [multilingual v3 download](https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8)
for its other supported languages. Allow roughly 700 MB for the model files.

## Download

Run this in PowerShell. Set `$variant` to `"v3"` for the multilingual version.

```powershell
$variant = "v2"
$destination = Join-Path $env:LOCALAPPDATA "ChitChat\models\parakeet-$variant"
$repository = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-$variant-int8"
$source = "https://huggingface.co/$repository/resolve/main"
$assets = @("encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt")

New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($asset in $assets) {
    $file = Join-Path $destination $asset
    $temporaryFile = "$file.partial"
    try {
        curl.exe --fail --location --output $temporaryFile "$source/$asset"
        if ($LASTEXITCODE -ne 0) { throw "Download failed for $asset" }
        Move-Item -LiteralPath $temporaryFile -Destination $file -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile
        }
    }
}
```

The model directory is the folder containing all four files. Chit-Chat looks for the
standard v2 folder automatically. To use v3 or keep the files elsewhere, select the
model directory in Settings and check that the app recognizes it.

If Chit-Chat reports a missing model, check the names and make sure the encoder download
completed. All three ONNX files and `tokens.txt` must come from the same model variant.

## Model credits

NVIDIA publishes [Parakeet v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) and
[Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The downloads above are the
sherpa-onnx conversions, quantized to int8. Chit-Chat uses
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) to load the four files at runtime.
