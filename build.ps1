$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project build/_build.csproj -- $args
