
Describe "Smoke testing" -Tags "Feature" {
 
 BeforeAll{
    Register-PackageSource -Name Nugettest -provider NuGet -Location https://www.nuget.org/api/v2 -force

 }

    it "EXPECTED: Find a package"  {
        $a = Find-Package -ProviderName NuGet -Name jquery -source Nugettest
        $a.Name -contains "jquery" | should be $true
	}

    it "EXPECTED: Install a package"  {
        $a = install-Package -ProviderName NuGet -Name jquery -force -source Nugettest
        $a.Name -contains "jquery" | should be $true
	}


    it "EXPECTED: Get a package"  {
        $a = Get-Package -ProviderName NuGet -Name jquery
        $a.Name -contains "jquery" | should be $true
	}

    it "EXPECTED: save a package"  {
        $a = save-Package -ProviderName NuGet -Name jquery -path $TestDrive -force -source Nugettest
        $a.Name -contains "jquery" | should be $true
	}

    it "EXPECTED: uninstall a package"  {
        $a = uninstall-Package -ProviderName NuGet -Name jquery
        $a.Name -contains "jquery" | should be $true
	}

}