param([switch]$Test)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build '.\PCL2.TweakLoader\PCL2.TweakLoader.csproj' -c Release -m:1 -p:UseSharedCompilation=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Loader build failed.' }
    Copy-Item -LiteralPath '.\PCL2.TweakLoader\bin\Release\net48\PCL2.TweakLoader.exe' -Destination '.\PCL2.TweakLoader.exe'
    Copy-Item -LiteralPath '.\PCL2.TweakLoader\bin\Release\net48\PCL2.TweakLoader.exe.config' -Destination '.\PCL2.TweakLoader.exe.config'
    if ($Test) {
        dotnet build '.\tests\PCL2.TweakLoader.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Check harness build failed.' }
        & '.\tests\bin\Release\net48\PCL2.TweakLoader.Tests.exe'
        if ($LASTEXITCODE -ne 0) { throw 'Loader checks failed.' }
    }
} finally { Pop-Location }
