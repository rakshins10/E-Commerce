<#
.SYNOPSIS
    Logs in as every seed user and shows what each one's token actually contains.

.DESCRIPTION
    A demo and diagnostic helper. It proves the composite-role design works end
    to end: nobody is assigned a permission directly, yet every user's token
    arrives carrying exactly the permissions their role composes.

    Requires the stack to be running:
        cd deploy; docker compose up -d --wait

.EXAMPLE
    ./scripts/show-permissions.ps1

.EXAMPLE
    ./scripts/show-permissions.ps1 -User administrator -Raw
    Shows the full decoded token for one user.

.LINK
    docs/authorization-model.md
#>
[CmdletBinding()]
param(
    [string] $Keycloak = 'http://localhost:8080',
    [string] $Realm = 'ecommerce',
    [string] $Password = 'Passw0rd!',
    [string] $User,
    [switch] $Raw
)

$ErrorActionPreference = 'Stop'

$seedUsers = @(
    @{ Name = 'customer';      Role = 'customer';        Note = 'shopper' }
    @{ Name = 'support';       Role = 'support-agent';   Note = 'helpdesk, read-only' }
    @{ Name = 'catalogmgr';    Role = 'catalog-manager'; Note = 'merchandising' }
    @{ Name = 'ordermgr';      Role = 'order-manager';   Note = 'fulfilment' }
    @{ Name = 'administrator'; Role = 'admin';           Note = 'everything' }
    @{ Name = 'blocked';       Role = 'customer';        Note = 'DISABLED account' }
)

function Get-TokenClaims {
    param([string] $Username)

    $body = @{
        grant_type    = 'password'
        client_id     = 'test-harness'
        client_secret = 'dev_only_test_harness_secret'
        username      = $Username
        password      = $Password
    }

    $response = Invoke-RestMethod -Method Post -UseBasicParsing `
        -Uri "$Keycloak/realms/$Realm/protocol/openid-connect/token" -Body $body

    # A JWT is three dot-separated base64url segments: header.payload.signature.
    # The payload is NOT encrypted - anyone can read it. It is merely signed, so
    # nobody can forge one. See docs/concepts-explained.md#17.
    $payload = $response.access_token.Split('.')[1]
    $payload += '=' * ((4 - $payload.Length % 4) % 4)
    $json = [System.Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($payload.Replace('-', '+').Replace('_', '/')))

    return @{ Claims = ($json | ConvertFrom-Json); Token = $response.access_token }
}

# --- Single user, full detail -------------------------------------------------
if ($User) {
    $result = Get-TokenClaims -Username $User
    if ($Raw) {
        $result.Claims | ConvertTo-Json -Depth 10
    }
    else {
        Write-Host "`n  $User" -ForegroundColor Cyan
        Write-Host "  sub          : $($result.Claims.sub)"
        Write-Host "  audience     : $($result.Claims.aud -join ', ')"
        Write-Host "  issuer       : $($result.Claims.iss)"
        Write-Host "  expires in   : $([math]::Round(($result.Claims.exp - [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) / 60, 1)) minutes"
        Write-Host "  realm roles  : $((($result.Claims.realm_access.roles | Where-Object { $_ -notmatch 'default-roles|offline_access|uma_' }) | Sort-Object) -join ', ')"
        Write-Host "  permissions  :" -ForegroundColor Yellow
        $result.Claims.permissions | Sort-Object | ForEach-Object { Write-Host "                 $_" }
        Write-Host "`n  Paste this into https://jwt.io to read it yourself:`n" -ForegroundColor DarkGray
        Write-Host "  $($result.Token)`n" -ForegroundColor DarkGray
    }
    exit 0
}

# --- All seed users -----------------------------------------------------------
Write-Host "`n  Seed users and the permissions their token carries" -ForegroundColor Cyan
Write-Host "  Realm: $Keycloak/realms/$Realm`n" -ForegroundColor DarkGray
Write-Host "  Nothing below is assigned to a user directly. Each realm role is a"
Write-Host "  Keycloak COMPOSITE that grants these permissions.`n" -ForegroundColor DarkGray

foreach ($seed in $seedUsers) {
    try {
        $result = Get-TokenClaims -Username $seed.Name
        $permissions = $result.Claims.permissions | Sort-Object

        Write-Host ("  {0,-14}" -f $seed.Name) -ForegroundColor Green -NoNewline
        Write-Host ("{0,-16} " -f $seed.Role) -NoNewline
        Write-Host ("{0,2} permissions" -f $permissions.Count) -ForegroundColor Yellow

        $line = '                 '
        foreach ($permission in $permissions) {
            if (($line.Length + $permission.Length) -gt 95) {
                Write-Host $line -ForegroundColor DarkGray
                $line = '                 '
            }
            $line += "$permission  "
        }
        if ($line.Trim()) { Write-Host $line -ForegroundColor DarkGray }
        Write-Host ''
    }
    catch {
        # A disabled account fails at AUTHENTICATION, inside Keycloak - it never
        # reaches our authorization layer. That is the correct place for it to
        # fail, and seeing it here is the point of the `blocked` seed user.
        Write-Host ("  {0,-14}" -f $seed.Name) -ForegroundColor Red -NoNewline
        Write-Host ("{0,-16} " -f $seed.Role) -NoNewline
        Write-Host "CANNOT LOG IN  ($($seed.Note))" -ForegroundColor Red
        Write-Host ''
    }
}

Write-Host "  Try:  ./scripts/show-permissions.ps1 -User administrator" -ForegroundColor DarkGray
Write-Host "  Docs: docs/authorization-model.md`n" -ForegroundColor DarkGray
