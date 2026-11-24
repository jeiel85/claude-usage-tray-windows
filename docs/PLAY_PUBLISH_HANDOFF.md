# Google Play 자동 업로드 핸드오프 가이드

작성일: 2026-11-24 · 관련 이슈: `#348` (PR `#349`, 커밋 `33b452a`) · 상세 구조 문서: [MOBILE_UPDATES.md](./MOBILE_UPDATES.md)

---

## 1. 현재 완료된 상태

| 항목 | 상태 | 비고 |
|---|---|---|
| 모바일 Antigravity 개편 (`v1.73.0`, `#345`) | 완료 | `release.yml` run `45332884347` — 서명 AAB(`versionCode 17300`) 생성됨 |
| PC 팝업 레이아웃 정렬 (`v1.73.1`, `#346`) | 완료 | `release.yml` run `45335656730` — 서명 AAB(`versionCode 17301`) 생성됨(`mobile/` 변경 없음) |
| 공용 업로드 스크립트 ([`tools/publish-play.ps1`](../tools/publish-play.ps1)) | 완료 | 서드파티 의존성 0개, Google Play Developer API v3 직접 호출, 실패 시 Edit 자동 삭제 |
| 수동 CI 워크플로 ([`.github/workflows/play-publish.yml`](../.github/workflows/play-publish.yml)) | 완료 | `workflow_dispatch` (`Publish to Google Play`), Action 커밋 SHA 고정, `env:` 입력 전달 |
| PR 감시 경로 ([`.github/workflows/pr-ci.yml`](../.github/workflows/pr-ci.yml)) | 완료 | `tools/**` 변경 시 PR CI 자동 실행 |
| 통합 테스트 (`PublishPlayScriptTests.cs`) | 완료 | `pwsh` 실프로세스 + 루프백 목 서버 7건 포함 전체 1,487 / 1,487 통과 |
| 실물 `-DryRun` 검증 | 완료 | 로컬(`v1.73.1` 가드 차단 · `-Force` 통과 · `v1.73.0` 통과) + Ubuntu CI run `45340382388` 통과 |
| 서비스 계정 JSON 키 발급 및 첫 실제 업로드 | 완료 (2026-11-24) | 아래 "진행 결과" 참고 — `17300 (1.73.0)` Alpha 검토 중 |

### 진행 결과 (2026-11-24)

- **GCP 프로젝트**: `ai-usage-play-publisher`(Play Console 계정 `pedaiah85@gmail.com` 소유, 조직 없음) — `Google Play Android Developer API` 사용 설정.
- **서비스 계정**: `play-publisher@ai-usage-play-publisher.iam.gserviceaccount.com` — GCP IAM 역할 없음(Play 권한은 Play Console 에서만 줌), JSON 키 1개.
- **Play Console 권한**: 개발자 계정 EL Studio → 사용자 및 권한에 초대, `AI 사용량` 앱 한정으로 *앱을 테스트 트랙으로 출시* + *프로덕션으로 출시, 기기 제외, Play 앱 서명 사용*(이 권한을 켜면 *정책 선언 관리*·*딥 링크 관리*가 함께 강제됨) + 기본 읽기 권한. 계정 권한·관리자 권한은 주지 않음. 초대 직후 `활성`, 반영 지연 403 없이 바로 동작.
- **키 배치**: 메인 체크아웃 `.keystore/play-service-account.json`(git 제외) · 백업 `G:\내 드라이브\모바일 앱 개발\Claude Usage Tray\릴리즈 키스토어\`(cmp 일치) · Actions Secret `CUT_PLAY_SERVICE_ACCOUNT_JSON`. 내려받은 원본은 다운로드 폴더에서 옮겨 남기지 않음.
- **검증**: 로컬 `-DryRun`(JWT 서명 True, 업로드 인증서 일치) · CI `dry_run=true` run `45343406206` 성공(Secret 으로 JWT 서명 True).
- **첫 실제 업로드**: `v1.73.0`(`versionCode 17300`) → `alpha`, `completed` → edit 커밋 후 Play Console 에서 "1.73.0 검토 중" 확인(국가 178개·테스터 목록은 트랙 설정 그대로). 직전 Alpha 는 `17212 (1.72.12)` 였으므로 1.72.13·1.72.14 휴대폰 변경까지 담은 출시 노트를 `-ReleaseNotesKo`/`-ReleaseNotesEn` 으로 따로 넣음.

### 운영하며 알게 된 점

- **CHANGELOG 자동 노트는 PC 문구 위주이고 500자에서 잘린다.** 이 저장소의 CHANGELOG 는 PC·휴대폰 변경을 한 섹션에 쓰므로, 휴대폰 테스터용으로는 `-ReleaseNotesKo`/`-ReleaseNotesEn` 으로 휴대폰 변경만 짧게 넣는다. 또 Alpha 에 마지막으로 올린 버전 이후의 모든 휴대폰 변경을 담는다(스크립트는 그 태그 섹션 하나만 추출).
- **여러 줄 노트는 `pwsh -File` 로 넘기면 인자가 끊긴다**(`'ReleaseNotesKo'에 대한 인수가 없습니다`). PowerShell 7 세션 안에서 `& ./tools/publish-play.ps1 v1.73.0 -ReleaseNotesKo $ko -ReleaseNotesEn $en` 처럼 직접 호출한다.
- **출시 이름이 수동 업로드와 다르다**: 스크립트는 `1.73.0`, 그동안 수동으로는 `17300 (1.73.0)` 꼴을 썼다. 동작에는 영향 없음.
- 서비스 계정 키는 만료 없는 장기 자격 증명이다. Google 은 CI 에 워크로드 아이덴티티 제휴를 권장하므로, CI 경로를 그쪽으로 옮기면 Secret 의 JSON 키를 없앨 수 있다(로컬 스크립트는 JSON 키 유지).

---

## 2. 서비스 계정 키 발급 · 권한 부여 (2026-11-24 완료 — 키를 다시 만들거나 교체할 때의 절차)

Play Console 자동 업로드를 실제로 실행하려면 **Google Cloud 서비스 계정 JSON 키** 1회 등록이 필요합니다. 지금 쓰는 프로젝트·계정 이름은 1절 "진행 결과"에 있습니다.

1. **Google Cloud Console — API 사용 설정 및 키 발급**
   - [Google Cloud Console](https://console.cloud.google.com/)에서 프로젝트를 선택(또는 생성)합니다.
   - **API 및 서비스 → 라이브러리**에서 **`Google Play Android Developer API`**(`androidpublisher.googleapis.com`)를 **사용**으로 켭니다.
   - **IAM 및 관리자 → 서비스 계정**에서 새 서비스 계정(예: `play-publisher`)을 만들고 **키 → 키 추가 → 새 키 만들기 → JSON**으로 키 파일을 내려받습니다.
2. **Google Play Console — 앱 출시 권한 부여**
   - [Google Play Console](https://play.google.com/console/) → **사용자 및 권한 → 사용자 초대**에 서비스 계정 이메일(`…@….iam.gserviceaccount.com`)을 입력합니다.
   - **앱 권한** 탭에서 **`AI 사용량` (`com.jeiel85.aiusage`)** 앱을 추가하고 **테스트 트랙 출시 · 프로덕션 출시 관리** 권한을 부여한 뒤 저장합니다.
   - *(주의)* 계정 수준 권한만 주거나 방금 권한을 저장한 직후에는 Google 측 반영 지연으로 `403 PERMISSION_DENIED`가 날 수 있습니다.
3. **키 배치** (로컬/CI 중 사용할 환경 또는 둘 다에 등록)
   - **로컬 PC**: `<저장소 루트>/.keystore/play-service-account.json`에 저장 (`.keystore/`는 `.gitignore`로 제외됨). 다른 경로를 쓰려면 `CUT_PLAY_SERVICE_ACCOUNT_FILE` 환경 변수 또는 `-ServiceAccountPath` 사용.
   - **GitHub Actions CI**: 저장소 **Settings → Secrets and variables → Actions**에 Secret **`CUT_PLAY_SERVICE_ACCOUNT_JSON`** 추가(JSON 파일 내용 전체).

> 서비스 계정 JSON·업로드 키·서명 키는 절대 커밋하거나 로그/대화/문서에 출력하지 마세요.

---

## 3. 키 등록 후 검증 및 첫 업로드 실행

### 3-1. DryRun 검증 (JWT 서명까지 확인)

키를 배치한 뒤 먼저 실제 업로드 없이 JWT 서명과 AAB·인증서·노트 추출이 모두 통과하는지 확인합니다.

```powershell
# 로컬 확인 (JWT 서명 확인: True 및 업로드 인증서 SHA-256 일치 확인)
pwsh tools/publish-play.ps1 v1.73.0 -DryRun

# CI 확인
gh workflow run play-publish.yml -f tag=v1.73.0 -f dry_run=true
```

### 3-2. 어떤 태그를 올릴지 선택

- **`v1.73.0` (`versionCode 17300`) 권장**: 직전 태그(`v1.72.14`) 대비 `mobile/` 변경(Antigravity 카드 개편)이 직접 들어간 태그라 `-Force` 없이 통과하고, `CHANGELOG.md`의 모바일 변경 요약이 그대로 출시 노트로 들어갑니다.
- **`v1.73.1` (`versionCode 17301`)**: `v1.73.0`과 모바일 앱 바이너리는 동일하며 PC XAML만 바뀐 태그입니다. 더 높은 `versionCode`로 올리고 싶을 때 `-Force`를 붙여 실행합니다(필요하면 `-ReleaseNotesKo`/`-ReleaseNotesEn`으로 모바일용 문구 재정의 가능).
- *(참고)* 한 번 더 높은 `versionCode`(예: `17301`)를 Play Console에 올리면, 그보다 낮은 `versionCode`(`17300`)는 다시 올릴 수 없습니다.

### 3-3. 실제 업로드 명령

**로컬 실행:**

```powershell
# 기본값: 비공개 테스트(alpha) 트랙, 상태 completed, CHANGELOG(ko-KR/en-US ≤500자)
pwsh tools/publish-play.ps1 v1.73.0

# v1.73.1 로 올릴 때 (mobile/ 변경 없음 가드 우회)
pwsh tools/publish-play.ps1 v1.73.1 -Force
```

**GitHub Actions 수동 실행 (Actions 탭 → `Publish to Google Play` → Run workflow):**

```powershell
gh workflow run play-publish.yml -f tag=v1.73.0 -f track=alpha -f status=completed
```

**상황별 추가 옵션:**

| 상황 | 로컬 플래그 | CI 입력 |
|---|---|---|
| Play Console 앱이 아직 첫 심사 제출 전(초안 앱)일 때 | `-Status draft` | `status=draft` |
| 기존 검토 대기 변경과 충돌할 때 | `-ChangesNotSentForReview` | `changes_not_sent_for_review=true` |
| 비공개 테스트를 기본 `alpha` 외 다른 트랙 이름으로 만들었을 때 | `-Track "<트랙명>"` | `custom_track="<트랙명>"` |
| 프로덕션 단계적 배포(예: 20%) | `-Track production -Status inProgress -UserFraction 0.2` | `track=production`, `status=inProgress`, `user_fraction=0.2` |

---

## 4. 업로드 후 확인 및 운영 규칙

1. **업로드 확인**: [Google Play Console](https://play.google.com/console/) → **`AI 사용량` (`com.jeiel85.aiusage`)** → **테스트 및 출시 → 비공개 테스트**(또는 지정 트랙)에서 해당 `versionCode`와 한국어/영어 출시 노트가 반영됐는지 확인합니다(`-Status draft`로 올린 경우 콘솔에서 검토 제출).
2. **키 종류 구분 (혼동 금지)**:
   - GitHub APK 서명 키: `CUT_RELEASE_*` (`cut-release.jks`) — GitHub Releases `ClaudeUsageTray-Mobile.apk` 전용
   - Play AAB 업로드 키: `CUT_PLAY_*` (`cut-play-upload.jks`) — `release.yml`이 `play-bundle` 서명에 사용 (`4C:65:B6:47:…:22`)
   - Play API 서비스 계정 키: `CUT_PLAY_SERVICE_ACCOUNT_JSON` / `.keystore/play-service-account.json` — `publish-play.ps1` API 인증 전용
   - Windows 업데이트 서명 키: `.keystore/update-signing.pem` (`tools/sign-release.ps1`) — `ClaudeUsageTray.exe.sig` 서명 전용
3. **릴리스 운영 원칙**: 이 저장소는 Windows PC 전용 패치 태그도 자주 배포되므로, Play 업로드는 `release.yml`에 묶어 자동 실행하지 않고 `mobile/` 변경이 있는 릴리스에서만 `tools/publish-play.ps1` 또는 `Publish to Google Play` 워크플로를 실행합니다.
