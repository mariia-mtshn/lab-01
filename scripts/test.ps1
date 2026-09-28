$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
    param([scriptblock]$Step)
    & $Step
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Invoke-Step { docker compose --env-file "$root/infra/.env.example" -f "$root/infra/compose.yaml" up -d --wait }
Invoke-Step { dotnet  build "$root/src/SecureLab.Api/SecureLab.Api.csproj" --configuration Release }
Invoke-Step { dotnet "$root/src/SecureLab.Api" -- --reset-database }
Invoke-Step { dotnet  test "$root/tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj" --configuration Release }