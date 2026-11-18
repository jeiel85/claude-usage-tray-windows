#requires -Version 7.0
<#
.SYNOPSIS
    GitHub 릴리스의 ClaudeUsageTray.exe 에 자동 업데이트 서명(ClaudeUsageTray.exe.sig)을 붙인다(#290, docs/DECISIONS.md D-1).

.DESCRIPTION
    태그 push 로 CI 가 릴리스를 만든 뒤, 개인키가 있는 배포 PC 에서 실행한다. 서명이 붙기 전까지 v1.67.3 이상의 앱은 그 릴리스를
    업데이트로 보지 않는다. 서명하기 전에 확인한다:
      1. exe·SHA256.txt 를 올린 것이 CI(github-actions[bot])이다 — 손으로 바꿔 끼운 자산에 서명하지 않는다.
      2. 태그 커밋이 origin/master 에 들어 있다 — 리뷰·머지를 거친 코드다.
      3. 받은 exe 의 SHA-256 이 CI 가 만든 SHA256.txt 와 같다.
      4. 키의 공개키가 그 태그의 앱에 내장된 공개키(UpdateSignature.cs)와 같다.
    서명 메시지는 UpdateSignature.Message 와 같아야 한다: "claude-usage-tray-update/v1\n<버전>\n<sha256 소문자>\n".

.PARAMETER Tag
    서명할 릴리스 태그(예: v1.67.3).

.PARAMETER KeyDir
    update-signing.pem·update-signing.properties 가 있는 폴더. 기본은 메인 체크아웃의 .keystore(워크트리에서 실행해도 찾는다).
#>
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$KeyDir
)

$ErrorActionPreference = 'Stop'
$repo = 'jeiel85/claude-usage-tray-windows'
$assetName = 'ClaudeUsageTray.exe.sig'

if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "태그는 v1.2.3 형식이어야 합니다: $Tag" }
$version = [Version]$Matches[1]

if (-not $KeyDir) {
    $common = git -C $PSScriptRoot rev-parse --path-format=absolute --git-common-dir
    $KeyDir = Join-Path (Split-Path $common -Parent) '.keystore'
}
$pemPath = Join-Path $KeyDir 'update-signing.pem'
$propsPath = Join-Path $KeyDir 'update-signing.properties'
foreach ($p in $pemPath, $propsPath) {
    if (-not (Test-Path -LiteralPath $p)) { throw "서명 키가 없습니다: $p (백업: G:\내 드라이브\모바일 앱 개발\Claude Usage Tray\릴리즈 키스토어)" }
}

# 1. 릴리스와 자산을 올린 쪽
$release = gh api "repos/$repo/releases/tags/$Tag" | ConvertFrom-Json
if ($release.assets | Where-Object name -eq $assetName) { throw "$Tag 에는 이미 서명이 있습니다." }
$exeAsset = $release.assets | Where-Object name -eq 'ClaudeUsageTray.exe'
$shaAsset = $release.assets | Where-Object name -eq 'SHA256.txt'
if (-not $exeAsset -or -not $shaAsset) { throw "$Tag 에 ClaudeUsageTray.exe 또는 SHA256.txt 가 없습니다." }
foreach ($a in $exeAsset, $shaAsset) {
    if ($a.uploader.login -ne 'github-actions[bot]') { throw "$($a.name) 을 올린 것이 CI 가 아닙니다($($a.uploader.login)). 서명하지 않습니다." }
}

# 2. 태그 커밋이 master 에 있는가
git -C $PSScriptRoot fetch -q origin master --tags
$tagCommit = (git -C $PSScriptRoot rev-parse "$Tag^{commit}").Trim()
git -C $PSScriptRoot merge-base --is-ancestor $tagCommit origin/master
if ($LASTEXITCODE -ne 0) { throw "$Tag($tagCommit) 가 origin/master 에 없습니다. 서명하지 않습니다." }

$work = Join-Path ([IO.Path]::GetTempPath()) "cut-sign-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
$key = $null
try {
    # 3. 해시
    gh release download $Tag -R $repo -p 'ClaudeUsageTray.exe' -p 'SHA256.txt' -D $work
    if ($LASTEXITCODE -ne 0) { throw "자산을 내려받지 못했습니다." }
    $sha = (Get-FileHash -LiteralPath (Join-Path $work 'ClaudeUsageTray.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    $ciSha = (Get-Content -LiteralPath (Join-Path $work 'SHA256.txt') -Raw).Trim().Split(' ')[0].ToLowerInvariant()
    if ($sha -ne $ciSha) { throw "exe 해시($sha)가 SHA256.txt($ciSha)와 다릅니다. 서명하지 않습니다." }

    # 4. 키 ↔ 앱에 내장된 공개키
    $password = ((Get-Content -LiteralPath $propsPath) | Where-Object { $_ -match '^password=' } | Select-Object -First 1) -replace '^password=', ''
    if (-not $password) { throw "update-signing.properties 에 password= 줄이 없습니다." }
    $key = [System.Security.Cryptography.ECDsa]::Create()
    $key.ImportFromEncryptedPem((Get-Content -LiteralPath $pemPath -Raw), $password)
    Remove-Variable password
    # 그 태그의 앱이 내장한 공개키. 서명 도입 전 태그면 지금 master 의 것(그 릴리스를 업데이트로 받을 앱이 가진 키)과 맞춘다.
    $source = git -C $PSScriptRoot show "${tagCommit}:ClaudeUsageTray/Services/UpdateSignature.cs" 2>$null
    if ($LASTEXITCODE -ne 0) { $source = git -C $PSScriptRoot show 'origin/master:ClaudeUsageTray/Services/UpdateSignature.cs' }
    if ($LASTEXITCODE -ne 0) { throw "UpdateSignature.cs 를 찾지 못했습니다." }
    $embedded = (($source -join "`n") -replace '(?s).*-----BEGIN PUBLIC KEY-----(.*?)-----END PUBLIC KEY-----.*', '$1') -replace '\s', ''
    $mine = ($key.ExportSubjectPublicKeyInfoPem() -replace '-----(BEGIN|END) PUBLIC KEY-----', '') -replace '\s', ''
    if ($embedded -ne $mine) { throw "이 키는 앱에 내장된 공개키(UpdateSignature.PublicKeyPem)와 짝이 아닙니다." }

    # 서명 — UpdateSignature.Message 와 같은 바이트
    $message = [Text.Encoding]::UTF8.GetBytes("claude-usage-tray-update/v1`n$($version.ToString(3))`n$sha`n")
    $signature = $key.SignData($message, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    $pub = [System.Security.Cryptography.ECDsa]::Create()
    $pub.ImportFromPem($key.ExportSubjectPublicKeyInfoPem())
    if (-not $pub.VerifyData($message, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) { throw "서명 자체 검증 실패" }

    $sigPath = Join-Path $work $assetName
    Set-Content -LiteralPath $sigPath -Value ([Convert]::ToBase64String($signature)) -Encoding ascii -NoNewline
    gh release upload $Tag $sigPath -R $repo
    if ($LASTEXITCODE -ne 0) { throw "서명을 올리지 못했습니다." }
    Write-Host "서명함: $Tag (v$($version.ToString(3)), sha256 $sha, 커밋 $tagCommit)"
}
finally {
    if ($key) { $key.Dispose() }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
