<# Package NuGetProvider into a NuGet package #>
param(
    [Parameter(Mandatory=$true)]
    [string]
    $Source,

    [Parameter(Mandatory=$false)]
    [switch]
    $RefreshNuGetClient
)

if ((-not (Test-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath nuget.exe))) -or $RefreshNuGetClient) {
    Write-Host "Downloading latest NuGet.exe"
    $null = Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile (Join-Path -Path $PSScriptRoot -ChildPath nuget.exe)
}

$nuspecContent = Get-Content (Join-Path -Path $PSScriptRoot -ChildPath "Microsoft.PackageManagement.NuGetProvider.nuspec_template") | Out-String
$releaseNotes = Get-Content (Join-Path -Path $PSScriptRoot -ChildPath "releasenotes.md") | Out-String
$versionFileContent = Get-Content (Join-Path -Path $PSScriptRoot -ChildPath "Properties" | Join-Path -ChildPath "AssemblyInfo.cs") | Out-String
$versionFileContent -match "[[]assembly: AssemblyVersion[(]""(.*)""[)][]]"
$version = $matches[1]
Write-Verbose "Version: $version"
$fileTemplate = "<file src=""{path}"" target=""lib\{framework}"" />"
$files = ""
foreach ($frameworkDir in (Get-ChildItem -Path $Source -Directory)) {
    Write-Verbose "Framework: $(Split-Path -Path $frameworkDir -Leaf)"
    foreach ($dll in (Get-ChildItem -Path "$($frameworkDir.FullName)\Microsoft.PackageManagement.NuGetProvider.dll")) {
        Write-Verbose "Path: $($dll.FullName)"
        $files += $fileTemplate.Replace("{path}", $dll.FullName).Replace("{framework}", (Split-Path -Path $frameworkDir -Leaf))
    }
}

$nuspecContent = $nuspecContent.Replace("{version}", $version)
$nuspecContent = $nuspecContent.Replace("{release notes}", $releaseNotes)
$nuspecContent = $nuspecContent.Replace("{files}", $files)
$nuspecContent | Out-File "Microsoft.PackageManagement.NuGetProvider.nuspec"
& (Join-Path -Path $PSScriptRoot -ChildPath nuget.exe) pack "Microsoft.PackageManagement.NuGetProvider.nuspec"
Write-Host "Done packing."