param(
    [ValidateSet("Beta", "Alpha")]
    [string]$Configuration = "Alpha"
)

$ErrorActionPreference = "Stop"

$pluginPath = "D:\workspace\MidiBard2"
$csprojPath = "$pluginPath\Midibard\MidiBard2.csproj"
$dalamudRepoPath = "D:\workspace\DalamudPlugins"
$pluginMasterJsonPath = "$dalamudRepoPath\pluginmaster.json"

$deployByConfig = @{
    Beta = @{
        AssemblyName = "ZuneBard"
        InternalName = "ZuneBard"
        BuildSourcePath = "$pluginPath\Midibard\bin\Beta\ZuneBard"
        RepositoryBuildPath = "$dalamudRepoPath\downloads\MidiBard2"
    }
    Alpha = @{
        AssemblyName = "ZuneBardAlpha"
        InternalName = "ZuneBardAlpha"
        BuildSourcePath = "$pluginPath\Midibard\bin\Alpha\ZuneBardAlpha"
        RepositoryBuildPath = "$dalamudRepoPath\downloads\MidiBard2Alpha"
    }
}

$selected = $deployByConfig[$Configuration]
$assemblyName = $selected.AssemblyName
$internalName = $selected.InternalName
$buildSourcePath = $selected.BuildSourcePath
$repositoryBuildPath = $selected.RepositoryBuildPath

Write-Host "Deploy configuration: $Configuration"
Write-Host "Assembly: $assemblyName"

# === version bump in csproj ===
[xml]$xml = Get-Content $csprojPath

$propertyGroups = $xml.Project.PropertyGroup | Where-Object {
    $assemblyNode = $_.SelectSingleNode('AssemblyName')
    $versionNode = $_.SelectSingleNode('Version')
    $null -ne $assemblyNode -and
    $null -ne $versionNode -and
    [string]$assemblyNode.InnerText -eq $assemblyName
}

if (-not $propertyGroups) {
    Write-Error "No <Version> found for assembly '$assemblyName' in $csprojPath."
    exit 1
}

$targetPropertyGroup = $propertyGroups | Select-Object -First 1
$currentVersion = [string]$targetPropertyGroup.SelectSingleNode('Version').InnerText
$currentVersion = $currentVersion.Trim()

if ([string]::IsNullOrWhiteSpace($currentVersion)) {
    Write-Error "Version node is empty for assembly '$assemblyName'."
    exit 1
}
$versionParts = $currentVersion -split '\.'

if ($versionParts.Count -lt 2) {
    Write-Error "Unexpected version format: $currentVersion"
    exit 1
}

$versionParts[-1] = ([int]$versionParts[-1] + 1).ToString()
$newVersion = $versionParts -join '.'

foreach ($pg in $propertyGroups) {
    $pg.SelectSingleNode('Version').InnerText = $newVersion
}

$xml.Save($csprojPath)
Write-Host "Version ($assemblyName) updated: $currentVersion -> $newVersion"

# === build selected configuration ===
Set-Location $pluginPath
dotnet build --configuration $Configuration

if (-not (Test-Path $buildSourcePath)) {
    Write-Error "Build output path not found: $buildSourcePath"
    exit 1
}

if (-not (Test-Path $repositoryBuildPath)) {
    New-Item -ItemType Directory -Path $repositoryBuildPath -Force | Out-Null
}

# Move artifacts to downloads target
Get-ChildItem -Path $buildSourcePath | ForEach-Object {
    Move-Item -Path $_.FullName -Destination $repositoryBuildPath -Force
}

# === update pluginmaster.json AssemblyVersion ===
if (-not (Test-Path $pluginMasterJsonPath)) {
    Write-Error "pluginmaster.json not found: $pluginMasterJsonPath"
    exit 1
}

$pluginMaster = Get-Content $pluginMasterJsonPath -Raw | ConvertFrom-Json
$targetEntry = $pluginMaster | Where-Object { $_.InternalName -eq $internalName } | Select-Object -First 1

if (-not $targetEntry) {
    Write-Error "InternalName '$internalName' not found in pluginmaster.json"
    exit 1
}

$targetEntry.AssemblyVersion = $newVersion
$pluginMaster | ConvertTo-Json -Depth 20 | Set-Content -Path $pluginMasterJsonPath

Write-Host "pluginmaster.json updated: $internalName AssemblyVersion -> $newVersion"

# === git in DalamudPlugins repo ===
Set-Location $dalamudRepoPath
git pull
git add .

$hasChanges = git status --porcelain
if ($hasChanges) {
    git commit -m "Build $Configuration $newVersion"
    git push origin main
    Write-Host "Deploy committed and pushed."
}
else {
    Write-Host "No changes detected to commit."
}

Set-Location $pluginPath
