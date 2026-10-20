<#
.SYNOPSIS
    Installs the local NPC dialogue runtime: llama.cpp builds and the pinned GGUF models (ADR-0004).

.DESCRIPTION
    Every download is verified against a pinned SHA256 before use. Binaries and models land in the gitignored
    Library/ folder and are never committed:
      Library/SocialNative/llama-cuda    NVIDIA GPUs (CUDA 12.4 build + its CUDA runtime, ~630 MB)
      Library/SocialNative/llama-vulkan  any GPU with Vulkan: AMD, Intel, NVIDIA (~30 MB)
      Library/SocialNative/llama-cpu     no usable GPU; slow (~18 MB)
      Library/SocialModels               Qwen3.5 2B (default), MiniCPM 1B, optional Qwen3.5 4B
    All builds come from one llama.cpp release (b11718) and expose the same llama-server API, so the game's
    LlamaServerModelProvider runs unchanged on any of them; the host picks one with LlamaRuntimes.Resolve.

.PARAMETER Runtime
    auto (default: CUDA when an NVIDIA GPU is present, otherwise Vulkan), cuda, vulkan, cpu or all. Several allowed.
.PARAMETER Models
    Also download the Qwen3.5 2B and MiniCPM 1B models.
.PARAMETER Compare4B
    Also download the optional Qwen3.5 4B model.
.PARAMETER CudaFallback
    Deprecated alias for -Runtime cuda.

.EXAMPLE
    pwsh -File tools/social/bootstrap.ps1 -Models
.EXAMPLE
    pwsh -File tools/social/bootstrap.ps1 -Runtime vulkan,cpu
#>
param([string[]]$Runtime = @('auto'), [switch]$Models, [switch]$Compare4B, [switch]$CudaFallback)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$cache = Join-Path $repoRoot 'Library/SocialNative'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Hash) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Hash) { throw "Checksum mismatch: $Path" }
}

# One llama.cpp release for every backend; hashes are the release's published asset digests.
$release = 'b11718'
$builds = @{
    cuda   = @(@("llama-$release-bin-win-cuda-12.4-x64.zip", 'c13fda0ef74f47cd2bccda1eea2a62980c12a5778d39b39bedadb525b4e81aff'),
               @('cudart-llama-bin-win-cuda-12.4-x64.zip', '8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6'))
    vulkan = @(,@("llama-$release-bin-win-vulkan-x64.zip", '237e5736031a9cdcf9eff54e1f4e9bd871945de1d56ec542ecb9ebaf8d45bdb6'))
    cpu    = @(,@("llama-$release-bin-win-cpu-x64.zip", '92cc529b29a43f9a30e21d490cb2e95062acd0b59af6fee8b3b52b28074f838c'))
}

$wanted = New-Object System.Collections.Generic.List[string]
if ($CudaFallback) { $wanted.Add('cuda') }
foreach ($r in ($Runtime | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })) {
    switch ($r) {
        'auto' {
            $nvidia = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'NVIDIA' }).Count -gt 0
            $wanted.Add($(if ($nvidia) { 'cuda' } else { 'vulkan' }))
        }
        'all' { 'cuda', 'vulkan', 'cpu' | ForEach-Object { $wanted.Add($_) } }
        { $builds.ContainsKey($_) } { $wanted.Add($r) }
        default { throw "Unknown -Runtime '$r' (use auto, cuda, vulkan, cpu or all)" }
    }
}
foreach ($backend in ($wanted | Select-Object -Unique)) {
    $target = Join-Path $cache "llama-$backend"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($asset in $builds[$backend]) {
        $archive = Join-Path $cache $asset[0]
        Get-VerifiedFile "https://github.com/ggml-org/llama.cpp/releases/download/$release/$($asset[0])" $archive $asset[1]
        Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'llama-server.exe'))) { throw "llama-server.exe missing after extracting the $backend build" }
    Write-Output "llama.cpp $release $backend build ready: $target"
}

if ($Models) {
    $modelCache = Join-Path $repoRoot 'Library/SocialModels'
    New-Item -ItemType Directory -Force -Path $modelCache | Out-Null
    Get-VerifiedFile 'https://huggingface.co/bartowski/Qwen_Qwen3.5-2B-GGUF/resolve/7d26695454df6de5fbcce2e58681e62dae06ce43/Qwen_Qwen3.5-2B-Q4_K_M.gguf' (Join-Path $modelCache 'Qwen_Qwen3.5-2B-Q4_K_M.gguf') '57a1085840f497d764a7fc5d346922dbde961efb54cc792ea81d694fd846a1d8'
    Get-VerifiedFile 'https://huggingface.co/openbmb/MiniCPM5-1B-GGUF/resolve/3d55fac80935ae6456986ad2384b5cbcc4d6c948/MiniCPM5-1B-Q4_K_M.gguf' (Join-Path $modelCache 'MiniCPM5-1B-Q4_K_M.gguf') '81b64d05a23b17b34c475f42b3e72fbde62d4b92cc34541f7a8031d0752deafa'
}
if ($Compare4B) {
    $modelCache = Join-Path $repoRoot 'Library/SocialModels'
    New-Item -ItemType Directory -Force -Path $modelCache | Out-Null
    Get-VerifiedFile 'https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF/resolve/4168f45a16a1290d65a4ec0fa312ae917a4c15d6/Qwen_Qwen3.5-4B-Q4_K_M.gguf' (Join-Path $modelCache 'Qwen_Qwen3.5-4B-Q4_K_M.gguf') '13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983'
}
Write-Output 'Social runtime prepared. In Unity: FratRow > Social Brains > Enable development experiment. Docs: Docs/SocialBrains.md'
