<#
.SYNOPSIS
    Shared plumbing for the Google Sheets bridge: OAuth, the Sheets REST calls, the workbook manifest,
    the two probes an .xlsx already uses elsewhere in Tools/, and the label-scan helpers body/level tabs
    need because those tabs are not header/row tables.

.DESCRIPTION
    The bridge is cell-level, not schema-level: it never learns what a card or a body means. Push copies
    xlsx cells to Google: pull copies Google cells back into the xlsx. That is what lets it carry Cards
    and Equipment (genuinely flat header/row tabs, read with Import-Excel exactly like the rest of
    Tools/) and Enemies/Bosses/Levels (label-scanned tabs, read the same "find by label, never by row
    number" way EnemySheet.Common.psm1's Read-BodySheetTab and LevelSheet.Common.psm1's
    Read-LevelSheetTab already do - reusing that convention here rather than hardcoding row numbers,
    which would break the moment Write-EnemyWorkbook.ps1 or Write-LevelWorkbook.ps1's layout shifts).

    Every existing Import-*.ps1 keeps reading the .xlsx exactly as it does today. This module's only job
    is to get phone edits into that file before Import runs, and the current sheet contents out to Google
    after Export runs - nothing downstream changes.

    Auth is an OAuth 2.0 installed-app refresh token, not a service account: the Editor shells out to
    Windows PowerShell 5.1 (.NET Framework), where RSA.ImportPkcs8PrivateKey - needed to sign a service
    account JWT - does not exist. A refresh token needs no signing, just one Invoke-RestMethod POST. It
    also puts the spreadsheets in the user's own Drive, so they show up in their phone's Sheets app
    directly rather than being owned by a robot account and shared out.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------------------------------

function Get-BridgeRoot {
    param()
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..'))
}

function Get-GoogleTokenPath {
    <# Outside the repo on purpose - this file holds a refresh token. GMTK_GOOGLE_TOKEN overrides it for
       anyone who wants the credential somewhere else (a second machine, a synced secrets folder). #>
    param()
    if ($env:GMTK_GOOGLE_TOKEN) { return $env:GMTK_GOOGLE_TOKEN }
    return (Join-Path $env:USERPROFILE '.gmtk-google\token.json')
}

function Get-BridgeStatePath {
    param()
    return (Join-Path $PSScriptRoot 'state.json')
}

function Get-WorkbookManifest {
    <# The one place tab/column layout knowledge lives for the bridge. A .psd1 rather than code so the
       column lists read as data, not logic. #>
    param()
    return (Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'workbooks.psd1'))
}

# ---------------------------------------------------------------------------------------------------
# OAuth
# ---------------------------------------------------------------------------------------------------

$script:CachedAccessToken = $null
$script:CachedAccessTokenExpiresUtc = [DateTime]::MinValue

function Get-ErrorDetailMessage {
    <# $_.ErrorDetails is $null for plenty of real failures (a DNS/connection error, for one) - under
       Set-StrictMode, "$null.Message" throws PropertyNotFoundException instead of just being blank,
       which then replaces the ACTUAL error with a confusing one about a missing 'Message' property.
       Every catch block in this module reads the response body through this instead of touching
       .ErrorDetails.Message directly.

       .ErrorDetails also comes back empty in this environment for at least some Sheets API 400s even
       though the response DOES carry a JSON body explaining what was wrong - so this falls back to
       reading the response stream by hand, which is where the actually useful "invalid range" /
       "duplicate header" / etc. message from Google turns out to live. #>
    param($ErrorRecord)

    if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) { return $ErrorRecord.ErrorDetails.Message }

    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) { return $null }
    try {
        # A fresh HTTP response stream is not seekable, so read it start-to-finish exactly once - no
        # rewinding attempt here, since that itself throws NotSupportedException on this kind of stream.
        $stream = $response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    }
    catch {
        return $null
    }
}

function Read-GoogleToken {
    param()
    $path = Get-GoogleTokenPath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "No Google credential at $path yet. Run Tools/GoogleBridge/Connect-GoogleSheets.ps1 first."
    }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

function Write-GoogleToken {
    param([string]$ClientId, [string]$ClientSecret, [string]$RefreshToken)

    $path = Get-GoogleTokenPath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $payload = [ordered]@{
        clientId     = $ClientId
        clientSecret = $ClientSecret
        refreshToken = $RefreshToken
    }
    Set-Content -LiteralPath $path -Value (ConvertTo-Json -InputObject $payload) -Encoding utf8
    Write-Host "Saved $path" -ForegroundColor Green
}

function Get-GoogleAccessToken {
    <# Exchanges the stored refresh token for an access token, cached for this process's lifetime (they
       last an hour - one process only ever runs one push or pull, so a single cache entry is enough). #>
    param()

    if ($script:CachedAccessToken -and (Get-Date) -lt $script:CachedAccessTokenExpiresUtc) {
        return $script:CachedAccessToken
    }

    $token = Read-GoogleToken
    $body = @{
        client_id     = $token.clientId
        client_secret = $token.clientSecret
        refresh_token = $token.refreshToken
        grant_type    = 'refresh_token'
    }

    try {
        $resp = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body $body
    }
    catch {
        $detail = Get-ErrorDetailMessage -ErrorRecord $_
        throw "Could not refresh the Google access token ($($_.Exception.Message))$(if ($detail) { " - $detail" }). " +
              "Run Connect-GoogleSheets.ps1 again if the refresh token was revoked."
    }

    $script:CachedAccessToken = $resp.access_token
    # 60s of slack so a call started right at the boundary doesn't get a token that expires mid-flight.
    $script:CachedAccessTokenExpiresUtc = (Get-Date).AddSeconds([int]$resp.expires_in - 60)
    return $script:CachedAccessToken
}

# ---------------------------------------------------------------------------------------------------
# Sheets / Drive REST
# ---------------------------------------------------------------------------------------------------

function Invoke-GoogleApi {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [string]$Uri,
        $Body
    )

    $headers = @{ Authorization = "Bearer $(Get-GoogleAccessToken)" }
    $params = @{
        Method  = $Method
        Uri     = $Uri
        Headers = $headers
    }
    if ($null -ne $Body) {
        # -InputObject, never a bare pipe: piping an array (or anything containing one, even nested
        # inside a hashtable) into ConvertTo-Json in this PowerShell version enumerates it element by
        # element and wraps EACH element as {"value":...,"Count":N} instead of a plain JSON array - the
        # exact corruption that made a values:batchUpdate 400 with "Invalid value ... ListValue" the
        # first time this shipped. -InputObject passes the whole object through untouched.
        $params.Body = (ConvertTo-Json -InputObject $Body -Depth 20)
        $params.ContentType = 'application/json'
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = Get-ErrorDetailMessage -ErrorRecord $_
        throw "Google API call failed: $Method $Uri - $($_.Exception.Message)$(if ($detail) { "`n$detail" })"
    }
}

function New-GoogleSpreadsheet {
    <# Creates a spreadsheet with the given tab names (in order) and returns its id. Sheets API always
       creates one default "Sheet1" tab, deleted afterwards so the result matches the manifest exactly. #>
    param([Parameter(Mandatory)] [string]$Title, [Parameter(Mandatory)] [string[]]$SheetTitles)

    $body = @{
        properties = @{ title = $Title }
        sheets     = @($SheetTitles | ForEach-Object { @{ properties = @{ title = $_ } } })
    }
    $resp = Invoke-GoogleApi -Method Post -Uri 'https://sheets.googleapis.com/v4/spreadsheets' -Body $body
    return $resp.spreadsheetId
}

function Get-GoogleSpreadsheetMeta {
    param([Parameter(Mandatory)] [string]$SpreadsheetId)
    return Invoke-GoogleApi -Method Get -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId"
}

function Invoke-GoogleBatchUpdate {
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] [array]$Requests)
    if ($Requests.Count -eq 0) { return }
    Invoke-GoogleApi -Method Post `
        -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId`:batchUpdate" `
        -Body @{ requests = $Requests } | Out-Null
}

function Clear-GoogleTabs {
    <# Wipes each named tab in full before Push-GoogleSheet.ps1 rewrites it. Needed because a values
       write anchored only at the top-left cell (what Set-GoogleValues sends) overwrites the overlapping
       region but leaves anything past the new data's extent untouched - so a tab that shrank since the
       last push (a row deleted straight in Excel, a prefab or level removed) would otherwise leave stale
       rows behind in Google forever. #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] [string[]]$SheetNames)

    if ($SheetNames.Count -eq 0) { return }
    Invoke-GoogleApi -Method Post `
        -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId/values:batchClear" `
        -Body @{ ranges = @($SheetNames | ForEach-Object { "'$_'" }) } | Out-Null
}

function ConvertTo-CellPayload {
    <#
        Turns a cell's string into the JSON type Sheets should store it as: a plain number goes over as a
        JSON number, everything else as a string.

        Necessary because these are sent with valueInputOption RAW, which stores exactly the type it is
        given - a numeric cell sent as the STRING "5" becomes text in the sheet. Ordinary arithmetic
        coerces text back to a number so most formulas still look right, but the aggregate functions do
        not: AVERAGEIF silently skips text cells, so "Average Damage" came out 0 across a deck of 5-damage
        cards until these went over as real numbers.

        The regex guard keeps the conversion to unambiguous decimals - no scientific notation, no
        thousands separators, nothing that could turn a card name or an id into a number by accident -
        and invariant culture keeps a decimal point a decimal point.
    #>
    param([string]$Text)

    if ($Text -match '^-?\d+(\.\d+)?$') {
        $parsed = 0.0
        if ([double]::TryParse($Text, [Globalization.NumberStyles]::Float,
                                [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
            return $parsed
        }
    }
    return $Text
}

function ConvertTo-TypedRows {
    <# Applies ConvertTo-CellPayload across a rows-of-cells matrix, with plain loops throughout - see
       Get-DataRows on what piping does to arrays destined for ConvertTo-Json. #>
    param($Rows)

    $out = @()
    foreach ($row in @($Rows)) {
        $typed = @()
        foreach ($cell in @($row)) { $typed += ConvertTo-CellPayload -Text "$cell" }
        $out += , $typed
    }
    return ,$out
}

function Set-GoogleValues {
    <# One values:batchUpdate call for every range at once - far cheaper than one call per tab. #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] [hashtable]$RangeValues)

    if ($RangeValues.Count -eq 0) { return }
    $data = @($RangeValues.GetEnumerator() | ForEach-Object {
        @{ range = $_.Key; majorDimension = 'ROWS'; values = (ConvertTo-TypedRows -Rows $_.Value) }
    })

    Invoke-GoogleApi -Method Post `
        -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId/values:batchUpdate" `
        -Body @{ valueInputOption = 'RAW'; data = $data } | Out-Null
}

function Get-GoogleValues {
    <#
        .SYNOPSIS
            One values:batchGet call for every range, returned as an ORDERED hashtable keyed by the
            exact range string passed in, in Ranges order.

        .DESCRIPTION
            Deliberately does not key the result off the range text the API echoes back in
            valueRanges[].range - the Sheets API is free to normalise that (e.g. re-quoting a sheet
            name, or filling in the resolved row count) and there is no documented guarantee it is
            byte-identical to what was requested. What IS guaranteed is that valueRanges comes back in
            the same order as the ranges query parameters were given, so this zips the response against
            the caller's own Ranges list positionally instead.

            Values are unformatted, so a typed number comes back as a number rather than locale-formatted
            text. A missing or empty range comes back as an empty array, never null, so callers don't
            need a null-check on every row access.
    #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] [string[]]$Ranges)

    if ($Ranges.Count -eq 0) { return @{} }
    $qs = ($Ranges | ForEach-Object { "ranges=$([Uri]::EscapeDataString($_))" }) -join '&'
    $resp = Invoke-GoogleApi -Method Get `
        -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId/values:batchGet?$qs&valueRenderOption=UNFORMATTED_VALUE"

    $valueRanges = @($resp.valueRanges)
    if ($valueRanges.Count -ne $Ranges.Count) {
        throw "Google returned $($valueRanges.Count) value range(s) for $($Ranges.Count) requested - cannot match them up safely."
    }

    $out = [ordered]@{}
    for ($i = 0; $i -lt $Ranges.Count; $i++) {
        $vr = $valueRanges[$i]
        $out[$Ranges[$i]] = @(if ($vr.PSObject.Properties.Name -contains 'values') { $vr.values } else { @() })
    }
    return $out
}

# ---------------------------------------------------------------------------------------------------
# Bridge state - spreadsheet ids and the per-tab hash recorded at the last successful push. Distinct
# from Tools/*/baseline.json: that baseline is the asset/sheet merge's memory, this is only "what did we
# last hand to Google", so a pull can tell whether Excel moved since.
# ---------------------------------------------------------------------------------------------------

function Read-BridgeState {
    param()
    $path = Get-BridgeStatePath
    if (-not (Test-Path -LiteralPath $path)) {
        return [ordered]@{ spreadsheets = [ordered]@{}; tabHashes = [ordered]@{} }
    }
    $raw = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $state = [ordered]@{ spreadsheets = [ordered]@{}; tabHashes = [ordered]@{} }
    if ($raw.PSObject.Properties.Name -contains 'spreadsheets') {
        foreach ($p in $raw.spreadsheets.PSObject.Properties) { $state.spreadsheets[$p.Name] = $p.Value }
    }
    if ($raw.PSObject.Properties.Name -contains 'tabHashes') {
        foreach ($p in $raw.tabHashes.PSObject.Properties) { $state.tabHashes[$p.Name] = $p.Value }
    }
    return $state
}

function Write-BridgeState {
    param([Parameter(Mandatory)] $State)
    Set-Content -LiteralPath (Get-BridgeStatePath) -Value (ConvertTo-Json -InputObject $State -Depth 8) -Encoding utf8
}

function Get-TabHashKey {
    param([string]$Workbook, [string]$Tab)
    return "$Workbook/$Tab"
}

function Get-DataRows {
    <#
        .SYNOPSIS
            Every row after the first (the header), for something about to be hashed or JSON-serialized.

        .DESCRIPTION
            Deliberately never pipes $Rows through Select-Object/Where-Object to do this. Piping an
            array-of-arrays through either wraps each SURVIVING row in a way ConvertTo-Json cannot tell
            apart from a plain object with "value"/"Count" properties, so it serializes those properties
            instead of the row's actual cells - {"value":["Display Name","Bat"],"Count":2} instead of
            ["Display Name","Bat"]. Two rows with identical CONTENT then hash differently purely because
            one array went through a pipe and the other didn't, which is exactly what made
            Pull-GoogleSheet.ps1 refuse every single body tab immediately after a completely clean push -
            Push's hash used a Select-Object/Where-Object-filtered array, Pull's used a plain loop, and
            the two never agreed no matter how identical the underlying data was.

            Plain array-range indexing carries none of this risk, so that is all this does.
    #>
    param($Rows)

    $arr = @($Rows)
    if ($arr.Count -le 1) { return ,@() }
    return ,$arr[1..($arr.Count - 1)]
}

function Get-CellsHash {
    <# SHA-256 of a canonical JSON of the given rows, so the guard compares content, not formatting or
       object identity. Same purpose as EquipmentSheet.Common.psm1's Get-Sha1Hex, just over row data
       instead of a modifier tree, and SHA-256 because this one is a security-relevant integrity check
       (did the file change under us) rather than a change-detection cache key. #>
    param($Rows)

    # Rebuilt via plain indexing (never a pipe) as a defence in depth against the exact bug
    # Get-DataRows's header comment describes - a caller that hands this a Select-Object/Where-Object-
    # filtered array would otherwise still get a hash that silently disagrees with a clean one built
    # from identical content.
    # Plain @() accumulation, NOT "New-Object object[] n" - New-Object hands back a PSObject-wrapped
    # array that ConvertTo-Json serialises as {"value":[...],"Count":n}. Harmless for a hash as long as
    # both sides do it identically, but it is the same trap that made the dropdown requests invalid, so
    # it is not left lying around here either.
    $source = @($Rows)
    $plain = @()
    foreach ($srcRow in $source) {
        $plainRow = @()
        foreach ($cell in @($srcRow)) { $plainRow += "$cell" }
        $plain += , $plainRow
    }

    # -InputObject, not a pipe - see the matching comment in Invoke-GoogleApi.
    $json = (ConvertTo-Json -InputObject $plain -Depth 10 -Compress)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return [System.BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

# ---------------------------------------------------------------------------------------------------
# The two file-lock probes every Export/Import-*.ps1 already uses, centralised so both bridge scripts
# fail with the same plain-language message the rest of Tools/ does.
# ---------------------------------------------------------------------------------------------------

function Assert-WorkbookReadable {
    <# Shared read - push only reads, and Excel marks ownership with a ~$ file rather than holding the
       .xlsx exclusively, so a saved edit is picked up without closing anything. #>
    param([Parameter(Mandatory)] [string]$WorkbookPath)

    if (-not (Test-Path -LiteralPath $WorkbookPath)) {
        throw "$WorkbookPath does not exist yet - run the sheet's own Export-*.ps1 (or Sync With Sheet) once before pushing it to Google."
    }
    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'Read', 'ReadWrite')
        $probe.Close()
    }
    catch {
        throw "$WorkbookPath cannot be read ($($_.Exception.Message.Trim()))."
    }
}

function Assert-WorkbookWritable {
    <# Exclusive - pull rewrites cells in place and cannot share the file with Excel while doing it. #>
    param([Parameter(Mandatory)] [string]$WorkbookPath)

    try {
        $probe = [IO.File]::Open($WorkbookPath, 'Open', 'ReadWrite', 'None')
        $probe.Close()
    }
    catch {
        throw "$WorkbookPath is open in another program (probably Excel). Close it and pull again."
    }
}

# ---------------------------------------------------------------------------------------------------
# Label-scan helpers for body/level tabs - the same "find by label in column A, never by row number"
# rule Read-BodySheetTab and Read-LevelSheetTab already follow, generalised here to also return the ROW
# NUMBER a label lives on, since push only needs values but pull needs somewhere to write them back.
# ---------------------------------------------------------------------------------------------------

function Get-LabelRowMap {
    <# label (column A text) -> row number, for every non-blank label in the sheet. A label that repeats
       (only "Coords" does, once per wave pair) keeps its FIRST row - callers that care about repeats
       (the Waves block) scan around this map themselves rather than relying on it for those rows. #>
    param($Worksheet)

    $map = [ordered]@{}
    if ($null -eq $Worksheet.Dimension) { return $map }
    $last = $Worksheet.Dimension.End.Row
    for ($r = 1; $r -le $last; $r++) {
        $label = ([string]$Worksheet.Cells[$r, 1].Text).Trim()
        if ($label -eq '' -or $map.Contains($label)) { continue }
        $map[$label] = $r
    }
    return $map
}

function Get-PaddedRow {
    <#
        .SYNOPSIS
            Pads a fetched Google row out to MinLength, since the Sheets API omits trailing empty cells
            from a row's values array entirely rather than returning them as "".

        .DESCRIPTION
            Every caller uses this from inside "$rows | ForEach-Object { Get-PaddedRow -Row $_ ... }" -
            which means the leading comma on the return is not optional. Without it, a call whose result
            is a multi-element array gets that array ENUMERATED by ForEach-Object's own output handling,
            same as a plain "return $array" does at a normal function boundary. With more than one row
            flowing through the pipe this does not fail loudly - it silently flattens every row's cells
            into one giant list with no row boundaries left, so $googleRows[$i][$j] ends up reading
            whatever cell happened to land at that position rather than row i's column j. That is what
            let a real GUID column comparison come back reading a completely unrelated item's name.
    #>
    param($Row, [int]$MinLength)

    $arr = @($Row)
    if ($arr.Count -ge $MinLength) { return ,$arr }
    return ,($arr + (@('') * ($MinLength - $arr.Count)))
}

function Get-EnumLists {
    <#
        .SYNOPSIS
            Every dropdown list on a workbook's Enums tab, as @{ Effect = @(...); EnemyCard = @(...) }.

        .DESCRIPTION
            The Enums tab is one list per column, list name in row 1, regenerated by each Export-*.ps1
            from the C# enums and the real asset names. Reading it here means Google's dropdowns are
            built from the exact same source Excel's own dropdowns are, so the two can never drift and
            nothing has to duplicate a copy of any enum.

            Columns with no values are skipped - each Enums tab ends with a prose note in row 1 of a
            trailing column ("Regenerated on every export - edit the C# enum, not this sheet"), which is
            a header with nothing under it rather than a list.
    #>
    param($Worksheet)

    $lists = @{}
    if ($null -eq $Worksheet -or $null -eq $Worksheet.Dimension) { return $lists }

    $lastCol = $Worksheet.Dimension.End.Column
    $lastRow = $Worksheet.Dimension.End.Row
    for ($c = 1; $c -le $lastCol; $c++) {
        $name = ([string]$Worksheet.Cells[1, $c].Text).Trim()
        if ($name -eq '') { continue }
        $vals = @()
        for ($r = 2; $r -le $lastRow; $r++) {
            $v = ([string]$Worksheet.Cells[$r, $c].Text).Trim()
            if ($v -ne '') { $vals += $v }
        }
        if ($vals.Count -eq 0) { continue }
        $lists[$name] = $vals
    }
    return $lists
}

function Set-GoogleDropdowns {
    <#
        .SYNOPSIS
            Applies ONE_OF_LIST data validation (a real dropdown) to the given ranges.

        .DESCRIPTION
            Specs are @{ Sheet; StartRow; EndRow; StartCol; EndCol; Values } with ZERO-BASED, half-open
            row/column indexes, matching the Sheets API's own GridRange. strict = $false so a value not
            on the list is flagged rather than rejected - the same call Write-CardWorkbook.ps1 makes for
            Excel (ShowErrorMessage = $false), and it matters: an effect or card that exists in the
            assets but not yet in a regenerated Enums tab must never become untypeable.

            Sent in chunks because each request carries its own full copy of the value list (a 100-entry
            effect list across three columns on nine tabs adds up fast) and one oversized batchUpdate
            fails wholesale.
    #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] $Meta, $Specs)

    $specs = @($Specs)
    if ($specs.Count -eq 0) { return }

    $sheetIds = @{}
    foreach ($sheet in $Meta.sheets) { $sheetIds[$sheet.properties.title] = $sheet.properties.sheetId }

    $requests = @()
    foreach ($spec in $specs) {
        if (-not $sheetIds.ContainsKey($spec.Sheet)) { continue }
        $values = @($spec.Values)
        if ($values.Count -eq 0) { continue }

        # Plain @() accumulation, NOT "New-Object object[] n" - New-Object's result comes back through
        # the pipeline PSObject-wrapped, and ConvertTo-Json then serialises that wrapper's adapted
        # properties ({"value":[...],"Count":n}) instead of a JSON array, which the API rejects with
        # 'Unknown name "value" ... Cannot find field'.
        $entries = @()
        foreach ($v in $values) { $entries += @{ userEnteredValue = "$v" } }

        $requests += @{
            setDataValidation = @{
                range = @{
                    sheetId          = $sheetIds[$spec.Sheet]
                    startRowIndex    = $spec.StartRow
                    endRowIndex      = $spec.EndRow
                    startColumnIndex = $spec.StartCol
                    endColumnIndex   = $spec.EndCol
                }
                rule = @{
                    condition    = @{ type = 'ONE_OF_LIST'; values = $entries }
                    showCustomUi = $true
                    strict       = $false
                }
            }
        }
    }

    $chunk = 20
    for ($i = 0; $i -lt $requests.Count; $i += $chunk) {
        $end = [Math]::Min($i + $chunk - 1, $requests.Count - 1)
        Invoke-GoogleBatchUpdate -SpreadsheetId $SpreadsheetId -Requests @($requests[$i..$end])
    }
}

function ConvertFrom-ColumnLetter {
    <# "A" -> 1, "B" -> 2, "AA" -> 27. #>
    param([string]$Letters)
    $n = 0
    foreach ($ch in $Letters.ToUpperInvariant().ToCharArray()) { $n = ($n * 26) + ([int][char]$ch - 64) }
    return $n
}

function ConvertFrom-ExcelAddress {
    <#
        Parses an EPPlus named-range address - "'PowerLevel'!$B$2" or "'PowerLevel'!$A$38:$B$43" - into
        @{ Sheet; StartRow; EndRow; StartCol; EndCol } with ZERO-BASED, half-open indexes ready for a
        Sheets API GridRange. Returns $null for anything it does not recognise (a multi-area name, say),
        so a caller can skip rather than guess.
    #>
    param([string]$Address)

    if ($Address -notmatch "^'?([^'!]+)'?!(.+)$") { return $null }
    $sheet = $Matches[1]
    $cells = $Matches[2] -replace '\$', ''
    if ($cells -match ',') { return $null }

    $parts = $cells -split ':'
    if ($parts[0] -notmatch '^([A-Za-z]+)(\d+)$') { return $null }
    $startCol = ConvertFrom-ColumnLetter $Matches[1]
    $startRow = [int]$Matches[2]

    $endCol = $startCol
    $endRow = $startRow
    if ($parts.Count -gt 1) {
        if ($parts[1] -notmatch '^([A-Za-z]+)(\d+)$') { return $null }
        $endCol = ConvertFrom-ColumnLetter $Matches[1]
        $endRow = [int]$Matches[2]
    }

    return @{
        Sheet = $sheet
        StartRow = ($startRow - 1); EndRow = $endRow
        StartCol = ($startCol - 1); EndCol = $endCol
    }
}

function Convert-FormulaRowRefs {
    <#
        .SYNOPSIS
            Rewrites the ROW numbers in an Excel formula so it refers to the same labels at the row
            positions the Google mirror puts them at.

        .DESCRIPTION
            The workbook's own layout and the Google mirror's differ (the mirror is a Field/Value list
            plus a reference block), so a formula like "B$32*pw_Damage" would otherwise point at whatever
            happens to sit at row 32 over here. RowMap is Excel row -> Google row, built from the labels
            present on both sides.

            Column letters are deliberately left alone: deck card N is column B+N on both sides, so
            "B$26:F$26" spans the same cards in both. Named ranges (pw_Damage, range_Power, pow_Skeleton)
            carry no row number and are untouched - note the lookbehind/lookahead, without which the
            trailing "2" of a name like pow_EvilWizard2 would be rewritten as if it were a row.
    #>
    param([string]$Formula, [hashtable]$RowMap)

    if ([string]::IsNullOrEmpty($Formula)) { return '' }

    $evaluator = {
        param($m)
        $row = [int]$m.Groups[4].Value
        $mapped = if ($RowMap.ContainsKey($row)) { $RowMap[$row] } else { $row }
        return "$($m.Groups[1].Value)$($m.Groups[2].Value)$($m.Groups[3].Value)$mapped"
    }
    return [regex]::Replace($Formula, '(?<![A-Za-z0-9_])(\$?)([A-Za-z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_(])', $evaluator)
}

function Set-GoogleFormulas {
    <# Writes cells as FORMULAS. Separate from Set-GoogleValues because that one sends RAW on purpose -
       under RAW a leading "=" is stored as literal text, and under USER_ENTERED Google would coerce
       ordinary card names and TRUE/FALSE strings into other types. #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] [hashtable]$RangeValues)

    if ($RangeValues.Count -eq 0) { return }
    $data = @()
    foreach ($entry in $RangeValues.GetEnumerator()) {
        $data += @{ range = $entry.Key; majorDimension = 'ROWS'; values = $entry.Value }
    }

    Invoke-GoogleApi -Method Post `
        -Uri "https://sheets.googleapis.com/v4/spreadsheets/$SpreadsheetId/values:batchUpdate" `
        -Body @{ valueInputOption = 'USER_ENTERED'; data = $data } | Out-Null
}

function Set-GoogleNamedRanges {
    <# Recreates the given named ranges, replacing any of the same name that already exist (a re-push
       must not fail with "already exists", and an address may have moved). Specs are
       @{ Name; Sheet; StartRow; EndRow; StartCol; EndCol } with zero-based, half-open indexes. #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] $Meta, $Specs)

    $specs = @($Specs)
    if ($specs.Count -eq 0) { return }

    $sheetIds = @{}
    foreach ($sheet in $Meta.sheets) { $sheetIds[$sheet.properties.title] = $sheet.properties.sheetId }

    $wanted = @{}
    foreach ($spec in $specs) { $wanted[$spec.Name] = $true }

    $requests = @()
    if ($Meta.PSObject.Properties.Name -contains 'namedRanges') {
        foreach ($existing in @($Meta.namedRanges)) {
            if ($null -ne $existing -and $wanted.ContainsKey($existing.name)) {
                $requests += @{ deleteNamedRange = @{ namedRangeId = $existing.namedRangeId } }
            }
        }
    }

    foreach ($spec in $specs) {
        if (-not $sheetIds.ContainsKey($spec.Sheet)) { continue }
        $requests += @{
            addNamedRange = @{
                namedRange = @{
                    name  = $spec.Name
                    range = @{
                        sheetId          = $sheetIds[$spec.Sheet]
                        startRowIndex    = $spec.StartRow
                        endRowIndex      = $spec.EndRow
                        startColumnIndex = $spec.StartCol
                        endColumnIndex   = $spec.EndCol
                    }
                }
            }
        }
    }

    $chunk = 40
    for ($i = 0; $i -lt $requests.Count; $i += $chunk) {
        $end = [Math]::Min($i + $chunk - 1, $requests.Count - 1)
        Invoke-GoogleBatchUpdate -SpreadsheetId $SpreadsheetId -Requests @($requests[$i..$end])
    }
}

function Set-GoogleRowShading {
    <# Greys a block of rows so a read-only reference section is visibly not for editing. Specs are
       @{ Sheet; StartRow; EndRow } with zero-based, half-open row indexes. #>
    param([Parameter(Mandatory)] [string]$SpreadsheetId, [Parameter(Mandatory)] $Meta, $Specs)

    $specs = @($Specs)
    if ($specs.Count -eq 0) { return }

    $sheetIds = @{}
    foreach ($sheet in $Meta.sheets) { $sheetIds[$sheet.properties.title] = $sheet.properties.sheetId }

    $requests = @()
    foreach ($spec in $specs) {
        if (-not $sheetIds.ContainsKey($spec.Sheet)) { continue }
        $requests += @{
            repeatCell = @{
                range = @{
                    sheetId       = $sheetIds[$spec.Sheet]
                    startRowIndex = $spec.StartRow
                    endRowIndex   = $spec.EndRow
                }
                cell   = @{ userEnteredFormat = @{
                    backgroundColor = @{ red = 0.93; green = 0.93; blue = 0.93 }
                    textFormat      = @{ italic = $true }
                    # Computed cells are raw floats (2.4000000000000004), which is unreadable on a phone.
                    # A display format only - the underlying value is untouched, and it is inert on the
                    # text rows in the same block (Actions from Card, Counted).
                    numberFormat    = @{ type = 'NUMBER'; pattern = '0.###' }
                } }
                fields = 'userEnteredFormat.backgroundColor,userEnteredFormat.textFormat.italic,userEnteredFormat.numberFormat'
            }
        }
    }

    $chunk = 20
    for ($i = 0; $i -lt $requests.Count; $i += $chunk) {
        $end = [Math]::Min($i + $chunk - 1, $requests.Count - 1)
        Invoke-GoogleBatchUpdate -SpreadsheetId $SpreadsheetId -Requests @($requests[$i..$end])
    }
}

function Get-BodyFieldPropertyName {
    <# Read-BodySheetTab's PSCustomObject properties mostly follow "strip the spaces out of the label"
       (Display Name -> DisplayName, Loot Table -> LootTable, ...), but Brandon's Power Level was
       hand-named Brandon. Shared by Push- and Pull-GoogleSheet.ps1 so the two can never disagree about
       which property a manifest field name means. #>
    param([string]$Field)

    $map = @{ "Brandon's Power Level" = 'Brandon' }
    if ($map.ContainsKey($Field)) { return $map[$Field] }
    return ($Field -replace '[^A-Za-z0-9]', '')
}

function Get-HeaderColumnMap {
    <# header text (row 1) -> column number, for a genuinely flat tab. Built by hand rather than via
       Import-Excel because the write side needs the column INDEX, not just the value. #>
    param($Worksheet)

    $map = [ordered]@{}
    if ($null -eq $Worksheet.Dimension) { return $map }
    $lastCol = $Worksheet.Dimension.End.Column
    for ($c = 1; $c -le $lastCol; $c++) {
        $name = ([string]$Worksheet.Cells[1, $c].Text).Trim()
        if ($name -eq '') { continue }
        $map[$name] = $c
    }
    return $map
}

Export-ModuleMember -Function *
