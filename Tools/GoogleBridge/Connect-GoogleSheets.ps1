<#
.SYNOPSIS
    One-time setup: trades a Google OAuth client for a refresh token and saves it outside the repo.

.DESCRIPTION
    Run this once per machine before Push-GoogleSheet.ps1 or Pull-GoogleSheet.ps1 work. It needs a
    Google Cloud project with the Sheets API and Drive API enabled, and an OAuth client ID of type
    "Desktop app" (Google Cloud Console -> APIs & Services -> Credentials -> Create Credentials ->
    OAuth client ID -> Desktop app). Paste that client's ID and secret in when prompted.

    Runs the installed-app loopback flow: opens the consent page in your browser, listens on
    http://127.0.0.1:<port> for the redirect, and exchanges the code it receives for a refresh token.
    Scope is drive.file only - this app can see and edit exactly the spreadsheets it creates, nothing
    else in your Drive.

    The token is written to %USERPROFILE%\.gmtk-google\token.json (or $env:GMTK_GOOGLE_TOKEN if set),
    deliberately outside the repo since it is a long-lived credential.

.PARAMETER ClientId
.PARAMETER ClientSecret
    From the OAuth client. Prompted for if not passed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/GoogleBridge/Connect-GoogleSheets.ps1
#>
[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$ClientSecret
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'GoogleBridge.Common.psm1') -Force -DisableNameChecking

if (-not $ClientId)     { $ClientId     = Read-Host 'Google OAuth Client ID' }
if (-not $ClientSecret) { $ClientSecret = Read-Host 'Google OAuth Client Secret' }

$port = 53682
$redirectUri = "http://127.0.0.1:$port/"
$scope = [Uri]::EscapeDataString('https://www.googleapis.com/auth/drive.file')

$authUrl = 'https://accounts.google.com/o/oauth2/v2/auth' +
           "?client_id=$([Uri]::EscapeDataString($ClientId))" +
           "&redirect_uri=$([Uri]::EscapeDataString($redirectUri))" +
           '&response_type=code' +
           "&scope=$scope" +
           '&access_type=offline' +
           '&prompt=consent'

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($redirectUri)

try {
    $listener.Start()
}
catch {
    throw "Could not listen on $redirectUri ($($_.Exception.Message)). Something else may already be using port $port."
}

Write-Host "Opening the consent page in your browser..." -ForegroundColor Cyan
Write-Host "If it does not open automatically, visit:`n$authUrl`n"
try { Start-Process $authUrl } catch { Write-Host "(could not auto-open a browser - copy the URL above)" -ForegroundColor Yellow }

function ConvertFrom-QueryString {
    <# A minimal '?a=1&b=2' -> @{a='1'; b='2'} parser, used instead of System.Web.HttpUtility - that
       type lives in an assembly this NoProfile/-File powershell.exe process does not load automatically,
       and failing to load it here means crashing AFTER Google's redirect already arrived with a real,
       now-wasted authorization code, but BEFORE anything is written back to the browser - which is
       exactly what shows up there as "site can't be reached". #>
    param([string]$Query)

    $result = @{}
    $q = $Query.TrimStart('?')
    if (-not $q) { return $result }
    foreach ($pair in ($q -split '&')) {
        if (-not $pair) { continue }
        $kv = $pair -split '=', 2
        $key = [Uri]::UnescapeDataString($kv[0])
        $value = if ($kv.Count -gt 1) { [Uri]::UnescapeDataString(($kv[1] -replace '\+', ' ')) } else { '' }
        $result[$key] = $value
    }
    return $result
}

Write-Host "Waiting for Google to redirect back to $redirectUri ..."
$context = $listener.GetContext()   # blocks until the browser hits the redirect
$query = ConvertFrom-QueryString -Query $context.Request.Url.Query
$code = $query['code']
$authError = $query['error']

$responseHtml = if ($code) {
    '<html><body><h2>Connected.</h2>You can close this tab and go back to the terminal.</body></html>'
} else {
    "<html><body><h2>Something went wrong.</h2>$authError</body></html>"
}
$buffer = [System.Text.Encoding]::UTF8.GetBytes($responseHtml)
$context.Response.ContentLength64 = $buffer.Length
$context.Response.OutputStream.Write($buffer, 0, $buffer.Length)
$context.Response.OutputStream.Close()
$listener.Stop()

if (-not $code) {
    throw "Google did not return an authorization code (error: $authError)."
}

Write-Host "Exchanging the code for a refresh token..."
$tokenResp = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body @{
    client_id     = $ClientId
    client_secret = $ClientSecret
    code          = $code
    grant_type    = 'authorization_code'
    redirect_uri  = $redirectUri
}

if (-not $tokenResp.refresh_token) {
    throw "Google did not return a refresh token. If you have connected this app before, revoke its " +
          "access at https://myaccount.google.com/permissions and run this again - Google only issues " +
          "a refresh token on the FIRST consent for a given client/account pair."
}

Write-GoogleToken -ClientId $ClientId -ClientSecret $ClientSecret -RefreshToken $tokenResp.refresh_token
Write-Host "Connected. Push-GoogleSheet.ps1 / Pull-GoogleSheet.ps1 (and the Tools > Google Sheets menu in Unity) will use this from now on." -ForegroundColor Green
