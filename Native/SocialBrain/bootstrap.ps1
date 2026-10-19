param([switch]$Models, [switch]$CudaFallback, [switch]$Compare4B)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$cache = Join-Path $repoRoot 'Library/SocialNative'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Hash) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Hash) { throw "Checksum mismatch: $Path" }
}
$sdkFile = 'RunAnywhere-cpp-desktop-windows-x64-v0.20.39.tar.gz'
Get-VerifiedFile "https://github.com/RunanywhereAI/runanywhere-sdks/releases/download/cpp-desktop-v0.20.39/$sdkFile" (Join-Path $cache $sdkFile) '917c0798706f237a0668f45b1a78915d3b44150b19659347a4641ea7830bf51a'
& tar -xf (Join-Path $cache $sdkFile) -C $cache
if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed' }
# The release's MSVC libraries require a current toolset (VS 2026 works; old 14.38 does not).
& cmake -S $PSScriptRoot -B (Join-Path $cache 'bridge-vs18') -G 'Visual Studio 18 2026' -A x64 "-DRUNANYWHERE_ROOT=$cache/cpp-desktop-windows-x64"
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed' }
& cmake --build (Join-Path $cache 'bridge-vs18') --config Release
if ($LASTEXITCODE -ne 0) { throw 'Native bridge build failed' }
$plugins = Join-Path $repoRoot 'Assets/Plugins/x86_64'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item -LiteralPath (Join-Path $cache 'bridge-vs18/Release/FratRowSocialNative.dll') -Destination (Join-Path $plugins 'FratRowSocialNative.dll')
# Native binaries and models are deliberately generated/ignored, not committed.
if ($Models) {
    $modelCache = Join-Path $repoRoot 'Library/SocialModels'
    New-Item -ItemType Directory -Force -Path $modelCache | Out-Null
    Get-VerifiedFile 'https://huggingface.co/bartowski/Qwen_Qwen3.5-2B-GGUF/resolve/7d26695454df6de5fbcce2e58681e62dae06ce43/Qwen_Qwen3.5-2B-Q4_K_M.gguf' (Join-Path $modelCache 'Qwen_Qwen3.5-2B-Q4_K_M.gguf') '57a1085840f497d764a7fc5d346922dbde961efb54cc792ea81d694fd846a1d8'
    Get-VerifiedFile 'https://huggingface.co/openbmb/MiniCPM5-1B-GGUF/resolve/3d55fac80935ae6456986ad2384b5cbcc4d6c948/MiniCPM5-1B-Q4_K_M.gguf' (Join-Path $modelCache 'MiniCPM5-1B-Q4_K_M.gguf') '81b64d05a23b17b34c475f42b3e72fbde62d4b92cc34541f7a8031d0752deafa'
}
if ($CudaFallback) {
    $cuda = Join-Path $cache 'llama-cuda'
    New-Item -ItemType Directory -Force -Path $cuda | Out-Null
    foreach ($asset in @(
        @('llama-b11718-bin-win-cuda-12.4-x64.zip','c13fda0ef74f47cd2bccda1eea2a62980c12a5778d39b39bedadb525b4e81aff'),
        @('cudart-llama-bin-win-cuda-12.4-x64.zip','8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6')
    )) {
        $archive = Join-Path $cache $asset[0]
        Get-VerifiedFile ('https://github.com/ggml-org/llama.cpp/releases/download/b11718/' + $asset[0]) $archive $asset[1]
        Expand-Archive -LiteralPath $archive -DestinationPath $cuda -Force
    }
}
if ($Compare4B) {
    $modelCache = Join-Path $repoRoot 'Library/SocialModels'
    New-Item -ItemType Directory -Force -Path $modelCache | Out-Null
    Get-VerifiedFile 'https://huggingface.co/bartowski/Qwen_Qwen3.5-4B-GGUF/resolve/4168f45a16a1290d65a4ec0fa312ae917a4c15d6/Qwen_Qwen3.5-4B-Q4_K_M.gguf' (Join-Path $modelCache 'Qwen_Qwen3.5-4B-Q4_K_M.gguf') '13c16f426047e2de38cd075bdade4a7bcbc8c774384876f677740cda65f8a983'
}
Write-Output 'Social runtime prepared. Run tests and review Docs/SocialBrains.md before enabling it.'
