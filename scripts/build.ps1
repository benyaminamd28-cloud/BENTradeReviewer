$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
Write-Host 'Restoring packages...'
dotnet restore .\BENTradeReviewer.sln
Write-Host 'Building x64 Release...'
dotnet build .\BENTradeReviewer.sln --configuration Release --platform x64 --no-restore
Write-Host 'Running tests...'
dotnet test .\src\BENTradeReviewer.Tests\BENTradeReviewer.Tests.csproj --configuration Release --no-restore
Write-Host 'Build and tests completed.'
