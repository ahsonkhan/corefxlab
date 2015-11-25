﻿Push-Location $PSScriptRoot
[Environment]::CurrentDirectory = $PWD

$version = [string]::Format("0.1.0-d{0}-1", (Get-Date).ToString("MMddyy"))
$apiKey = $args[0]
$nugetPath = ".\packages\NuGet.exe"

foreach ($file in [System.IO.Directory]::EnumerateFiles(".\nuget", "*.nuspec")) {
    $arguments = "pack $file -Version $version -OutputDirectory .\nuget"
    Start-Process -FilePath $nugetPath -ArgumentList $arguments -Wait -PassThru
}

foreach ($file in [System.IO.Directory]::EnumerateFiles(".\nuget", "*.nupkg")) {
    $arguments = "push $file $apiKey -Source https://www.myget.org/F/dotnet-corefxlab/api/v2/package"
    Start-Process -FilePath $nugetPath -ArgumentList $arguments -Wait -PassThru
}

Pop-Location
[Environment]::CurrentDirectory = $PWD