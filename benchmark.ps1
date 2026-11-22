# Opens the training Benchmark page (benchmark/page.html) for this repo: one button runs ~10 s of the real training
# loop (training/train_gauntlet.py, the v3 match recipe) and records step time, machine and machine load in
# benchmark/history.json (commit it to compare laptops).
$ErrorActionPreference = 'Stop'
$py = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path $py)) {
    # The trainer's environment as training/README.md sets it up: uv sync --group train, then the CUDA build of torch.
    Write-Host 'Creating the training environment (uv sync --group train)...'
    Push-Location $PSScriptRoot
    try {
        uv sync --group train
        # train_gauntlet.py logs to TensorBoard, which the train group does not list.
        uv pip install --python $py tensorboard
        if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
            Write-Host 'Installing the CUDA build of torch...'
            uv pip install --python $py --reinstall-package torch --no-deps torch --index-url https://download.pytorch.org/whl/cu130
        }
    } finally { Pop-Location }
}
& $py (Join-Path $PSScriptRoot 'benchmark/serve.py') @args
