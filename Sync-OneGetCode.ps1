<# Sync dependencies from OneGet repo... #>
param()
$paths = @("Microsoft.PackageManagement\providers\inbox\Common\Extensions\Extensions.cs",
           "Microsoft.PackageManagement\providers\inbox\Common\Utility\*.cs",
           "Microsoft.PackageManagement\providers\inbox\Common\Version\*.cs",
           "Microsoft.PackageManagement\Utility\Platform\OSInformation.cs")
$doNotModifyHeader = @'
//
// This file was cloned from https://github.com/OneGet/oneget on {date}
// Do not directly modify this file. Make a pull request at https://github.com/OneGet/oneget first instead.
// Then run Sync-OneGetCode.ps1 in this repository to bring the changes down and merge the new files.
//

'@
$doNotModifyHeader = $doNotModifyHeader.Replace("{date}", (Get-Date).ToString())
# Clone into a temp directory
$temp = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ([System.IO.Path]::GetRandomFileName())
$null = New-Item -Path $temp -ItemType Directory
pushd $temp
git clone https://github.com/OneGet/oneget.git
popd
try {
    # Discover files
    $srcRootDir = Join-Path -Path $temp -ChildPath "oneget" | Join-Path -ChildPath "src"
    foreach ($pathCandidate in $paths) {
        $absolutePath = Join-Path -Path $srcRootDir -ChildPath $pathCandidate
        foreach ($fileInfo in (Get-ChildItem -Path $absolutePath -File)) {
            # Read content
            $content = Get-Content -Path $fileInfo.FullName
            # Prepend "Don't modify" header
            $content = ,$doNotModifyHeader + $content
            # Write content to NuGetProvider repo
            $subDir = Split-Path -Path ($fileInfo.FullName.Replace($srcRootDir, "").Trim([System.IO.Path]::DirectorySeparatorChar))
            $newDir = Join-Path -Path $PSScriptRoot -ChildPath $subDir
            $newFilePath = Join-Path -Path $newDir -ChildPath (Split-Path -Path $fileInfo.FullName -Leaf)
            if (-not (Test-Path -Path $newDir)) {
                $null = New-Item -Path $newDir -ItemType Directory
            }
            Write-Host "Updating file: $newFilePath"
            $content | Out-File -FilePath $newFilePath -Encoding UTF8
        }
    }
}
finally {
    $null = Remove-Item -Path $temp -Recurse -Force
}
