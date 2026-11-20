# Install a speech model on Windows

Chit-Chat runs speech recognition on your computer once the model is installed.

## Install from Chit-Chat

Open **Speech engine**, choose **Parakeet v2** for English or **Parakeet v3** for
multilingual dictation, then select **Download model**. Allow roughly 700 MB for the
model files. Chit-Chat shows progress, verifies the download, and selects the new
model when it is ready. You can cancel while the files are downloading.

For live transcription in English, choose **Zipformer streaming for English**.
It downloads about 73 MB and shows partial text while you speak. Turn **Live English
transcription** off to switch back to your selected Parakeet model.

If you already have a model, select **Browse** and choose the folder containing the
three ONNX files and `tokens.txt`. Choose **Use automatic location** to let Chit-Chat
look for an existing model in its default locations.

## Download manually

Run this in PowerShell. Set `$variant` to `"v3"` for the multilingual version.

```powershell
$variant = "v2"
$destination = Join-Path $env:LOCALAPPDATA "ChitChat\models\parakeet-$variant"
$repository = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-$variant-int8"
$revision = @{ v2 = "84b478d541cdbf0c296a7ab5f1e20a6f8f998114"; v3 = "ed5d745623472f97cfc2e6c2916ee248798fc5a8" }[$variant]
$source = "https://huggingface.co/$repository/resolve/$revision"
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
standard v2 folder automatically. To use a manually downloaded v3 model or keep the
files elsewhere, select the model directory in **Speech engine**.

If Chit-Chat reports a missing model, check the names and make sure the encoder download
completed. All three ONNX files and `tokens.txt` must come from the same model variant.

## Model credits

NVIDIA publishes [Parakeet v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) and
[Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The downloads above are the
sherpa-onnx conversions, quantized to int8. Chit-Chat uses
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) to load the four files at runtime.

The optional [English streaming Zipformer](https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/tree/672fbf1b30579d6585301139bb363f42a0ad4a24)
comes from the sherpa-onnx project under the Apache 2.0 license. Chit-Chat installs
its int8 model files and token vocabulary. The app includes a word-piece vocabulary
exported from the pinned model to support dictionary hints and installs it alongside
a copy of the model's license.
