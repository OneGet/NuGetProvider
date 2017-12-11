<#PSScriptInfo
.VERSION 0.0.1
.AUTHOR PowerShell Team
.COPYRIGHT (c) Microsoft Corporation
.GUID b4267027-4764-47fa-928f-61e7f6db43c3
.TAGS Find-Package, PackageManagement, TabExpansion2, Register-ArgumentCompleter
.LICENSEURI http://www.apache.org/licenses/LICENSE-2.0
.PROJECTURI https://github.com/oneget/NuGetProvider
#>

<#
.Synopsis
	Argument completers for Find-Package -Name for NuGet v3.
.Description
	The script registers argument completers for Find-Package -Name when a single source is registered OR -Source is specified. All credential parameters should be specified before pressing <tab>.
	Installation:
	* Using PowerShell v5+:
		Invoke Find-Package.NuGet.ArgumentCompleters.ps1 (or add it to your profile)
	* TabExpansionPlusPlus:
		Find-Package.NuGet.ArgumentCompleters.ps1 should be placed in the same directory as TabExpansionPlusPlus. Optionally, invoke Find-Package.NuGet.ArgumentCompleters.ps1 (or add it to your profile)
	* TabExpansion2.ps1
		Find-Package.NuGet.ArgumentCompleters.ps1 should be added to the path. Optionally, AFTER invoking TabExpansion2.ps1, invoke Find-Package.NuGet.ArgumentCompleters.ps1 (or add it to your profile)
#>
Register-ArgumentCompleter -CommandName Find-Package -ParameterName Name -ScriptBlock {
	param($commandName, $parameterName, $wordToComplete, $commandAst, $boundParameters)

	# Check that the loaded PackageManagement module is new enough
	if (-not ("Microsoft.PackageManagement.NuGetProvider.RequestWrapper" -as [Type])) {
		return
	}

	# This is a workaround until we can figure out how to properly page results. Currently argument completers seem to fully enumerate the results, so we have to limit the number of results ourselves.
	# Change this if you want more results, but be aware that the script's signing becomes invalid.
	$MAX_COMPLETION_RESULTS = 20


    [string]$location = $null
    [string]$credentialUserName = $null
    [System.Security.SecureString]$credentialUserPassword = $null
    [System.Net.IWebProxy]$proxy = $null
    [string[]]$headers = $null

    if ($boundParameters.ContainsKey('Source')) {
        $sourceValue = $boundParameters['Source']
        $ps = Get-PackageSource $sourceValue -ProviderName NuGet -ErrorAction Ignore -WarningAction Ignore | Select-Object -First 1
        if ($ps -ne $null) {
            $location = $ps.Location
        } elseif ([System.Uri]::IsWellFormedUriString($sourceValue, [System.UriKind]::Absolute)) {
            $location = $sourceValue
        }
    } else {
        $ps = Get-PackageSource -ProviderName NuGet -ErrorAction Ignore -WarningAction Ignore
        if ($ps -and ($ps.Count -eq 1)) {
            $location = $ps.Location
        }
    }

    if ($boundParameters.ContainsKey('Credential')) {
        $credentialUserName = $boundParameters['Credential'].UserName
        $credentialUserPassword = $boundParameters['Credential'].Password
    }

    if ($boundParameters.ContainsKey('Headers')) {
        $headers = $boundParameters['Headers']
    }

    if ($boundParameters.ContainsKey('Proxy')) {
        $proxyUrl = $boundParameters['Proxy']
        $proxy = [System.Net.WebProxy]::new($proxyUrl)
    }

    if ($location)
    {
        $fakeRequest = New-Object -TypeName Microsoft.PackageManagement.NuGetProvider.RequestWrapper
        $resources = [Microsoft.PackageManagement.NuGetProvider.NuGetResourceCollectionFactory]::GetResources($location, $fakeRequest)
        if ($resources.AutoCompleteFeed -ne $null) {
            $searchTerm = New-Object -TypeName Microsoft.PackageManagement.NuGetProvider.NuGetSearchTerm -ArgumentList [Microsoft.PackageManagement.NuGetProvider.NuGetSearchTerm+NuGetSearchTermType]::AutoComplete,$wordToComplete
            $resources.AutoCompleteFeed.Autocomplete($searchTerm, $fakeRequest, $false) | Select-Object -First $MAX_COMPLETION_RESULTS | ForEach-Object {
                New-Object System.Management.Automation.CompletionResult $_, $_, 'ParameterValue', $_ 
            }
        }
    }
}