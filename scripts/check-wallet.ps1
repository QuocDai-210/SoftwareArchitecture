param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$testContainer = 'swiftride-wallet-check-' + [Guid]::NewGuid().ToString('N')
$created = $false
$previousConnection = $env:WALLET_TEST_CONNECTION
$testExit = 1
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & docker compose --profile '*' stop
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop project containers before testing.' }
    if (-not $SkipBuild) {
        & dotnet build backend/tests/SwiftRide.Checks/SwiftRide.Checks.csproj `
            -p:RestoreLockedMode=true -m:1 --disable-build-servers -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Wallet test build failed.' }
    }
    $testPassword = [Guid]::NewGuid().ToString('N')
    & docker run --detach --rm --name $testContainer --label swiftride.test=wallet `
        --memory 512m --memory-swap 512m --tmpfs /var/lib/postgresql/data:rw,size=256m `
        -e "POSTGRES_PASSWORD=$testPassword" -e POSTGRES_DB=wallet_checks `
        -p 127.0.0.1::5432 postgres:17.4-alpine3.21 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated wallet test database.' }
    $created = $true
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker exec $testContainer pg_isready -h 127.0.0.1 -U postgres -d wallet_checks 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Wallet test database did not become ready.' }
    $mapping = & docker port $testContainer 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $mapping -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Invalid isolated database port mapping.'
    }
    $testPort = $Matches[1]
    $env:WALLET_TEST_CONNECTION = "Host=127.0.0.1;Port=$testPort;Database=wallet_checks;Username=postgres;Password=$testPassword"
    & dotnet test backend/tests/SwiftRide.Checks/SwiftRide.Checks.csproj `
        --no-build --no-restore --filter 'Category=WalletIntegration' --nologo
    $testExit = $LASTEXITCODE
} finally {
    if ($created) {
        # Only this invocation's disposable test container; no project volume is touched.
        & docker rm --force $testContainer | Out-Null
    }
    $env:WALLET_TEST_CONNECTION = $previousConnection
    Pop-Location
}
exit $testExit
