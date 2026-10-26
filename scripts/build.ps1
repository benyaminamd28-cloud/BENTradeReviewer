$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
    }
}

Write-Host 'Restoring packages...'
Invoke-DotNet @('restore', '.\BENTradeReviewer.sln')

Write-Host 'Building x64 Release...'
Invoke-DotNet @('build', '.\BENTradeReviewer.sln', '--configuration', 'Release', '--platform', 'x64', '--no-restore')

Write-Host 'Running tests...'
Invoke-DotNet @('test', '.\src\BENTradeReviewer.Tests\BENTradeReviewer.Tests.csproj', '--configuration', 'Release', '--no-restore')

Write-Host 'Build and tests completed successfully.'
