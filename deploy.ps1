$pluginPath = "D:\workspace\MidiBard2"
$csprojPath = "$pluginPath\MidiBard\MidiBard2.csproj"

# === version bump ===
[xml]$xml = Get-Content $csprojPath

$propertyGroups = $xml.Project.PropertyGroup | Where-Object {
    $_.AssemblyName -eq 'ZuneBard' -and $_.Version
}

if (-not $propertyGroups) {
    Write-Error "No <Version> found."
    exit 1
}

# base version
$currentVersion = $propertyGroups[0].Version
$versionParts = $currentVersion -split '\.'

# increment
$versionParts[-1] = [int]$versionParts[-1] + 1
$newVersion = $versionParts -join '.'

foreach ($pg in $propertyGroups) {
    $pg.Version = $newVersion
}

$xml.Save($csprojPath)

Write-Host "Version (ZuneBard) updated: $currentVersion → $newVersion"

# === Build ===
cd $pluginPath
dotnet build --configuration Beta

$buildSourcePath = "$pluginPath\Midibard\bin\Beta\ZuneBard"
$repositoryBuildPath = "D:\workspace\DalamudPlugins\downloads\MidiBard2"

# create
if (!(Test-Path $repositoryBuildPath)) {
    New-Item -ItemType Directory -Path $repositoryBuildPath
}

# # === Git ===
cd $repositoryBuildPath
git pull

# move files
Get-ChildItem -Path $buildSourcePath | ForEach-Object {
    Move-Item -Path $_.FullName -Destination $repositoryBuildPath -Force
}

git add .
git commit -m "Build $newVersion"
git push origin main

cd $pluginPath
