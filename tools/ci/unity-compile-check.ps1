<#
.SYNOPSIS
    Headless C# compile gate for the Unity scripts (no Unity editor needed).

.DESCRIPTION
    Generates one SDK-style project per Unity assembly into an ignored folder
    (default: <repo>\Temp\CompileCheck) and compiles them with `dotnet build`:

      * one project per .asmdef under Assets/ (any number, with or without
        noEngineReferences; references listed in the asmdef become project
        references, Assembly-CSharp / Assembly-CSharp-Editor reference every
        auto-referenced asmdef),
      * Assembly-CSharp-firstpass / Assembly-CSharp-Editor-firstpass when
        scripts exist under Assets/Plugins, Assets/Standard Assets, ...,
      * Assembly-CSharp       (all other scripts, not in an Editor folder),
      * Assembly-CSharp-Editor (scripts in Editor folders).

    Compile items are globbed from disk on every run, so new .cs files are
    picked up automatically. References, defines, LangVersion, NoWarn and
    analyzers are copied from the Unity-generated *.csproj files (repo root
    first, then -UnityProjectDir). An asmdef that has no Unity-generated csproj
    yet (new asmdef, Unity not reopened) gets one synthesized from a template
    csproj. Package assemblies are referenced as compiled DLLs from
    Library\ScriptAssemblies, never rebuilt.

    Exit codes: 0 = compiles, 1 = compile errors, 2 = environment/setup
    problem, 3 = dotnet failed without a parsable diagnostic.

.PARAMETER RepoRoot
    Repo/worktree root (contains Assets/). Default: two levels above this script.
.PARAMETER UnityProjectDir
    Fallback folder holding Unity-generated *.csproj and Library\ when the repo
    root has none (git worktrees). Default: D:\fratrow
.PARAMETER OutDir
    Generated projects, obj, bin, logs. Default: <RepoRoot>\Temp\CompileCheck
.PARAMETER MaxErrors
    Cap on printed errors (all are written to <OutDir>\logs). Default 100.
.PARAMETER NoAnalyzers
    Drop Unity's analyzers / source generators from the build.
.PARAMETER Incremental
    Keep obj/bin between runs. Faster, but warnings of up-to-date assemblies
    are not re-reported. Default is a clean, full compile.
.PARAMETER SharedCompilation
    Use the Roslyn compiler server. Default is off, so no long-lived process
    keeps Unity's DLLs open while the editor recompiles.
.PARAMETER ShowWarnings
    Print every distinct warning, not only the count and top codes.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\unity-compile-check.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$UnityProjectDir = 'D:\fratrow',
    [string]$OutDir,
    [int]$MaxErrors = 100,
    [switch]$NoAnalyzers,
    [switch]$Incremental,
    [switch]$SharedCompilation,
    [switch]$ShowWarnings
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:Clock = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------- helpers ---
function Stop-Check([string]$Message, [int]$Code = 2) {
    [Console]::Error.WriteLine("unity-compile-check: ERROR: $Message")
    exit $Code
}

function Get-Prop($Obj, [string]$Name, $Default = $null) {
    if ($null -eq $Obj) { return $Default }
    $p = $Obj.PSObject.Properties[$Name]
    if ($null -eq $p -or $null -eq $p.Value) { return $Default }
    return $p.Value
}

function Get-RelPath([string]$Base, [string]$Path) {
    $b = $Base.TrimEnd('\') + '\'
    if ($Path.StartsWith($b, [System.StringComparison]::OrdinalIgnoreCase)) { return $Path.Substring($b.Length) }
    return $Path
}

# Escape characters that MSBuild treats specially inside Include="..."
function ConvertTo-MSBuildLiteral([string]$s) {
    $s = $s.Replace('%', '%25').Replace('*', '%2A').Replace('?', '%3F')
    return $s.Replace('@', '%40').Replace('$', '%24').Replace(';', '%3B')
}

# Unity ignores folders/files that start with '.' or end with '~'.
function Test-UnityHidden([string]$RelPath) {
    foreach ($seg in $RelPath.Split('\')) {
        if ($seg.StartsWith('.') -or $seg.EndsWith('~')) { return $true }
    }
    return $false
}

function Find-Csproj([string]$Name) {
    foreach ($d in $script:CsprojDirs) {
        $p = Join-Path $d "$Name.csproj"
        if (Test-Path -LiteralPath $p -PathType Leaf) { return $p }
    }
    return $null
}

function Resolve-RefPath([string]$Hint, [string]$CsprojDir) {
    if ([string]::IsNullOrWhiteSpace($Hint)) { return $null }
    $h = $Hint.Trim()
    if ([System.IO.Path]::IsPathRooted($h)) {
        if (Test-Path -LiteralPath $h -PathType Leaf) { return $h }
        return $null
    }
    foreach ($d in @($script:RepoRoot, $CsprojDir, $script:UnityProjectDir)) {
        if ([string]::IsNullOrEmpty($d)) { continue }
        $p = [System.IO.Path]::GetFullPath((Join-Path $d $h))
        if (Test-Path -LiteralPath $p -PathType Leaf) { return $p }
    }
    return $null
}

function Find-ScriptAssembly([string]$Name) {
    foreach ($d in $script:ScriptAssemblyDirs) {
        $p = Join-Path $d "$Name.dll"
        if (Test-Path -LiteralPath $p -PathType Leaf) { return $p }
    }
    return $null
}

function Read-Json([string]$Path) {
    $raw = [System.IO.File]::ReadAllText($Path)
    return ($raw | ConvertFrom-Json)
}

# guid -> asmdef name, built lazily from .asmdef.meta files
$script:GuidMap = $null
function Get-AsmdefNameByGuid([string]$Guid) {
    if ($null -eq $script:GuidMap) {
        $script:GuidMap = @{}
        $roots = @($script:AssetsDir, (Join-Path $script:RepoRoot 'Packages'))
        foreach ($d in $script:ScriptAssemblyDirs) { $roots += (Join-Path (Split-Path -Parent (Split-Path -Parent $d)) 'Library\PackageCache') }
        foreach ($root in ($roots | Select-Object -Unique)) {
            if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
            try {
                foreach ($m in [System.IO.Directory]::EnumerateFiles($root, '*.asmdef.meta', [System.IO.SearchOption]::AllDirectories)) {
                    $mt = [System.IO.File]::ReadAllText($m)
                    if ($mt -match '(?m)^guid:\s*([0-9a-fA-F]{32})') {
                        $g = $Matches[1].ToLowerInvariant()
                        $asm = $m.Substring(0, $m.Length - 5)
                        if ((-not $script:GuidMap.ContainsKey($g)) -and (Test-Path -LiteralPath $asm -PathType Leaf)) {
                            $script:GuidMap[$g] = (Get-Prop (Read-Json $asm) 'name' $null)
                        }
                    }
                }
            } catch { }
        }
    }
    $k = $Guid.ToLowerInvariant()
    if ($script:GuidMap.ContainsKey($k)) { return $script:GuidMap[$k] }
    return $null
}

function Resolve-AsmdefRef([string]$Ref) {
    if ($Ref -match '^GUID:(.+)$') {
        $n = Get-AsmdefNameByGuid $Matches[1].Trim()
        if ($null -eq $n) { $script:Notes.Add("unresolved asmdef GUID reference '$Ref'") }
        return $n
    }
    return $Ref
}

# ---------------------------------------------------------- environment ----
$script:Notes = New-Object System.Collections.Generic.List[string]

if ([string]::IsNullOrEmpty($RepoRoot)) {
    if ([string]::IsNullOrEmpty($PSScriptRoot)) { $RepoRoot = (Get-Location).Path }
    else { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
}
$script:RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$script:AssetsDir = Join-Path $script:RepoRoot 'Assets'
if (-not (Test-Path -LiteralPath $script:AssetsDir -PathType Container)) { Stop-Check "No Assets folder under '$($script:RepoRoot)'. Run from the repo root or pass -RepoRoot." }

# At script scope $script:UnityProjectDir IS the -UnityProjectDir parameter: read the argument before resetting it.
$unityDirArgument = $UnityProjectDir
$script:UnityProjectDir = ''
if (-not [string]::IsNullOrEmpty($unityDirArgument)) { $script:UnityProjectDir = [System.IO.Path]::GetFullPath($unityDirArgument).TrimEnd('\') }
$script:CsprojDirs = @($script:RepoRoot)
if ($script:UnityProjectDir -ne '' -and ($script:UnityProjectDir -ne $script:RepoRoot) -and (Test-Path -LiteralPath $script:UnityProjectDir -PathType Container)) {
    $script:CsprojDirs += $script:UnityProjectDir
}
$script:ScriptAssemblyDirs = @()
foreach ($d in $script:CsprojDirs) {
    $sa = Join-Path $d 'Library\ScriptAssemblies'
    if (Test-Path -LiteralPath $sa -PathType Container) { $script:ScriptAssemblyDirs += $sa }
}
if ($script:ScriptAssemblyDirs.Count -eq 0) {
    Stop-Check ("Library\ScriptAssemblies not found under: " + ($script:CsprojDirs -join ', ') + ". Open the project in Unity once (it compiles package assemblies there) or pass -UnityProjectDir.")
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-Check 'dotnet SDK not found on PATH (install .NET SDK 8 or newer).' }

if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $script:RepoRoot 'Temp\CompileCheck' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir).TrimEnd('\')
$projDir = Join-Path $OutDir 'proj'
$objDir = Join-Path $OutDir 'obj'
$binDir = Join-Path $OutDir 'bin'
$logDir = Join-Path $OutDir 'logs'
$marker = Join-Path $OutDir '.compile-check'
if ((Test-Path -LiteralPath $OutDir -PathType Container) -and -not (Test-Path -LiteralPath $marker)) {
    if (@(Get-ChildItem -LiteralPath $OutDir -Force).Count -gt 0) { Stop-Check "Refusing to use non-empty '$OutDir' that was not created by this script." }
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Set-Content -LiteralPath $marker -Value 'generated by tools/ci/unity-compile-check.ps1' -Encoding ASCII
if (-not $Incremental) {
    foreach ($d in @($projDir, $objDir, $binDir, $logDir)) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
}
foreach ($d in @($projDir, $objDir, $binDir, $logDir)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
# Stop MSBuild from inheriting Directory.Build.* / Directory.Packages.props from parent folders.
foreach ($f in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    Set-Content -LiteralPath (Join-Path $projDir $f) -Value '<Project />' -Encoding ASCII
}

# ------------------------------------------------- discover Unity inputs ----
function Get-UnityFiles([string]$Pattern) {
    $list = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($f in [System.IO.Directory]::EnumerateFiles($script:AssetsDir, $Pattern, [System.IO.SearchOption]::AllDirectories)) {
            $rel = Get-RelPath $script:AssetsDir $f
            if (Test-UnityHidden $rel) { continue }
            $ext = [System.IO.Path]::GetExtension($f)
            if ($Pattern -eq '*.cs' -and -not $ext.Equals('.cs', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $list.Add($f)
        }
    } catch {
        Stop-Check "Could not enumerate '$($script:AssetsDir)' ($($_.Exception.Message)). Paths over 260 chars need pwsh 7 or a shorter checkout path."
    }
    return ,$list
}

$csFiles = Get-UnityFiles '*.cs'
$asmdefFiles = Get-UnityFiles '*.asmdef'
$asmrefFiles = Get-UnityFiles '*.asmref'

# asmdef folders own every script below them (nearest asmdef wins)
$asmdefs = @{}      # name -> info
$owners = New-Object System.Collections.Generic.List[object]
foreach ($f in $asmdefFiles) {
    $j = Read-Json $f
    $name = Get-Prop $j 'name' $null
    if ([string]::IsNullOrEmpty($name)) { Stop-Check "asmdef without a name: $f" }
    if ($asmdefs.ContainsKey($name)) { Stop-Check "Duplicate asmdef name '$name': $f and $($asmdefs[$name].Path)" }
    $info = [pscustomobject]@{
        Name = $name; Path = $f; Dir = (Split-Path -Parent $f); Json = $j
        NoEngine = [bool](Get-Prop $j 'noEngineReferences' $false)
        AutoRef = [bool](Get-Prop $j 'autoReferenced' $true)
        Unsafe = [bool](Get-Prop $j 'allowUnsafeCode' $false)
        Refs = @(Get-Prop $j 'references' @())
    }
    $asmdefs[$name] = $info
    $owners.Add([pscustomobject]@{ Dir = $info.Dir; Target = $name })
}
foreach ($f in $asmrefFiles) {   # .asmref: folder compiled into another asmdef's assembly
    $j = Read-Json $f
    $target = Resolve-AsmdefRef ([string](Get-Prop $j 'reference' ''))
    $owners.Add([pscustomobject]@{ Dir = (Split-Path -Parent $f); Target = $(if ($target -and $asmdefs.ContainsKey($target)) { $target } else { '' }) })
    if (-not ($target -and $asmdefs.ContainsKey($target))) { $script:Notes.Add("asmref '$(Get-RelPath $script:RepoRoot $f)' targets a package/unknown asmdef; its scripts are not compiled") }
}
$ownerList = @($owners | Sort-Object { $_.Dir.Length } -Descending)

$groups = @{}   # assembly name -> list of files
function Add-ToGroup([string]$Name, [string]$File) {
    if (-not $groups.ContainsKey($Name)) { $groups[$Name] = New-Object System.Collections.Generic.List[string] }
    $groups[$Name].Add($File)
}
foreach ($f in $csFiles) {
    $owner = $null
    foreach ($o in $ownerList) {
        if ($f.StartsWith($o.Dir + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $owner = $o; break }
    }
    if ($null -ne $owner) {
        if ($owner.Target -ne '') { Add-ToGroup $owner.Target $f }
        continue
    }
    $rel = Get-RelPath $script:AssetsDir $f
    $segs = $rel.Split('\')
    $dirSegs = @()
    if ($segs.Length -gt 1) { $dirSegs = @($segs[0..($segs.Length - 2)]) }
    if ($dirSegs.Count -gt 0 -and $dirSegs[0] -eq 'StreamingAssets') { continue }
    $first = $dirSegs.Count -gt 0 -and (@('Plugins', 'Standard Assets', 'Pro Standard Assets') -contains $dirSegs[0])
    $editor = $dirSegs -contains 'Editor'
    if ($first -and $editor) { Add-ToGroup 'Assembly-CSharp-Editor-firstpass' $f }
    elseif ($first) { Add-ToGroup 'Assembly-CSharp-firstpass' $f }
    elseif ($editor) { Add-ToGroup 'Assembly-CSharp-Editor' $f }
    else { Add-ToGroup 'Assembly-CSharp' $f }
}

# Base csproj (for defines / refs) - Assembly-CSharp is mandatory.
$acsCsproj = Find-Csproj 'Assembly-CSharp'
if ($null -eq $acsCsproj) {
    Stop-Check ("Assembly-CSharp.csproj not found in: " + ($script:CsprojDirs -join ', ') + ". Open the project in Unity once to generate it, or pass -UnityProjectDir.")
}
$acsDoc = New-Object System.Xml.XmlDocument
$acsDoc.Load($acsCsproj)
$acsDefineNode = $acsDoc.SelectSingleNode('/Project/PropertyGroup/DefineConstants')
$acsDefines = @()
if ($null -ne $acsDefineNode) { $acsDefines = @($acsDefineNode.InnerText.Split(';') | Where-Object { $_ -ne '' }) }

# Which asmdefs does Unity compile for the Windows editor?
function Test-AsmdefBuilt($Info) {
    $j = $Info.Json
    $inc = @(Get-Prop $j 'includePlatforms' @())
    $exc = @(Get-Prop $j 'excludePlatforms' @())
    if ($inc.Count -gt 0 -and -not ($inc -contains 'Editor')) { return 'includePlatforms excludes Editor' }
    if ($exc -contains 'Editor') { return 'excludePlatforms contains Editor' }
    foreach ($c in @(Get-Prop $j 'defineConstraints' @())) {
        $neg = $c.StartsWith('!')
        $sym = $c.TrimStart('!')
        if (($acsDefines -contains $sym) -eq $neg) { return "defineConstraints not met ($c)" }
    }
    if (@(Get-Prop $j 'versionDefines' @()).Count -gt 0) { $script:Notes.Add("asmdef '$($Info.Name)' has versionDefines; they are not evaluated (defines come from the Unity csproj)") }
    if ([bool](Get-Prop $j 'overrideReferences' $false)) { $script:Notes.Add("asmdef '$($Info.Name)' uses overrideReferences/precompiledReferences; only the Unity csproj's plugin references are used") }
    return ''
}

$assemblies = New-Object System.Collections.Generic.List[object]
foreach ($name in @($asmdefs.Keys | Sort-Object)) {
    if (-not $groups.ContainsKey($name)) { continue }   # asmdef without scripts: Unity builds nothing
    $why = Test-AsmdefBuilt $asmdefs[$name]
    if ($why -ne '') { $script:Notes.Add("asmdef '$name' skipped: $why"); continue }
    $assemblies.Add([pscustomobject]@{ Name = $name; Kind = 'asmdef'; Info = $asmdefs[$name]; Files = $groups[$name]; Deps = @(); DllRefs = @(); BasePath = $null; Mode = ''; ProjPath = (Join-Path $projDir "$name.csproj") })
}
foreach ($name in @('Assembly-CSharp-firstpass', 'Assembly-CSharp-Editor-firstpass', 'Assembly-CSharp', 'Assembly-CSharp-Editor')) {
    if (-not $groups.ContainsKey($name)) { continue }
    $assemblies.Add([pscustomobject]@{ Name = $name; Kind = 'predefined'; Info = $null; Files = $groups[$name]; Deps = @(); DllRefs = @(); BasePath = $null; Mode = ''; ProjPath = (Join-Path $projDir "$name.csproj") })
}
if (-not ($assemblies | Where-Object { $_.Name -eq 'Assembly-CSharp' })) { Stop-Check 'No scripts for Assembly-CSharp found under Assets/.' }
$byName = @{}
foreach ($a in $assemblies) { $byName[$a.Name] = $a }

# Dependencies
$autoRef = @($assemblies | Where-Object { $_.Kind -eq 'asmdef' -and $_.Info.AutoRef } | ForEach-Object { $_.Name })
foreach ($a in $assemblies) {
    $deps = New-Object System.Collections.Generic.List[string]
    $dlls = New-Object System.Collections.Generic.List[string]
    if ($a.Kind -eq 'asmdef') {
        foreach ($r in $a.Info.Refs) {
            $rn = Resolve-AsmdefRef ([string]$r)
            if ([string]::IsNullOrEmpty($rn)) { continue }
            if ($byName.ContainsKey($rn)) { $deps.Add($rn) }
            elseif ($asmdefs.ContainsKey($rn)) { $script:Notes.Add("asmdef '$($a.Name)' references skipped/empty asmdef '$rn'") }
            else { $dlls.Add($rn) }
        }
    } else {
        foreach ($n in $autoRef) { $deps.Add($n) }
        switch ($a.Name) {
            'Assembly-CSharp' { if ($byName.ContainsKey('Assembly-CSharp-firstpass')) { $deps.Add('Assembly-CSharp-firstpass') } }
            'Assembly-CSharp-Editor-firstpass' { if ($byName.ContainsKey('Assembly-CSharp-firstpass')) { $deps.Add('Assembly-CSharp-firstpass') } }
            'Assembly-CSharp-Editor' {
                foreach ($n in @('Assembly-CSharp', 'Assembly-CSharp-firstpass', 'Assembly-CSharp-Editor-firstpass')) { if ($byName.ContainsKey($n)) { $deps.Add($n) } }
            }
        }
    }
    $a.Deps = @($deps | Select-Object -Unique)
    $a.DllRefs = @($dlls | Select-Object -Unique)
}

# Topological order (dependencies first)
$ordered = New-Object System.Collections.Generic.List[object]
$state = @{}
function Visit-Assembly($a) {
    if ($state.ContainsKey($a.Name)) {
        if ($state[$a.Name] -eq 1) { Stop-Check "Assembly reference cycle through '$($a.Name)'." }
        return
    }
    $state[$a.Name] = 1
    foreach ($d in $a.Deps) { Visit-Assembly $byName[$d] }
    $state[$a.Name] = 2
    $ordered.Add($a)
}
foreach ($a in $assemblies) { Visit-Assembly $a }

# --------------------------------------------------- project generation ----
$injected = @{ Count = 0 }
$unresolvedRefs = New-Object System.Collections.Generic.List[string]
$missingAnalyzers = New-Object System.Collections.Generic.List[string]
$engineRx = '^Unity(Engine|Editor)(\.|$)'

function Select-Template($a) {
    # returns @{ Path; StripEngine }
    if ($a.Kind -eq 'asmdef' -and $a.Info.NoEngine) {
        foreach ($o in $assemblies) {
            if ($o.Kind -eq 'asmdef' -and $o.Info.NoEngine -and $o.Name -ne $a.Name) {
                $p = Find-Csproj $o.Name
                if ($p) { return @{ Path = $p; StripEngine = $false } }
            }
        }
        return @{ Path = $acsCsproj; StripEngine = $true }
    }
    if ($a.Name -eq 'Assembly-CSharp-Editor-firstpass') {
        $p = Find-Csproj 'Assembly-CSharp-Editor'
        if ($p) { return @{ Path = $p; StripEngine = $false } }
    }
    return @{ Path = $acsCsproj; StripEngine = $false }
}

function New-ProjectFile($a) {
    $base = Find-Csproj $a.Name
    $stripPackages = $false
    $stripEngine = $false
    if ($base) { $a.Mode = 'unity-csproj' }
    else {
        $t = Select-Template $a
        $base = $t.Path
        $stripEngine = $t.StripEngine
        $stripPackages = ($a.Kind -eq 'asmdef')
        $a.Mode = 'synthesized from ' + [System.IO.Path]::GetFileName($base)
    }
    $a.BasePath = $base
    $baseDir = Split-Path -Parent $base

    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($base)
    $root = $doc.DocumentElement
    $targetsImport = $doc.SelectSingleNode("/Project/Import[@Sdk='Microsoft.NET.Sdk' and @Project='Sdk.targets']")
    if ($null -eq $targetsImport) { Stop-Check "$base is not an SDK-style Unity project (no Sdk.targets import); the Unity project generator changed." }

    # new property group (inserted before Sdk.targets)
    $pg = $doc.CreateElement('PropertyGroup')
    $root.InsertBefore($pg, $targetsImport) | Out-Null
    function Set-Prop([string]$n, [string]$v) {
        $node = $doc.SelectSingleNode("/Project/PropertyGroup/$n")
        if ($null -eq $node) { $node = $doc.CreateElement($n); $pg.AppendChild($node) | Out-Null }
        $node.InnerText = $v
    }
    Set-Prop 'BaseIntermediateOutputPath' ((Join-Path $objDir $a.Name) + '\')
    Set-Prop 'IntermediateOutputPath' ((Join-Path $objDir $a.Name) + '\')
    Set-Prop 'OutputPath' ($binDir + '\')
    Set-Prop 'AssemblyName' $a.Name
    if ($a.Kind -eq 'asmdef' -and $a.Mode -ne 'unity-csproj') {
        Set-Prop 'RootNamespace' ([string](Get-Prop $a.Info.Json 'rootNamespace' ''))
        Set-Prop 'AllowUnsafeBlocks' $(if ($a.Info.Unsafe) { 'True' } else { 'False' })
    }
    Set-Prop 'ProduceReferenceAssembly' 'false'
    Set-Prop 'DebugType' 'none'
    Set-Prop 'GenerateDocumentationFile' 'false'
    Set-Prop 'NuGetAudit' 'false'
    Set-Prop 'UseSharedCompilation' $(if ($SharedCompilation) { 'true' } else { 'false' })

    # drop what we regenerate
    foreach ($xp in @('//Compile', '//None', '//ProjectReference')) {
        foreach ($n in @($doc.SelectNodes($xp))) { $n.ParentNode.RemoveChild($n) | Out-Null }
    }
    # Unity's own Compile list (for the staleness note)
    $unityCompiled = @()
    if ($a.Mode -eq 'unity-csproj') {
        $d2 = New-Object System.Xml.XmlDocument
        $d2.Load($base)
        $unityCompiled = @($d2.SelectNodes('//Compile') | ForEach-Object { $_.GetAttribute('Include') })
    }

    # analyzers
    foreach ($n in @($doc.SelectNodes('//Analyzer'))) {
        $inc = $n.GetAttribute('Include')
        $keep = $false
        if (-not $NoAnalyzers) {
            if ([System.IO.Path]::IsPathRooted($inc)) { $keep = Test-Path -LiteralPath $inc -PathType Leaf }
            else {
                $r = Resolve-RefPath $inc $baseDir
                if ($r) { $n.SetAttribute('Include', $r); $keep = $true }
            }
            if (-not $keep -and -not $missingAnalyzers.Contains($inc)) { $missingAnalyzers.Add($inc) }
        }
        if (-not $keep) { $n.ParentNode.RemoveChild($n) | Out-Null }
    }

    # references
    $have = @{}
    foreach ($n in @($doc.SelectNodes('//Reference'))) {
        $rname = $n.GetAttribute('Include')
        $hintNode = $n.SelectSingleNode('HintPath')
        $hint = if ($null -ne $hintNode) { $hintNode.InnerText } else { '' }
        $drop = $byName.ContainsKey($rname)                              # replaced by a ProjectReference
        if ($stripEngine -and $rname -match $engineRx) { $drop = $true }
        $resolved = $null
        if (-not $drop) {
            $resolved = Resolve-RefPath $hint $baseDir
            if ($null -eq $resolved) {
                $drop = $true
                if ($hint -ne '' -and -not $unresolvedRefs.Contains($rname)) { $unresolvedRefs.Add($rname) }
            } elseif ($stripPackages -and ($script:ScriptAssemblyDirs | Where-Object { $resolved.StartsWith($_ + '\', [System.StringComparison]::OrdinalIgnoreCase) })) {
                $drop = $true    # asmdef assemblies only see the packages they list
            }
        }
        if ($drop -or $have.ContainsKey($rname)) { $n.ParentNode.RemoveChild($n) | Out-Null; continue }
        $have[$rname] = $true
        $hintNode.InnerText = $resolved
    }

    # new items
    $ig = $doc.CreateElement('ItemGroup')
    $root.InsertBefore($ig, $targetsImport) | Out-Null
    foreach ($f in ($a.Files | Sort-Object)) {
        $c = $doc.CreateElement('Compile')
        $c.SetAttribute('Include', (ConvertTo-MSBuildLiteral $f))
        $ig.AppendChild($c) | Out-Null
    }
    foreach ($dn in $a.Deps) {
        $pr = $doc.CreateElement('ProjectReference')
        $pr.SetAttribute('Include', $byName[$dn].ProjPath)
        $ig.AppendChild($pr) | Out-Null
    }
    # references to package assemblies (compiled DLLs), from the ProjectReferences Unity wrote + the asmdef's own list
    $dllNames = New-Object System.Collections.Generic.List[string]
    foreach ($n in $a.DllRefs) { $dllNames.Add($n) }
    if ($a.Mode -eq 'unity-csproj') {
        $d3 = New-Object System.Xml.XmlDocument
        $d3.Load($base)
        foreach ($n in @($d3.SelectNodes('//ProjectReference'))) {
            $pn = [System.IO.Path]::GetFileNameWithoutExtension(($n.GetAttribute('Include')))
            if (-not $byName.ContainsKey($pn)) { $dllNames.Add($pn) }
        }
    }
    foreach ($dn in ($dllNames | Select-Object -Unique)) {
        if ($have.ContainsKey($dn) -or $byName.ContainsKey($dn)) { continue }
        $dll = Find-ScriptAssembly $dn
        if ($null -eq $dll) { if (-not $unresolvedRefs.Contains($dn)) { $unresolvedRefs.Add($dn) }; continue }
        $have[$dn] = $true
        $r = $doc.CreateElement('Reference'); $r.SetAttribute('Include', $dn)
        $h = $doc.CreateElement('HintPath'); $h.InnerText = $dll; $r.AppendChild($h) | Out-Null
        $pv = $doc.CreateElement('Private'); $pv.InnerText = 'False'; $r.AppendChild($pv) | Out-Null
        $ig.AppendChild($r) | Out-Null
    }

    # write (only when changed, so -Incremental stays incremental)
    $sw = New-Object System.IO.StringWriter
    $xs = New-Object System.Xml.XmlWriterSettings
    $xs.Indent = $true; $xs.OmitXmlDeclaration = $true
    $xw = [System.Xml.XmlWriter]::Create($sw, $xs)
    $doc.Save($xw); $xw.Close()
    $text = $sw.ToString()
    $existing = $null
    if (Test-Path -LiteralPath $a.ProjPath) { $existing = [System.IO.File]::ReadAllText($a.ProjPath) }
    if ($existing -ne $text) { [System.IO.File]::WriteAllText($a.ProjPath, $text, (New-Object System.Text.UTF8Encoding $true)) }

    # staleness note: Unity's csproj vs files on disk
    if ($a.Mode -eq 'unity-csproj') {
        $onDisk = @{}
        foreach ($f in $a.Files) { $onDisk[(Get-RelPath $script:RepoRoot $f).ToLowerInvariant()] = (Get-RelPath $script:RepoRoot $f) }
        $inUnity = @{}
        foreach ($u in $unityCompiled) {
            $full = if ([System.IO.Path]::IsPathRooted($u)) { $u } else { Join-Path $baseDir $u }
            $rel = Get-RelPath $baseDir ([System.IO.Path]::GetFullPath($full))
            $inUnity[$rel.ToLowerInvariant()] = $rel
        }
        $added = @($onDisk.Keys | Where-Object { -not $inUnity.ContainsKey($_) } | ForEach-Object { $onDisk[$_] } | Sort-Object)
        $gone = @($inUnity.Keys | Where-Object { -not $onDisk.ContainsKey($_) } | ForEach-Object { $inUnity[$_] } | Sort-Object)
        if ($added.Count -gt 0 -or $gone.Count -gt 0) {
            $m = "$($a.Name): on-disk scripts differ from Unity's csproj ($(Split-Path -Leaf $baseDir)): +$($added.Count) new, -$($gone.Count) absent"
            if ($added.Count -gt 0) { $m += ' [new: ' + (($added | Select-Object -First 3) -join ', ') + $(if ($added.Count -gt 3) { ', ...' } else { '' }) + ']' }
            if ($gone.Count -gt 0) { $m += ' [absent: ' + (($gone | Select-Object -First 3) -join ', ') + $(if ($gone.Count -gt 3) { ', ...' } else { '' }) + ']' }
            $script:Notes.Add($m)
        }
    }
}

foreach ($a in $ordered) { New-ProjectFile $a }

# --------------------------------------------------------------- build -----
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:MSBUILDDISABLENODEREUSE = '1'

$sdkVersion = (& dotnet --version 2>&1 | Select-Object -First 1)
Write-Host ("unity-compile-check: repo={0}" -f $script:RepoRoot)
Write-Host ("  dotnet SDK {0}; csproj source: {1}; out: {2}" -f $sdkVersion, ($script:CsprojDirs -join ' | '), (Get-RelPath $script:RepoRoot $OutDir))
Write-Host '  assemblies:'
foreach ($a in $ordered) {
    $depText = if ($a.Deps.Count -gt 0) { ' -> ' + ($a.Deps -join ', ') } else { '' }
    Write-Host ("    {0} ({1} scripts, {2}){3}" -f $a.Name, $a.Files.Count, $a.Mode, $depText)
}

$rxFile = '^(?<file>.*?)\((?<line>\d+),(?<col>\d+)(?:,\d+,\d+)?\):\s*(?<sev>error|warning)\s+(?<code>[^\s:]+)\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]*proj)\])?\s*$'
$rxTool = '^(?<tool>[^()]*?)\s*:\s*(?<sev>error|warning)\s+(?<code>[^\s:]+)\s*:\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]*proj)\])?\s*$'
$errors = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[object]
$seen = New-Object System.Collections.Generic.HashSet[string]
$unparsedFailure = $false

foreach ($a in $ordered) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $buildArgs = @('build', $a.ProjPath, '-nologo', '-v:q', '-nodeReuse:false', '-consoleloggerparameters:NoSummary')
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = @(& dotnet @buildArgs 2>&1 | ForEach-Object { [string]$_ })
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $prev
    [System.IO.File]::WriteAllLines((Join-Path $logDir "$($a.Name).log"), [string[]]$lines)

    $e0 = $errors.Count; $w0 = $warnings.Count
    foreach ($line in $lines) {
        $m = [regex]::Match($line, $rxFile)
        $file = ''; $ln = 0
        if ($m.Success) { $file = $m.Groups['file'].Value; $ln = [int]$m.Groups['line'].Value }
        else {
            $m = [regex]::Match($line, $rxTool)
            if (-not $m.Success) { continue }
            $file = $m.Groups['tool'].Value.Trim()
        }
        $d = [pscustomobject]@{
            Sev = $m.Groups['sev'].Value; Code = $m.Groups['code'].Value; Msg = $m.Groups['msg'].Value.Trim()
            File = (Get-RelPath $script:RepoRoot $file).Replace('\', '/'); Line = $ln; Asm = $a.Name
        }
        $key = "$($d.Sev)|$($d.File)|$($d.Line)|$($d.Code)|$($d.Msg)"
        if (-not $seen.Add($key)) { continue }
        if ($d.Sev -eq 'error') { $errors.Add($d) } else { $warnings.Add($d) }
    }
    $newErr = $errors.Count - $e0
    $status = if ($newErr -gt 0) { 'FAILED' } elseif ($exit -ne 0) { 'FAILED' } else { 'ok' }
    if ($exit -ne 0 -and $newErr -eq 0) {
        $unparsedFailure = $true
        $tail = @($lines | Select-Object -Last 15)
        Write-Host ("  [build] {0} FAILED (dotnet exit {1}, no parsable diagnostics). Last output:" -f $a.Name, $exit)
        foreach ($t in $tail) { Write-Host "      $t" }
    } else {
        Write-Host ("  [build] {0}: {1} ({2:N1}s, {3} error(s), {4} warning(s))" -f $a.Name, $status, $sw.Elapsed.TotalSeconds, $newErr, ($warnings.Count - $w0))
    }
    # A failed assembly makes every dependent fail with noise; stop at the first broken layer.
    if ($exit -ne 0) {
        $rest = @($ordered | Where-Object { -not $state.ContainsKey('built:' + $_.Name) -and $_.Name -ne $a.Name -and ($ordered.IndexOf($_) -gt $ordered.IndexOf($a)) } | ForEach-Object { $_.Name })
        if ($rest.Count -gt 0) { $script:Notes.Add("not compiled because '$($a.Name)' failed: " + ($rest -join ', ')) }
        break
    }
}

# -------------------------------------------------------------- report -----
Write-Host ''
if ($errors.Count -gt 0) {
    Write-Host ("ERRORS ({0}):" -f $errors.Count)
    $shown = 0
    foreach ($e in $errors) {
        if ($shown -ge $MaxErrors) { Write-Host ("  ... {0} more (see {1}\*.log)" -f ($errors.Count - $shown), (Get-RelPath $script:RepoRoot $logDir)); break }
        if ($e.Line -gt 0) { Write-Host ("  {0}:{1}: error {2}: {3}" -f $e.File, $e.Line, $e.Code, $e.Msg) }
        else { Write-Host ("  {0}: error {1}: {2}" -f $e.File, $e.Code, $e.Msg) }
        $shown++
    }
    Write-Host ''
}
if ($script:Notes.Count -gt 0 -or $unresolvedRefs.Count -gt 0 -or $missingAnalyzers.Count -gt 0) {
    Write-Host 'Notes:'
    foreach ($n in $script:Notes) { Write-Host "  - $n" }
    if ($unresolvedRefs.Count -gt 0) { Write-Host ("  - {0} reference(s) not found on disk and left out: {1}" -f $unresolvedRefs.Count, (($unresolvedRefs | Select-Object -First 8) -join ', ') + $(if ($unresolvedRefs.Count -gt 8) { ', ...' } else { '' })) }
    if ($missingAnalyzers.Count -gt 0) { Write-Host ("  - {0} analyzer/source-generator DLL(s) not found and skipped (e.g. {1})" -f $missingAnalyzers.Count, [System.IO.Path]::GetFileName($missingAnalyzers[0])) }
    Write-Host ''
}
Write-Host ("Warnings: {0} distinct" -f $warnings.Count)
if ($warnings.Count -gt 0) {
    if ($ShowWarnings) {
        foreach ($w in $warnings) { Write-Host ("  {0}:{1}: warning {2}: {3}" -f $w.File, $w.Line, $w.Code, $w.Msg) }
    } else {
        $top = $warnings | Group-Object Code | Sort-Object Count -Descending | Select-Object -First 5
        Write-Host ('  top codes: ' + (($top | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', ') + '   (use -ShowWarnings to list)')
    }
}
$secs = $script:Clock.Elapsed.TotalSeconds
if ($errors.Count -gt 0) {
    Write-Host ("RESULT: FAIL - {0} error(s), {1} warning(s) in {2:N1}s" -f $errors.Count, $warnings.Count, $secs)
    exit 1
}
if ($unparsedFailure) {
    Write-Host ("RESULT: FAIL - dotnet build failed without a parsable diagnostic (logs: {0}) in {1:N1}s" -f (Get-RelPath $script:RepoRoot $logDir), $secs)
    exit 3
}
Write-Host ("RESULT: PASS - 0 errors, {0} warning(s) in {1:N1}s" -f $warnings.Count, $secs)
exit 0
