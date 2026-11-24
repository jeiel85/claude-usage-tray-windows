#requires -Version 7.0
<#
.SYNOPSIS
    Google Play Console 에 서명된 Play AAB(com.jeiel85.aiusage)를 업로드한다(로컬·CI 공용).

.DESCRIPTION
    태그 push 로 release.yml 이 만든 play-bundle 아티팩트(ClaudeUsageTray-Mobile-play.aab) 또는 로컬 AAB 를
    검증한 뒤, 서드파티 Action/패키지 없이 Google Play Developer API v3(OAuth2 JWT → edits → bundles → tracks → commit)로
    제출한다(기본 트랙: alpha 비공개 테스트, 상태: completed).
      1. 태그 형식(vX.Y.Z, minor/patch 0..99 → versionCode = X*10000 + Y*100 + Z) 및 origin/master 포함 여부 검증.
      2. 직전 v* 태그 대비 mobile/ 변경 여부 확인 — PC 전용 패치 태그를 실수로 Play 에 올리지 않도록 차단(-Force 로 우회).
      3. AAB 구조(BundleConfig.pb, base/manifest/AndroidManifest.xml) 및 Play 업로드 키 인증서 SHA-256 검증.
      4. CHANGELOG.md 에서 ko-KR / en-US 릴리스 노트 추출(마크다운 정리 후 500자 제한).
      5. -DryRun 이 아니면 서비스 계정 JWT 인증 후 Google Play API 5단계 수행(실패 시 edit 자동 삭제).

.PARAMETER Tag
    배포할 릴리스 태그(예: v1.73.0).

.PARAMETER Track
    업로드할 트랙 이름. 기본값은 alpha(비공개 테스트). internal, beta, production 또는 사용자 지정 트랙명을 쓸 수 있다.

.PARAMETER Status
    트랙 릴리스 상태(completed | draft | inProgress | halted). 기본값은 completed.

.PARAMETER UserFraction
    단계적 출시 비율(0.0 < 비율 < 1.0). Status 가 inProgress 또는 halted 일 때만 지정한다.

.PARAMETER AabPath
    로컬에 있는 서명된 .aab 파일 경로. 생략하면 GitHub Actions release.yml 이 해당 태그 커밋에서 생성한 play-bundle 아티팩트를 내려받는다.

.PARAMETER ServiceAccountPath
    Google Play 서비스 계정 JSON 키 경로. 생략 시 CUT_PLAY_SERVICE_ACCOUNT_JSON → CUT_PLAY_SERVICE_ACCOUNT_FILE → <루트>/.keystore/play-service-account.json 순으로 찾는다.

.PARAMETER ReleaseNotesKo
    한국어 출시 노트 재정의(최대 500자). 생략하면 CHANGELOG.md 에서 추출한다.

.PARAMETER ReleaseNotesEn
    영어 출시 노트 재정의(최대 500자). 생략하면 CHANGELOG.md 에서 추출한다.

.PARAMETER ChangesNotSentForReview
    커밋 시 ?changesNotSentForReview=true 를 붙인다(기존 대기 중 검토 충돌 시 사용).

.PARAMETER Force
    직전 태그 이후 mobile/ 변경이 없어도 강제로 진행한다.

.PARAMETER DryRun
    Google Play API 를 호출하지 않고 태그·mobile/ 변경·AAB·인증서·릴리스 노트·JWT 서명까지만 검증한다.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Tag = '',
    [string]$Track = 'alpha',
    [ValidateSet('completed', 'draft', 'inProgress', 'halted')][string]$Status = 'completed',
    [ValidateRange(0.0, 1.0)][double]$UserFraction = 0.0,
    [string]$AabPath = '',
    [string]$ServiceAccountPath = '',
    [string]$ReleaseNotesKo = '',
    [string]$ReleaseNotesEn = '',
    [switch]$ChangesNotSentForReview,
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$script:ExpectedPackageName = 'com.jeiel85.aiusage'
$script:ExpectedUploadCertSha256 = '4C:65:B6:47:15:49:79:66:11:2C:FF:58:44:50:7A:5E:0C:EB:96:AF:7D:97:1E:E4:61:43:4E:62:A6:56:52:22'
$script:GoogleTokenUri = 'https://oauth2.googleapis.com/token'
$script:GooglePlayApiBaseUri = 'https://androidpublisher.googleapis.com'
$script:DefaultRepo = 'jeiel85/claude-usage-tray-windows'

function ConvertTo-Base64Url {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes
    )
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertFrom-PlayTagVersion {
    param(
        [Parameter(Mandatory)][string]$Tag
    )
    if ($Tag -notmatch '^v(\d+)\.(\d+)\.(\d+)$') {
        throw "태그는 vX.Y.Z 형식이어야 합니다: $Tag"
    }
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]
    # mobile/app/build.gradle.kts 의 versionCode 공식(major*10000 + minor*100 + patch)과 동일해야 한다.
    if ($minor -lt 0 -or $minor -gt 99 -or $patch -lt 0 -or $patch -gt 99) {
        throw "태그 $Tag 의 minor($minor)/patch($patch)는 0..99 범위여야 합니다."
    }
    $versionCode = [long]$major * 10000L + [long]$minor * 100L + [long]$patch
    if ($versionCode -le 0) {
        throw "versionCode 는 0 보다 커야 합니다: $Tag"
    }
    return [pscustomobject]@{
        Tag         = $Tag
        VersionText = "$major.$minor.$patch"
        Version     = [Version]::new($major, $minor, $patch)
        VersionCode = $versionCode
    }
}

function Format-PlayReleaseNoteText {
    param(
        [AllowEmptyString()][string]$RawText = '',
        [string]$FallbackText = '',
        [int]$MaxLength = 500
    )
    if ($MaxLength -lt 1) {
        throw "MaxLength 는 1 이상이어야 합니다: $MaxLength"
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $prevBlank = $true
    foreach ($rawLine in ($RawText -split "`r?`n")) {
        $line = $rawLine -replace '^#{1,6}\s+', ''
        $line = [regex]::Replace($line, '\[([^\]]+)\]\([^)]+\)', '$1')
        $line = $line.Replace('**', '').Replace('`', '')
        $line = $line.TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line)) {
            if (-not $prevBlank) {
                $lines.Add('')
                $prevBlank = $true
            }
            continue
        }
        $lines.Add($line)
        $prevBlank = $false
    }

    $clean = (($lines -join "`n").Trim())
    if ([string]::IsNullOrWhiteSpace($clean)) {
        $clean = $FallbackText.Trim()
    }
    if ($clean.Length -gt $MaxLength) {
        if ($MaxLength -eq 1) {
            return $clean.Substring(0, 1)
        }
        $clean = $clean.Substring(0, $MaxLength - 1).TrimEnd() + '…'
    }
    return $clean
}

function Get-PlayChangelogNotes {
    param(
        [Parameter(Mandatory)][string]$ChangelogPath,
        [Parameter(Mandatory)][string]$VersionText,
        [int]$MaxLength = 500
    )
    if (-not (Test-Path -LiteralPath $ChangelogPath -PathType Leaf)) {
        throw "CHANGELOG 파일을 찾을 수 없습니다: $ChangelogPath"
    }

    $content = [System.IO.File]::ReadAllText($ChangelogPath, [System.Text.Encoding]::UTF8)
    $escapedVer = [regex]::Escape($VersionText)
    $sectionLines = [System.Collections.Generic.List[string]]::new()
    $inside = $false

    foreach ($line in ($content -split "`r?`n")) {
        if ($inside) {
            if ($line -match '^##\s+\[') {
                break
            }
            $sectionLines.Add($line)
        }
        elseif ($line -match "^##\s+\[$escapedVer\](?:$|\s|-)") {
            $inside = $true
        }
    }

    $section = $sectionLines -join "`n"
    $koRaw = ''
    $enRaw = ''
    if ($inside) {
        $koMatch = [regex]::Match($section, '<!--\s*ko\s*-->([\s\S]*?)<!--\s*/ko\s*-->')
        if ($koMatch.Success) { $koRaw = $koMatch.Groups[1].Value }
        $enMatch = [regex]::Match($section, '<!--\s*en\s*-->([\s\S]*?)<!--\s*/en\s*-->')
        if ($enMatch.Success) { $enRaw = $enMatch.Groups[1].Value }
    }

    return [pscustomobject]@{
        Ko = Format-PlayReleaseNoteText -RawText $koRaw -FallbackText "v$VersionText 업데이트" -MaxLength $MaxLength
        En = Format-PlayReleaseNoteText -RawText $enRaw -FallbackText "Release v$VersionText" -MaxLength $MaxLength
    }
}

function Resolve-KeytoolExecutable {
    $cmd = Get-Command keytool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    if ($env:JAVA_HOME) {
        foreach ($candidateName in 'keytool.exe', 'keytool') {
            $candidate = Join-Path $env:JAVA_HOME (Join-Path 'bin' $candidateName)
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }
    return $null
}

function Test-PlayAabArchive {
    param(
        [Parameter(Mandatory)][string]$AabPath,
        [string]$ExpectedCertSha256 = $script:ExpectedUploadCertSha256,
        [switch]$SkipCertCheck
    )
    if (-not (Test-Path -LiteralPath $AabPath -PathType Leaf)) {
        throw "AAB 파일을 찾을 수 없습니다: $AabPath"
    }

    $sizeBytes = (Get-Item -LiteralPath $AabPath).Length
    if ($sizeBytes -le 0) {
        throw "AAB 파일이 비어 있습니다: $AabPath"
    }
    if ($sizeBytes -gt 150MB) {
        throw "AAB 파일 크기가 Google Play 한도(150MB)를 초과합니다: $([math]::Round($sizeBytes / 1MB, 2)) MB"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($AabPath)
    try {
        $entryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($entry in $zip.Entries) {
            [void]$entryNames.Add($entry.FullName.Replace('\', '/'))
        }
        foreach ($required in 'BundleConfig.pb', 'base/manifest/AndroidManifest.xml') {
            if (-not $entryNames.Contains($required)) {
                throw "올바른 Android App Bundle(.aab)이 아닙니다 — '$required' 항목이 없습니다: $AabPath"
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    $certSha256 = ''
    if (-not $SkipCertCheck) {
        $keytool = Resolve-KeytoolExecutable
        if (-not $keytool) {
            throw 'keytool 을 찾을 수 없습니다. JDK(JAVA_HOME)를 설치하거나 PATH 에 추가하세요.'
        }
        $certLines = & $keytool '-J-Duser.language=en' '-printcert' '-jarfile' $AabPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "AAB 서명 인증서를 읽지 못했습니다: $($certLines -join "`n")"
        }
        $certOutput = $certLines -join "`n"
        $shaMatch = [regex]::Match($certOutput, 'SHA256:\s*([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){31})')
        if (-not $shaMatch.Success) {
            throw "AAB 에서 서명 SHA-256 지문을 찾지 못했습니다(미서명 번들인지 확인하세요)."
        }
        $certSha256 = $shaMatch.Groups[1].Value.ToUpperInvariant()
        $expectedNormalized = $ExpectedCertSha256.ToUpperInvariant()
        if ($certSha256 -ne $expectedNormalized) {
            throw "AAB 서명 인증서가 Play 업로드 키($expectedNormalized)와 다릅니다: $certSha256"
        }
    }

    return [pscustomobject]@{
        AabPath    = (Resolve-Path -LiteralPath $AabPath).Path
        SizeBytes  = $sizeBytes
        CertSha256 = $certSha256
    }
}

function New-PlayServiceAccountJwt {
    param(
        [Parameter(Mandatory)][psobject]$ServiceAccount,
        [string]$TokenUri = $script:GoogleTokenUri,
        [long]$NowUnix = -1
    )
    if ($ServiceAccount.type -ne 'service_account') {
        throw "서비스 계정 JSON 의 type 이 'service_account' 가 아닙니다."
    }
    $clientEmail = [string]$ServiceAccount.client_email
    $privateKeyPem = [string]$ServiceAccount.private_key
    if ([string]::IsNullOrWhiteSpace($clientEmail) -or [string]::IsNullOrWhiteSpace($privateKeyPem)) {
        throw '서비스 계정 JSON 에 client_email 또는 private_key 가 없습니다.'
    }
    if ($privateKeyPem -notmatch '-----BEGIN (?:RSA )?PRIVATE KEY-----') {
        throw '서비스 계정 JSON 의 private_key 형식이 올바른 PEM 이 아닙니다.'
    }

    if ($NowUnix -lt 0) {
        $NowUnix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    }

    $headerObj = [ordered]@{
        alg = 'RS256'
        typ = 'JWT'
    }
    $keyIdProp = $ServiceAccount.PSObject.Properties['private_key_id']
    if ($keyIdProp -and -not [string]::IsNullOrWhiteSpace([string]$keyIdProp.Value)) {
        $headerObj.kid = [string]$keyIdProp.Value
    }

    $claimsObj = [ordered]@{
        iss   = $clientEmail
        scope = 'https://www.googleapis.com/auth/androidpublisher'
        aud   = $TokenUri
        iat   = $NowUnix
        exp   = $NowUnix + 3600L
    }

    $headerB64 = ConvertTo-Base64Url -Bytes ([Text.Encoding]::UTF8.GetBytes(($headerObj | ConvertTo-Json -Compress)))
    $claimsB64 = ConvertTo-Base64Url -Bytes ([Text.Encoding]::UTF8.GetBytes(($claimsObj | ConvertTo-Json -Compress)))
    $signingInput = "$headerB64.$claimsB64"

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($privateKeyPem)
        $sigBytes = $rsa.SignData(
            [Text.Encoding]::ASCII.GetBytes($signingInput),
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
        )
        return "$signingInput.$(ConvertTo-Base64Url -Bytes $sigBytes)"
    }
    finally {
        $rsa.Dispose()
    }
}

function New-PlayTrackReleasePayload {
    param(
        [Parameter(Mandatory)][string]$Track,
        [Parameter(Mandatory)][long]$VersionCode,
        [Parameter(Mandatory)][string]$ReleaseName,
        [Parameter(Mandatory)][string]$Status,
        [double]$UserFraction = 0.0,
        [string]$NotesKo = '',
        [string]$NotesEn = ''
    )
    if ([string]::IsNullOrWhiteSpace($Track) -or $Track -notmatch '^[A-Za-z0-9 _.-]{1,64}$') {
        throw "트랙 이름이 올바르지 않습니다: '$Track'"
    }
    if ($Status -notin @('completed', 'draft', 'inProgress', 'halted')) {
        throw "지원하지 않는 릴리스 상태입니다: '$Status'"
    }
    if ($VersionCode -le 0) {
        throw "versionCode 는 0 보다 커야 합니다: $VersionCode"
    }

    if ($Status -eq 'inProgress') {
        if ($UserFraction -le 0.0 -or $UserFraction -ge 1.0) {
            throw "Status 가 'inProgress' 일 때는 0 < UserFraction < 1 값을 지정해야 합니다(현재: $UserFraction)."
        }
    }
    elseif ($Status -in @('completed', 'draft')) {
        if ($UserFraction -gt 0.0) {
            throw "Status 가 '$Status' 일 때는 UserFraction 을 지정할 수 없습니다(현재: $UserFraction)."
        }
    }
    elseif ($Status -eq 'halted' -and $UserFraction -ge 1.0) {
        throw "Status 가 'halted' 일 때는 UserFraction 이 1.0 미만이어야 합니다(현재: $UserFraction)."
    }

    $releaseNotes = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in @(
            @{ Language = 'ko-KR'; Text = $NotesKo },
            @{ Language = 'en-US'; Text = $NotesEn }
        )) {
        if ([string]::IsNullOrWhiteSpace($entry.Text)) { continue }
        $trimmed = $entry.Text.Trim()
        if ($trimmed.Length -gt 500) {
            throw "$($entry.Language) 릴리스 노트가 Google Play 제한(500자)을 초과합니다: $($trimmed.Length)자"
        }
        $releaseNotes.Add([ordered]@{
                language = $entry.Language
                text     = $trimmed
            })
    }

    $release = [ordered]@{
        name         = $ReleaseName
        versionCodes = @([string]$VersionCode)
        status       = $Status
    }
    if (($Status -eq 'inProgress') -or ($Status -eq 'halted' -and $UserFraction -gt 0.0)) {
        $release.userFraction = [Math]::Round($UserFraction, 6)
    }
    if ($releaseNotes.Count -gt 0) {
        $release.releaseNotes = $releaseNotes.ToArray()
    }

    $payload = [ordered]@{
        track    = $Track
        releases = @($release)
    }
    return ($payload | ConvertTo-Json -Depth 5 -Compress)
}

function Test-MobileChangedSincePreviousTag {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Tag
    )
    $parsedTarget = ConvertFrom-PlayTagVersion -Tag $Tag
    $rawTags = git -C $RepoRoot tag --list 'v*'
    if ($LASTEXITCODE -ne 0) {
        throw 'git tag 목록을 조회하지 못했습니다.'
    }

    $parsedTags = [System.Collections.Generic.List[object]]::new()
    foreach ($t in $rawTags) {
        if ([string]::IsNullOrWhiteSpace($t)) { continue }
        $trimmed = $t.Trim()
        if ($trimmed -match '^v(\d+)\.(\d+)\.(\d+)$') {
            $parsedTags.Add([pscustomobject]@{
                    Tag     = $trimmed
                    Version = [Version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
                })
        }
    }

    $sortedDesc = @($parsedTags | Sort-Object Version -Descending)
    $olderTags = @($sortedDesc | Where-Object { $_.Version -lt $parsedTarget.Version })
    $previousTag = if ($olderTags.Count -gt 0) { $olderTags[0].Tag } else { $null }

    if (-not $previousTag) {
        return [pscustomobject]@{
            Changed       = $true
            PreviousTag   = $null
            ChangedFiles  = @('mobile/')
            LastMobileTag = $Tag
        }
    }

    $diffOut = git -C $RepoRoot diff --name-only "$previousTag..$Tag" -- 'mobile/'
    if ($LASTEXITCODE -ne 0) {
        throw "$previousTag..$Tag 구간의 mobile/ 변경 사항을 확인하지 못했습니다."
    }
    $changedFiles = @($diffOut | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $lastMobileTag = $null
    if ($changedFiles.Count -gt 0) {
        $lastMobileTag = $Tag
    }
    else {
        # 기준 태그 이하의 v* 태그 중 가장 최근에 mobile/ 을 바꾼 태그를 안내용으로 찾는다(최대 30개).
        $atOrBefore = @($sortedDesc | Where-Object { $_.Version -le $parsedTarget.Version })
        $limit = [Math]::Min($atOrBefore.Count - 1, 30)
        for ($i = 1; $i -lt $limit; $i++) {
            $curr = $atOrBefore[$i]
            $prev = $atOrBefore[$i + 1]
            $d = git -C $RepoRoot diff --name-only "$($prev.Tag)..$($curr.Tag)" -- 'mobile/'
            if ($LASTEXITCODE -eq 0 -and (@($d | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0)) {
                $lastMobileTag = $curr.Tag
                break
            }
        }
    }

    return [pscustomobject]@{
        Changed       = ($changedFiles.Count -gt 0)
        PreviousTag   = $previousTag
        ChangedFiles  = $changedFiles
        LastMobileTag = $lastMobileTag
    }
}

function Format-PlayApiErrorHint {
    param(
        [int]$StatusCode,
        [string]$ResponseBody = '',
        [string]$PackageName = $script:ExpectedPackageName
    )
    if ($ResponseBody -match 'Only releases with status draft may be created on draft app') {
        return '초안 앱(Play Console 첫 심사 제출 전)에서는 -Status draft (GitHub Actions: status=draft)를 사용해야 합니다.'
    }
    if ($ResponseBody -match 'changesNotSentForReview') {
        return 'Play Console 에 아직 검토 제출되지 않은 변경이 있어 충돌했습니다. -ChangesNotSentForReview (GitHub Actions: changes_not_sent_for_review=true) 옵션을 사용하세요.'
    }
    if ($ResponseBody -match 'already been used|APK specifies a version code that has already been used') {
        return '이 versionCode 는 이미 Play Console 에 업로드된 적이 있습니다. 새 앱 버전으로 태그를 다시 만들어야 합니다.'
    }
    if ($StatusCode -eq 403 -or $ResponseBody -match 'PERMISSION_DENIED|sufficient permissions') {
        return "Play Console '사용자 및 권한'에서 서비스 계정에 $PackageName 앱 릴리스 권한이 부여되어 있는지 확인하세요(권한 저장 직후에는 반영까지 시간이 걸릴 수 있습니다)."
    }
    if ($StatusCode -eq 401 -or $ResponseBody -match 'UNAUTHENTICATED|invalid_grant') {
        return '서비스 계정 인증에 실패했습니다. JSON 키가 올바르고 Google Play Android Developer API 가 활성화되어 있는지 확인하세요.'
    }
    return ''
}

function Invoke-PlayHttpRequest {
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpClient]$HttpClient,
        [Parameter(Mandatory)][System.Net.Http.HttpMethod]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [string]$AccessToken = '',
        [System.Net.Http.HttpContent]$Content = $null,
        [string]$PackageName = $script:ExpectedPackageName
    )
    $req = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
            $req.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $AccessToken)
        }
        $req.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
        if ($null -ne $Content) {
            $req.Content = $Content
        }

        $resp = $HttpClient.SendAsync($req).GetAwaiter().GetResult()
        try {
            $respBody = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $statusCode = [int]$resp.StatusCode
            if (-not $resp.IsSuccessStatusCode) {
                $apiMsg = $respBody
                try {
                    $errJson = $respBody | ConvertFrom-Json
                    if ($errJson.PSObject.Properties['error'] -and $errJson.error.PSObject.Properties['message']) {
                        $apiMsg = [string]$errJson.error.message
                    }
                    elseif ($errJson.PSObject.Properties['error_description']) {
                        $apiMsg = "$($errJson.error): $($errJson.error_description)"
                    }
                }
                catch { }
                $hint = Format-PlayApiErrorHint -StatusCode $statusCode -ResponseBody $respBody -PackageName $PackageName
                $msg = "Google Play API 요청 실패 (HTTP $statusCode, $($Method.Method) $Uri): $apiMsg"
                if ($hint) { $msg += "`n안내: $hint" }
                throw $msg
            }
            if ([string]::IsNullOrWhiteSpace($respBody)) { return $null }
            return ($respBody | ConvertFrom-Json)
        }
        finally {
            $resp.Dispose()
        }
    }
    finally {
        $req.Dispose()
    }
}

function Invoke-PlayUploadPipeline {
    param(
        [Parameter(Mandatory)][psobject]$ServiceAccount,
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$AabPath,
        [Parameter(Mandatory)][long]$ExpectedVersionCode,
        [Parameter(Mandatory)][string]$ReleaseName,
        [Parameter(Mandatory)][string]$Track,
        [Parameter(Mandatory)][string]$Status,
        [double]$UserFraction = 0.0,
        [string]$NotesKo = '',
        [string]$NotesEn = '',
        [switch]$ChangesNotSentForReview,
        [string]$TokenUri = $script:GoogleTokenUri,
        [string]$ApiBaseUri = $script:GooglePlayApiBaseUri,
        [switch]$AllowNonGoogleEndpointsForTest
    )
    if (-not $AllowNonGoogleEndpointsForTest) {
        if ($TokenUri -ne $script:GoogleTokenUri) {
            throw "허용되지 않는 token_uri 입니다: $TokenUri ($($script:GoogleTokenUri) 만 허용)"
        }
        if ($ApiBaseUri -ne $script:GooglePlayApiBaseUri) {
            throw "허용되지 않는 API 호스트입니다: $ApiBaseUri ($($script:GooglePlayApiBaseUri) 만 허용)"
        }
    }

    $normalizedBase = $ApiBaseUri.TrimEnd('/')
    $escapedPkg = [Uri]::EscapeDataString($PackageName)
    $escapedTrack = [Uri]::EscapeDataString($Track)
    $trackPayloadJson = New-PlayTrackReleasePayload -Track $Track -VersionCode $ExpectedVersionCode `
        -ReleaseName $ReleaseName -Status $Status -UserFraction $UserFraction -NotesKo $NotesKo -NotesEn $NotesEn

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $http = [System.Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromMinutes(5)

    $editId = $null
    $accessToken = ''
    try {
        # 1. 서비스 계정 JWT → OAuth2 Access Token 교환
        $jwt = New-PlayServiceAccountJwt -ServiceAccount $ServiceAccount -TokenUri $TokenUri
        $tokenFields = [System.Collections.Generic.Dictionary[string, string]]::new()
        $tokenFields.Add('grant_type', 'urn:ietf:params:oauth:grant-type:jwt-bearer')
        $tokenFields.Add('assertion', $jwt)
        $tokenForm = [System.Net.Http.FormUrlEncodedContent]::new($tokenFields)
        try {
            $tokenResp = Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Post) `
                -Uri $TokenUri -Content $tokenForm -PackageName $PackageName
        }
        finally {
            $tokenForm.Dispose()
        }
        if ($null -eq $tokenResp -or [string]::IsNullOrWhiteSpace([string]$tokenResp.access_token)) {
            throw 'OAuth2 토큰 응답에 access_token 이 없습니다.'
        }
        $accessToken = [string]$tokenResp.access_token

        # 2. Edits 세션 생성
        $createContent = [System.Net.Http.StringContent]::new('{}', [Text.Encoding]::UTF8, 'application/json')
        try {
            $editResp = Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Post) `
                -Uri "$normalizedBase/androidpublisher/v3/applications/$escapedPkg/edits" `
                -AccessToken $accessToken -Content $createContent -PackageName $PackageName
        }
        finally {
            $createContent.Dispose()
        }
        if ($null -eq $editResp -or [string]::IsNullOrWhiteSpace([string]$editResp.id)) {
            throw 'Play API edits.insert 응답에 edit id 가 없습니다.'
        }
        $editId = [Uri]::EscapeDataString([string]$editResp.id)

        try {
            # 3. AAB 업로드 + 서버가 파싱한 versionCode 일치 확인
            $aabBytes = [System.IO.File]::ReadAllBytes($AabPath)
            $bundleContent = [System.Net.Http.ByteArrayContent]::new($aabBytes)
            $bundleContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
            try {
                $bundleResp = Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Post) `
                    -Uri "$normalizedBase/upload/androidpublisher/v3/applications/$escapedPkg/edits/$editId/bundles?uploadType=media" `
                    -AccessToken $accessToken -Content $bundleContent -PackageName $PackageName
            }
            finally {
                $bundleContent.Dispose()
            }
            $uploadedCode = [long]$bundleResp.versionCode
            if ($uploadedCode -ne $ExpectedVersionCode) {
                throw "업로드된 AAB 의 versionCode($uploadedCode)가 태그 기준 기대값($ExpectedVersionCode)과 다릅니다."
            }

            # 4. 트랙 할당 + 출시 노트 반영
            $trackContent = [System.Net.Http.StringContent]::new($trackPayloadJson, [Text.Encoding]::UTF8, 'application/json')
            try {
                [void](Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Put) `
                        -Uri "$normalizedBase/androidpublisher/v3/applications/$escapedPkg/edits/$editId/tracks/$escapedTrack" `
                        -AccessToken $accessToken -Content $trackContent -PackageName $PackageName)
            }
            finally {
                $trackContent.Dispose()
            }

            # 5. Edit 커밋
            $commitUri = "$normalizedBase/androidpublisher/v3/applications/$escapedPkg/edits/$editId`:commit"
            if ($ChangesNotSentForReview) {
                $commitUri += '?changesNotSentForReview=true'
            }
            $commitContent = [System.Net.Http.StringContent]::new('', [Text.Encoding]::UTF8, 'application/json')
            try {
                $commitResp = Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Post) `
                    -Uri $commitUri -AccessToken $accessToken -Content $commitContent -PackageName $PackageName
            }
            finally {
                $commitContent.Dispose()
            }

            return [pscustomobject]@{
                PackageName = $PackageName
                EditId      = [string]$editResp.id
                CommittedId = if ($commitResp -and $commitResp.PSObject.Properties['id']) { [string]$commitResp.id } else { [string]$editResp.id }
                VersionCode = $uploadedCode
                Track       = $Track
                Status      = $Status
                ReleaseName = $ReleaseName
            }
        }
        catch {
            if ($editId) {
                try {
                    [void](Invoke-PlayHttpRequest -HttpClient $http -Method ([System.Net.Http.HttpMethod]::Delete) `
                            -Uri "$normalizedBase/androidpublisher/v3/applications/$escapedPkg/edits/$editId" `
                            -AccessToken $accessToken -PackageName $PackageName)
                }
                catch { }
            }
            throw
        }
    }
    finally {
        $accessToken = ''
        $http.Dispose()
    }
}

function Resolve-PlayServiceAccount {
    param(
        [string]$ExplicitPath = '',
        [string]$RepoRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "서비스 계정 JSON 파일을 찾을 수 없습니다: $ExplicitPath"
        }
        $raw = [System.IO.File]::ReadAllText($ExplicitPath, [System.Text.Encoding]::UTF8)
        return [pscustomobject]@{
            Source = $ExplicitPath
            Data   = ($raw | ConvertFrom-Json)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CUT_PLAY_SERVICE_ACCOUNT_JSON)) {
        return [pscustomobject]@{
            Source = 'env:CUT_PLAY_SERVICE_ACCOUNT_JSON'
            Data   = ($env:CUT_PLAY_SERVICE_ACCOUNT_JSON | ConvertFrom-Json)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CUT_PLAY_SERVICE_ACCOUNT_FILE)) {
        if (-not (Test-Path -LiteralPath $env:CUT_PLAY_SERVICE_ACCOUNT_FILE -PathType Leaf)) {
            throw "CUT_PLAY_SERVICE_ACCOUNT_FILE 경로에 파일이 없습니다: $($env:CUT_PLAY_SERVICE_ACCOUNT_FILE)"
        }
        $raw = [System.IO.File]::ReadAllText($env:CUT_PLAY_SERVICE_ACCOUNT_FILE, [System.Text.Encoding]::UTF8)
        return [pscustomobject]@{
            Source = $env:CUT_PLAY_SERVICE_ACCOUNT_FILE
            Data   = ($raw | ConvertFrom-Json)
        }
    }

    $commonGitDir = (git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commonGitDir)) {
        throw '메인 체크아웃 .keystore 폴더를 찾기 위해 git common-dir 를 확인하지 못했습니다.'
    }
    $mainRoot = Split-Path $commonGitDir -Parent
    $defaultKeyPath = Join-Path (Join-Path $mainRoot '.keystore') 'play-service-account.json'
    if (Test-Path -LiteralPath $defaultKeyPath -PathType Leaf) {
        $raw = [System.IO.File]::ReadAllText($defaultKeyPath, [System.Text.Encoding]::UTF8)
        return [pscustomobject]@{
            Source = $defaultKeyPath
            Data   = ($raw | ConvertFrom-Json)
        }
    }
    return $null
}

if ($MyInvocation.InvocationName -ne '.') {
    $previousOutputEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    try {
        if ([string]::IsNullOrWhiteSpace($Tag)) {
            throw '태그(-Tag, 예: v1.73.0)를 지정해야 합니다.'
        }

        $tagInfo = ConvertFrom-PlayTagVersion -Tag $Tag
        $repoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
            throw 'git 저장소 루트를 찾지 못했습니다.'
        }

        # 1. 태그가 origin/master 에 포함됐는지 확인(리뷰·머지된 코드만 배포)
        git -C $repoRoot fetch -q origin master --tags
        if ($LASTEXITCODE -ne 0) {
            throw 'origin master 와 태그를 동기화하지 못했습니다.'
        }
        $tagCommit = (git -C $repoRoot rev-parse "$Tag^{commit}").Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit)) {
            throw "태그 커밋을 찾지 못했습니다: $Tag"
        }
        git -C $repoRoot merge-base --is-ancestor $tagCommit origin/master
        if ($LASTEXITCODE -ne 0) {
            throw "$Tag($tagCommit) 가 origin/master 에 없습니다. 업로드하지 않습니다."
        }

        # 2. mobile/ 변경 가드 — PC 전용 패치 태그를 실수로 Play 에 올리지 않도록 막는다.
        $mobileCheck = Test-MobileChangedSincePreviousTag -RepoRoot $repoRoot -Tag $Tag
        if (-not $mobileCheck.Changed -and -not $Force) {
            $hintTag = if ($mobileCheck.LastMobileTag) { $mobileCheck.LastMobileTag } else { $mobileCheck.PreviousTag }
            throw "$Tag 는 직전 태그($($mobileCheck.PreviousTag)) 이후 mobile/ 변경이 없습니다(최근 mobile 변경 태그: $hintTag). 그래도 올리려면 -Force 를 지정하세요."
        }

        # 3. 릴리스 노트 준비 + payload 선검증
        $notes = Get-PlayChangelogNotes -ChangelogPath (Join-Path $repoRoot 'CHANGELOG.md') -VersionText $tagInfo.VersionText
        $finalKo = if ([string]::IsNullOrWhiteSpace($ReleaseNotesKo)) { $notes.Ko } else { Format-PlayReleaseNoteText -RawText $ReleaseNotesKo -FallbackText $notes.Ko }
        $finalEn = if ([string]::IsNullOrWhiteSpace($ReleaseNotesEn)) { $notes.En } else { Format-PlayReleaseNoteText -RawText $ReleaseNotesEn -FallbackText $notes.En }
        [void](New-PlayTrackReleasePayload -Track $Track -VersionCode $tagInfo.VersionCode `
                -ReleaseName $tagInfo.VersionText -Status $Status -UserFraction $UserFraction -NotesKo $finalKo -NotesEn $finalEn)

        # 4. AAB 준비(지정 파일 또는 release.yml 의 play-bundle 아티팩트) + 서명 인증서 검증
        $workDir = $null
        try {
            $resolvedAab = $AabPath
            $artifactSourceDesc = $AabPath
            if ([string]::IsNullOrWhiteSpace($resolvedAab)) {
                $runsRaw = gh api "repos/$($script:DefaultRepo)/actions/workflows/release.yml/runs?head_sha=$tagCommit&status=completed&per_page=20"
                if ($LASTEXITCODE -ne 0) {
                    throw "release.yml 워크플로 실행 이력을 조회하지 못했습니다($Tag)."
                }
                $runs = ($runsRaw | ConvertFrom-Json).workflow_runs
                $matchRun = $runs |
                    Where-Object { $_.head_sha -eq $tagCommit -and $_.conclusion -eq 'success' } |
                    Sort-Object @{ Expression = { $_.head_branch -eq $Tag }; Descending = $true }, @{ Expression = 'created_at'; Descending = $true } |
                    Select-Object -First 1
                if (-not $matchRun) {
                    throw "$Tag($tagCommit) 에 대해 성공한 release.yml 실행을 찾지 못했습니다."
                }

                $workDir = Join-Path ([IO.Path]::GetTempPath()) "cut-play-pub-$([Guid]::NewGuid().ToString('N'))"
                [void](New-Item -ItemType Directory -Path $workDir)
                gh run download $($matchRun.id) -R $script:DefaultRepo -n play-bundle -D $workDir
                if ($LASTEXITCODE -ne 0) {
                    throw "release.yml 실행 #$($matchRun.id) 에서 play-bundle 아티팩트를 내려받지 못했습니다(만료됐거나 CUT_PLAY_* 시크릿 없이 실행됐을 수 있습니다)."
                }
                $preferred = Join-Path $workDir 'ClaudeUsageTray-Mobile-play.aab'
                if (Test-Path -LiteralPath $preferred -PathType Leaf) {
                    $resolvedAab = $preferred
                }
                else {
                    $aabs = @(Get-ChildItem -LiteralPath $workDir -Recurse -Filter '*.aab' -File)
                    if ($aabs.Count -ne 1) {
                        throw "play-bundle 아티팩트에서 단일 .aab 파일을 찾지 못했습니다(발견: $($aabs.Count)개)."
                    }
                    $resolvedAab = $aabs[0].FullName
                }
                $artifactSourceDesc = "release.yml run #$($matchRun.id) (play-bundle)"
            }

            $aabCheck = Test-PlayAabArchive -AabPath $resolvedAab

            # 5. 서비스 계정 로드
            $saResult = Resolve-PlayServiceAccount -ExplicitPath $ServiceAccountPath -RepoRoot $repoRoot
            if ($null -eq $saResult) {
                if (-not $DryRun) {
                    throw "Google Play 서비스 계정 JSON 키를 찾을 수 없습니다. .keystore/play-service-account.json 또는 CUT_PLAY_SERVICE_ACCOUNT_JSON / CUT_PLAY_SERVICE_ACCOUNT_FILE 을 설정하세요(안내: docs/MOBILE_UPDATES.md)."
                }
                Write-Warning '서비스 계정 JSON 키가 없어 DryRun 에서 JWT 서명 검증만 건너뜁니다(실제 업로드 시 필수).'
                $jwtPreviewOk = $false
                $saEmail = '(미설정)'
                $tokenUri = $script:GoogleTokenUri
            }
            else {
                $tokenUriProp = $saResult.Data.PSObject.Properties['token_uri']
                $tokenUri = if ($tokenUriProp -and -not [string]::IsNullOrWhiteSpace([string]$tokenUriProp.Value)) {
                    [string]$tokenUriProp.Value
                } else {
                    $script:GoogleTokenUri
                }
                if ($tokenUri -ne $script:GoogleTokenUri) {
                    throw "서비스 계정 JSON 의 token_uri 가 Google OAuth2 엔드포인트($($script:GoogleTokenUri))와 다릅니다: $tokenUri"
                }
                [void](New-PlayServiceAccountJwt -ServiceAccount $saResult.Data -TokenUri $tokenUri)
                $jwtPreviewOk = $true
                $saEmail = [string]$saResult.Data.client_email
            }

            if ($DryRun) {
                $koPreview = ($finalKo -replace "`r?`n", ' / ')
                $enPreview = ($finalEn -replace "`r?`n", ' / ')
                Write-Host "[DryRun 검증 완료] $Tag (versionCode $($tagInfo.VersionCode), 커밋 $tagCommit)"
                Write-Host "  패키지: $($script:ExpectedPackageName) | 트랙: $Track | 상태: $Status"
                Write-Host "  AAB 출처: $artifactSourceDesc ($([math]::Round($aabCheck.SizeBytes / 1MB, 2)) MB)"
                Write-Host "  업로드 인증서 SHA-256: $($aabCheck.CertSha256) [일치]"
                Write-Host "  mobile/ 변경: $($mobileCheck.Changed) (기준 $($mobileCheck.PreviousTag), Force=$Force)"
                Write-Host "  서비스 계정: $saEmail (JWT 서명 확인: $jwtPreviewOk)"
                Write-Host "  노트 ko-KR ($($finalKo.Length)/500자): $koPreview"
                Write-Host "  노트 en-US ($($finalEn.Length)/500자): $enPreview"
                return
            }

            $res = Invoke-PlayUploadPipeline -ServiceAccount $saResult.Data `
                -PackageName $script:ExpectedPackageName `
                -AabPath $aabCheck.AabPath `
                -ExpectedVersionCode $tagInfo.VersionCode `
                -ReleaseName $tagInfo.VersionText `
                -Track $Track `
                -Status $Status `
                -UserFraction $UserFraction `
                -NotesKo $finalKo `
                -NotesEn $finalEn `
                -ChangesNotSentForReview:$ChangesNotSentForReview `
                -TokenUri $tokenUri `
                -ApiBaseUri $script:GooglePlayApiBaseUri

            Write-Host "Play Console 업로드 완료: $Tag (versionCode $($res.VersionCode), 트랙 $($res.Track), 상태 $($res.Status), edit $($res.CommittedId))"
        }
        finally {
            if ($workDir) {
                Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    finally {
        [Console]::OutputEncoding = $previousOutputEncoding
    }
}
