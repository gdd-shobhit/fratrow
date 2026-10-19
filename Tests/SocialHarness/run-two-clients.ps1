param(
    [Parameter(Mandatory=$true)][string]$Player,
    [string]$Campaign = ('social-test-' + [guid]::NewGuid().ToString('N')),
    [string]$Model = '',
    [string]$LlamaServer = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Player = (Resolve-Path -LiteralPath $Player).Path
if (-not $Model) { $Model = Join-Path $repoRoot 'Library/SocialModels/Qwen_Qwen3.5-2B-Q4_K_M.gguf' }
if (-not $LlamaServer) { $LlamaServer = Join-Path $repoRoot 'Library/SocialNative/llama-cuda/llama-server.exe' }
$output = Join-Path $repoRoot ('Library/SocialMultiplayer/' + $Campaign)
New-Item -ItemType Directory -Force -Path $output | Out-Null
function Quote-Arg([string]$Value) { if ($Value.Contains('"')) { throw 'Invalid path argument' }; '"' + $Value + '"' }
function Start-TestPlayer([string]$Role, [string]$Profile) {
    $args = @('-batchmode','-nographics','--social-brains','--social-local-role',$Role,'--social-profile',$Profile,
        '--social-campaign',$Campaign,'--social-name',$Profile,'--social-model',(Quote-Arg $Model),'--social-llama-server',(Quote-Arg $LlamaServer),
        '--social-inbox',(Quote-Arg (Join-Path $output ($Profile + '-in.json'))),
        '--social-outbox',(Quote-Arg (Join-Path $output ($Profile + '-out.json'))),
        '-logFile',(Quote-Arg (Join-Path $output ($Profile + '.log'))))
    Start-Process -FilePath $Player -ArgumentList $args -WindowStyle Hidden -PassThru
}
function Send-Command([string]$Profile, [hashtable]$Command) {
    $inputFile = Join-Path $output ($Profile + '-in.json')
    $outputFile = Join-Path $output ($Profile + '-out.json')
    $previous = if (Test-Path -LiteralPath $outputFile) { (Get-Item -LiteralPath $outputFile).LastWriteTimeUtc } else { [datetime]::MinValue }
    $Command.requestId = [guid]::NewGuid().ToString('N')
    $temporary = $inputFile + '.tmp'
    $Command | ConvertTo-Json -Compress | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $inputFile -Force
    $deadline = [datetime]::UtcNow.AddSeconds(120)
    do {
        if ((Test-Path -LiteralPath $outputFile) -and (Get-Item -LiteralPath $outputFile).LastWriteTimeUtc -gt $previous) {
            return Get-Content -LiteralPath $outputFile -Raw | ConvertFrom-Json
        }
        Start-Sleep -Milliseconds 200
    } while ([datetime]::UtcNow -lt $deadline)
    throw "No result from $Profile; inspect $output"
}
$hostProcess = Start-TestPlayer 'host' 'p1'
$clientProcess = Start-TestPlayer 'client' 'p2'
try {
    $deadline = [datetime]::UtcNow.AddMinutes(3)
    do {
        $hostLog = Join-Path $output 'p1.log'; $clientLog = Join-Path $output 'p2.log'
        $bound = (Test-Path $hostLog) -and (Test-Path $clientLog) -and
            (Select-String -LiteralPath $hostLog -SimpleMatch 'Local player bound.' -Quiet) -and
            (Select-String -LiteralPath $clientLog -SimpleMatch 'Local player bound.' -Quiet)
        if ($bound) { break }; Start-Sleep -Milliseconds 500
    } while ([datetime]::UtcNow -lt $deadline)
    if (-not $bound) { throw 'Two-client startup failed; inspect development player logs' }
    # Allow roster import and development contact unlock to complete.
    Start-Sleep -Seconds 1
    $insult = Send-Command 'p1' @{ action='talk'; text="You're pathetic. Nobody wants you at our frat house."; npcId='Ashley'; channel='text' }
    if ($insult.status -ne 'committed') { throw 'Insult did not produce a validated decision' }
    $invite = Send-Command 'p2' @{ action='talk'; text="I'm his frat mate, but that was cruel. I am sorry he treated you that way. Would you consider coming? I can meet you at the entrance."; npcId='Ashley'; channel='text' }
    if ($invite.status -ne 'committed') { throw 'Invitation did not produce a validated decision' }
    $before = Send-Command 'p1' @{action='dump'}
    $null = Send-Command 'p1' @{action='start_party'}
    # No player teleport/forged fulfillment: conditional promises expire if neither actual player meets the condition.
    Start-Sleep -Seconds 62
    $after = Send-Command 'p1' @{action='dump'}
    $npc = $after.npcs | Where-Object npcId -eq 'Ashley'
    $commitment = $npc.commitments | Select-Object -Last 1
    if ($commitment.state -eq 'accepted' -and -not $commitment.arrived) { throw 'Accepted NPC did not arrive' }
    if ($commitment.state -in @('declined','deferred','missed') -and $commitment.arrived) { throw 'Nonaccepted NPC arrived' }
    @{campaign=$Campaign; insult=$insult; invite=$invite; before=$before; after=$after; humanReviewRequired=$true} |
        ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    Write-Output "Two-client transport/state checks passed. Human attribution and dialogue review required: $output"
}
finally {
    # Stop only the two processes created by this script. Campaign saves remain for reload verification.
    foreach ($owned in @($clientProcess,$hostProcess)) {
        if (-not $owned.HasExited) {
            Get-CimInstance Win32_Process -Filter ('ParentProcessId=' + $owned.Id) | Where-Object Name -eq 'llama-server.exe' |
                ForEach-Object { Stop-Process -Id $_.ProcessId }
            Stop-Process -Id $owned.Id
        }
    }
}
