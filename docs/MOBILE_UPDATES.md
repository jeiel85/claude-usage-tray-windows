# 휴대폰 앱 자동 업데이트 (#228)

GitHub Releases의 APK로 설치한 휴대폰 앱은 스스로 새 버전을 확인하고 설치까지 이어 줍니다.
이 기능은 **GitHub 빌드에만** 있습니다. Play 빌드는 Google Play가 업데이트하고, 앱은 새 버전이 있다고 알려 Play 스토어로 보내기만 합니다(아래 "Play 빌드의 새 버전 알림" 참고).
구조는 [Homebase](https://github.com/jeiel85/homebase)의 `update/` 패키지(Markleaf에서 온 구조, Homebase DECISIONS M-4·M-22)를 그대로 옮겼습니다.

## 흐름

1. **확인**: 앱이 `https://github.com/jeiel85/claude-usage-tray-windows/releases/latest/download/update.json`을 **하루 한 번** GET 합니다.
   - GitHub REST API(`api.github.com`)는 쓰지 않습니다. 비인증 한도(시간당 60건)가 IP에 붙어 통신사 NAT 뒤에서는 남의 요청에 막히기 때문입니다.
   - 요청에는 URL과 표준 헤더만 실립니다(쿼리스트링·쿠키·기기 식별자 없음).
   - 온전히 읽힌 응답만 캐시하고 그때만 확인 시각을 기록합니다. 실패는 조용히 넘어가고 다음에 다시 확인합니다.
2. **알림**: 설치된 versionCode보다 큰(정수 비교) 버전이면 계정 탭 위에 배너가 뜨고, Android 알림을 **버전당 한 번** 띄웁니다.
   - 배경 확인은 JobScheduler 주기 잡(12시간, 네트워크 필요, 재부팅 뒤 유지)이고, 실제 GET은 위의 하루 1회 규칙을 따릅니다.
   - minSdk가 기기보다 높으면 제안하지 않습니다. "이 버전 건너뛰기"를 누른 버전도 제안하지 않습니다("지금 확인"을 누르면 건너뛰기가 풀립니다).
3. **백그라운드 자동 설치**(v1.66.0, #281, 기본 켜짐): 배경 확인이 새 버전을 찾으면 그 자리에서 설치까지 합니다. 포너디(find-my-wife-phone D-7)와 같은 방식입니다.
   - 앱이 화면에 떠 있으면 미룹니다(설치 순간 프로세스가 새 버전으로 교체돼 쓰던 화면이 닫힘). 다음 주기에 다시 봅니다.
   - 받은 APK는 SHA-256에 더해 **같은 패키지·더 높은 versionCode·설치된 앱과 같은 서명 키**인지 확인합니다.
   - `PackageInstaller` 세션으로 설치합니다. Android 12+에서는 `USER_ACTION_NOT_REQUIRED` + `UPDATE_PACKAGES_WITHOUT_USER_ACTION`으로, 앱이 **자기 자신을** 업데이트하는 경우 확인 없이 설치됩니다. 끝나면 "업데이트했습니다" 알림을 한 번 띄웁니다.
   - 시스템이 확인을 요구하면(Android 11 이하, 이 앱에 '알 수 없는 앱 설치'를 아직 허용하지 않은 경우) "설치 준비됨" 알림을 띄우고, 누르면 그 세션의 확인 화면(필요하면 출처 허용 → "업데이트")이 바로 뜹니다. 한 번 이렇게 설치하면 이후 업데이트는 조용히 됩니다.
   - 한 버전에 자동 시도는 한 번입니다. 받기·확인·설치가 실패한 버전은 평소 알림과 아래 수동 설치로 넘어갑니다.
   - 2026-11-18 API 36 에뮬레이터에서 확인: 화면에 있을 때 미룸 / 백그라운드 무확인 설치 + 알림 / 권한 없을 때 알림 → 출처 허용 → 업데이트.
4. **수동 설치**(사용자가 누를 때): 앱 캐시로 내려받고(최대 3회 시도) **SHA-256을 대조**한 뒤, 시스템 설치 앱(`ACTION_INSTALL_PACKAGE` + FileProvider)으로 넘깁니다. 설치가 끝나면 시스템 화면에 "열기"가 있어 바로 새 버전을 엽니다.
   - '알 수 없는 앱 설치' 권한이 없으면 설정 화면으로 안내합니다.
   - 실패하면 "다시 시도" 또는 "브라우저에서 열기".
5. **설정**: 홈 위쪽 톱니바퀴 → 설정 → **앱 업데이트**에서 확인 끄기(기본 켜짐), **백그라운드에서 자동 설치** 끄기(기본 켜짐, 끄면 알림만), 지금 확인, 설치된 버전을 봅니다.

## `update.json`

릴리스 워크플로(`.github/workflows/release.yml`의 `build-apk`)가 방금 만든 APK에서 직접 뽑아 만듭니다.

```json
{
  "versionCode": 15500,
  "versionName": "1.55.0",
  "apkUrl": "https://github.com/jeiel85/claude-usage-tray-windows/releases/download/v1.55.0/ClaudeUsageTray-Mobile.apk",
  "apkSizeBytes": 9873837,
  "sha256": "<APK의 SHA-256, 소문자 hex 64자>",
  "minSdk": 26,
  "releaseNotesUrl": "https://github.com/jeiel85/claude-usage-tray-windows/releases/tag/v1.55.0"
}
```

- versionCode·versionName·minSdk는 `aapt2 dump badging`, 크기·해시는 파일 자체에서 가져옵니다. `versionName`이 태그와 다르면 릴리스가 실패합니다.
- **정식 서명 APK일 때만 만듭니다.** Android는 설치된 앱과 같은 키로 서명된 APK만 업데이트로 받아들입니다. 서명 시크릿이 없을 때 CI가 만드는 디버그 서명 APK는 실행마다 키가 달라, 앱에 제안해도 설치 단계에서 반드시 거부됩니다. 그래서 디버그 서명 릴리스에는 `update.json`이 없고, 앱은 404를 "새 버전 없음"으로 보고 넘어갑니다.

## 정식 서명 키 준비 (한 번만)

PowerShell에서(JDK의 `keytool` 필요 — Android Studio에 포함된 `jbr\bin\keytool.exe`도 됩니다):

```powershell
# 1. 서명 키 만들기. 비밀번호 두 개(키 저장소·키)와 이름 등을 묻습니다. 유효 기간 100년.
keytool -genkeypair -v -keystore cut-release.jks -alias cut -keyalg RSA -keysize 4096 -validity 36500

# 2. GitHub 시크릿에 넣을 base64 를 클립보드로 복사
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$PWD\cut-release.jks")) | Set-Clipboard
```

GitHub 저장소 → Settings → Secrets and variables → Actions → **New repository secret**으로 네 개를 등록합니다.

| 이름 | 값 |
|---|---|
| `CUT_RELEASE_KEYSTORE_BASE64` | 위에서 복사한 base64 |
| `CUT_RELEASE_STORE_PASSWORD` | 키 저장소 비밀번호 |
| `CUT_RELEASE_KEY_ALIAS` | `cut` (위 명령의 `-alias`) |
| `CUT_RELEASE_KEY_PASSWORD` | 키 비밀번호 |

- `cut-release.jks`는 **저장소에 올리지 말고** 안전한 곳에 백업하세요. 잃어버리면 그 뒤 버전은 업데이트로 설치되지 않아 모든 휴대폰에서 지우고 다시 설치해야 합니다.
- 시크릿을 넣은 뒤 첫 릴리스부터 정식 서명 APK와 `update.json`이 붙습니다.
- 지금 디버그 서명 APK(v1.53·v1.54)를 쓰는 휴대폰은 **한 번만** 지우고 정식 서명 APK를 설치하세요(연결한 PC는 다시 추가). 앱도 업데이트 창에서 이렇게 안내합니다. 그다음부터는 앱 안에서 업데이트됩니다.

### 등록 상태 (2026-11-14, #240)

정식 서명 키를 만들어 위 시크릿 네 개를 등록했습니다. v1.58.1부터 정식 서명 APK와 `update.json`이 붙습니다.

- 인증서: `CN=Claude Usage Tray, OU=Mobile, O=jeiel85`, RSA 4096, 유효 기간 100년, 별칭 `cut`
- 인증서 SHA-256: `24:98:EE:9E:2D:1C:E2:28:F6:38:1E:E2:89:A0:71:F1:5C:E4:59:D9:F2:CC:06:FD:9F:E3:3D:07:0A:09:6C:C7`
  (`apksigner verify --print-certs ClaudeUsageTray-Mobile.apk`로 받은 APK가 이 키로 서명됐는지 확인할 수 있습니다)
- 키 파일과 비밀번호는 저장소 밖(관리자 PC의 `mobile/.keystore/`·`mobile/release-signing.properties`, git 제외)과 별도 백업에만 있습니다.

## 패키지명 변경 (v1.60.0, #254)

`applicationId`가 `io.github.jeiel85.claudeusage`에서 `com.jeiel85.aiusage`로 바뀌었습니다(새 패키지명 규칙 `com.jeiel85.<앱이름>`, Play 첫 업로드 전에 확정). Kotlin 패키지와 `namespace`는 그대로입니다. 서명 키는 같습니다.

- Android는 두 이름을 **다른 앱**으로 봅니다. v1.59.2 이하 앱의 자동 업데이트가 v1.60.0 APK를 내려받으면 시스템 설치 앱이 업데이트가 아니라 **새 앱으로 따로** 설치합니다.
- 앱 데이터(연결한 PC, 설정, 알림 기록)는 옮겨지지 않습니다. 새 앱에서 PC를 다시 연결해야 합니다.
- 새 앱은 이전 앱이 남아 있으면 홈에 **이전 앱 삭제** 안내를 띄웁니다(`ui/LegacyAppNotice.kt`, `<queries><package>` 와 `REQUEST_DELETE_PACKAGES`). 두 앱이 모두 연결돼 있으면 알림이 두 번 오기 때문입니다.
- PC에는 이전 휴대폰이 따로 연결된 기기로 남습니다. 휴대폰 연결 창의 '연결된 휴대폰'에서 이전 항목을 **연결 해제**하세요.
- 이전 앱을 지워도 ntfy에는 그 앱의 구독(`up…` 토픽)이 남을 수 있습니다. 앱이 지워질 때 배포자가 알아서 정리해 주지 않기 때문입니다(#242와 같은 원인). ntfy 구독 목록에서 직접 지우면 됩니다.

## 배포 채널: GitHub 빌드와 Play 빌드 (v1.61.0, #257)

휴대폰 앱은 채널마다 따로 빌드합니다(Gradle product flavor `channel`).

| | `github` | `play` |
|---|---|---|
| 배포 | GitHub Releases의 `ClaudeUsageTray-Mobile.apk` (태그 push → CI) | Google Play. 태그 push → CI가 서명된 `ClaudeUsageTray-Mobile-play.aab`를 `play-bundle` 아티팩트로 남기고, [`tools/publish-play.ps1`](../tools/publish-play.ps1) 또는 Actions `Publish to Google Play` 워크플로로 Play Console에 업로드합니다 |
| 앱 안 업데이트 | 있음 (이 문서의 흐름, `update.json`) | 설치는 Play가 함. 앱은 새 버전 알림 → Play 스토어로 이동만(#284, 아래) |
| `REQUEST_INSTALL_PACKAGES` | 있음 | 없음 |
| `RECEIVE_BOOT_COMPLETED` | 있음 (배경 확인 유지) | 있음 (배경 확인 유지, #284) |
| 서명 키 | `cut` 키 (아래 "정식 서명 키") | Play 업로드 키(아래) → Google이 자기가 보관하는 앱 서명 키로 다시 서명(Play App Signing) |

- 업데이트 코드·권한·문구는 `mobile/app/src/github/`에만 있고, 공용 코드는 `update/AppUpdates`만 부릅니다. Play 쪽 `AppUpdates`(`src/play/`)는 새 버전 알림만 합니다(#284). 실행 중에 설치 출처를 보고 끄는 방식은 쓰지 않습니다.
- Play 정책은 Play 밖에서 스스로 업데이트하는 앱을 허용하지 않고, `REQUEST_INSTALL_PACKAGES`는 제한 권한입니다. 그래서 Play 빌드에는 업데이터가 아예 들어가지 않습니다.
- **두 채널은 서로 덮어 설치할 수 없습니다.** 패키지명(`com.jeiel85.aiusage`)과 버전은 같지만 서명 키가 다르기 때문입니다. 채널을 바꾸려면 앱을 지우고 다른 채널로 다시 설치한 뒤 PC를 다시 연결해야 합니다(앱 데이터는 Android Keystore로 암호화돼 옮겨지지 않습니다).
- 빌드 명령: GitHub `.\gradlew assembleGithubRelease`, Play `.\gradlew bundlePlayRelease`. 업로드 키 값이 없으면 Play 번들은 서명되지 않은 채 만들어집니다(업로드 불가). `-Pcut.requirePlaySigning=true`를 주면 그때 빌드가 실패합니다.

### Play 빌드의 새 버전 알림 (v1.67.0, #284)

Play 자동 업데이트를 꺼 둔 사용자도 새 버전을 알 수 있게, Play 빌드는 **Play In-App Updates API**(`com.google.android.play:app-update`, `playImplementation`)로 휴대폰의 Google Play 앱에 "이 사용자가 받을 수 있는 더 높은 버전이 있는가"를 묻습니다. 같은 개발자의 도란(family-gagebu) Play 빌드와 같은 방식입니다.

- 답은 이 사용자에게 열린 트랙 기준입니다(내부·비공개 테스트 참여자는 그 트랙의 새 버전). 앱이 GitHub나 다른 서버로 요청하지 않습니다.
- 새 버전이 있으면 홈 배너, 설정 → 앱 업데이트 카드의 'Google Play' 버튼, 배경 확인 알림(버전당 1회)으로 알립니다. 누르면 Play 스토어 앱의 이 앱 페이지(`market://details`, 없으면 웹 `play.google.com`)로 갑니다. 알림은 앱을 거쳐 스토어를 엽니다(웹 대체 경로를 유지하려고).
- 설치는 Play가 합니다. 앱 안 다운로드·설치 코드와 `REQUEST_INSTALL_PACKAGES`는 여전히 없습니다. Flexible/Immediate 앱 내 업데이트 흐름도 쓰지 않습니다(스토어로 보내기만).
- 확인 시점: 앱이 화면에 올라올 때 마지막 답이 6시간보다 오래됐으면, 그리고 배경 잡(JobScheduler, ID `787076` = `0x0c_0284`, 12시간 주기·네트워크 필요·재부팅 유지). 설정의 '새 버전 알림'(기본 켜짐)은 배경 확인·알림만 끄고, 홈 배너는 그대로 보입니다.
- Play에서 받지 않은 설치본(사이드로드한 디버그 등)은 API가 답하지 않습니다. 배경·배너에서는 조용히 넘어가고, '지금 확인'을 눌렀을 때만 이유를 보여 줍니다.
- 코드: `mobile/app/src/play/java/.../update/` (`StoreUpdates`·`StoreUpdateDecision`·`StoreUpdateJob`·`StoreUpdateNotifier`·`StoreUpdateSurface`·`StoreUpdatePreferences`), 테스트 `src/testPlay/`.
- 사이드로드 빌드로 화면 확인(디버그 빌드만): Play가 버전을 내놓은 것처럼 흉내 냅니다.
  ```
  adb shell am start -S -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.MainActivity --ei cut.fakeStoreUpdate 16700
  adb shell cmd jobscheduler run -f com.jeiel85.aiusage 787076
  ```
  설치된 versionCode보다 높아야 하므로 `-Pcut.version=1.66.0`처럼 버전을 주고 빌드합니다. 실제 Play 응답은 Play에서 설치한 앱(테스트 트랙 포함)에서만 볼 수 있습니다.

### Play 업로드 키 (2026-11-16, v1.61.1)

- `cut` 키와 다른 키입니다(채널마다 다른 키). PKCS12, 별칭 `upload`, RSA 4096, 유효 기간 100년, 저장소·키 비밀번호 동일(무작위 32자).
- 인증서 SHA-256: `4C:65:B6:47:15:49:79:66:11:2C:FF:58:44:50:7A:5E:0C:EB:96:AF:7D:97:1E:E4:61:43:4E:62:A6:56:52:22`
  (`keytool -printcert -jarfile ClaudeUsageTray-Mobile-play.aab`로 확인. Play Console의 "앱 무결성 → 업로드 키 인증서"와 같아야 합니다)
- 키 파일과 비밀번호는 저장소 밖(관리자 PC의 `mobile/.keystore/cut-play-upload.jks`·`mobile/play-signing.properties`, git 제외)과 별도 백업에만 있습니다. CI는 시크릿 `CUT_PLAY_*` 4개를 씁니다.
- Play App Signing에서는 업로드 키를 잃어도 Play Console에서 재설정을 요청할 수 있습니다(앱 서명 키는 Google이 보관). 그래도 백업을 지킵니다.

### Google Play 자동 업로드 (로컬 · CI)

`tools/publish-play.ps1`(로컬·CI 공용 PowerShell 7 스크립트)과 `.github/workflows/play-publish.yml`(수동 `workflow_dispatch`)을 통해 서드파티 Action·외부 도구 없이 Google Play Developer API v3로 `play-bundle` AAB를 업로드할 수 있습니다. PC 전용 패치 태그를 실수로 Play에 제출하지 않도록 **직전 `v*` 태그 이후 `mobile/` 변경이 있는 태그만** 기본 허용합니다(필요 시 `-Force`로 우회).

#### 1. 서비스 계정 JSON 키 발급 (한 번만)

Play API 인증에는 업로드 키(`.jks`)와 별개로 **Google Cloud 서비스 계정 JSON 키**가 필요합니다.

1. [Google Cloud Console](https://console.cloud.google.com/)에서 프로젝트를 선택하거나 새로 만듭니다.
2. **API 및 서비스 → 라이브러리**에서 **`Google Play Android Developer API`**(`androidpublisher.googleapis.com`)를 **사용**으로 설정합니다.
3. **IAM 및 관리자 → 서비스 계정**에서 새 서비스 계정을 만듭니다(예: `play-publisher`).
4. 만든 서비스 계정 → **키 → 키 추가 → 새 키 만들기 → JSON**을 선택해 JSON 파일을 내려받습니다.
5. [Google Play Console](https://play.google.com/console/) → **사용자 및 권한 → 사용자 초대**:
   - 위에서 만든 서비스 계정 이메일(`…@….iam.gserviceaccount.com`)을 입력합니다.
   - **앱 권한** 탭에서 **`AI 사용량` (`com.jeiel85.aiusage`)** 앱을 추가하고 **테스트 트랙 출시 · 프로덕션 출시 관리** 권한을 부여합니다(계정 수준 기본 권한만으로는 거부될 수 있습니다).
   - 초대/권한 저장 후 Google 쪽 반영까지 최대 24시간 걸릴 수 있습니다.

#### 2. 키 보관

| 사용 환경 | 위치 / 시크릿 이름 | 비고 |
|---|---|---|
| **로컬 PC** | 메인 체크아웃 `.keystore/play-service-account.json` | `.gitignore`로 git 제외됨. `CUT_PLAY_SERVICE_ACCOUNT_FILE` 또는 `-ServiceAccountPath`로 경로 지정 가능 |
| **GitHub Actions CI** | Repository Secret `CUT_PLAY_SERVICE_ACCOUNT_JSON` | 내려받은 JSON 파일 내용 전체를 시크릿 값으로 붙여 넣음 |

> 서비스 계정 JSON 키와 개인키는 절대 커밋하거나 로그·대화에 출력하지 마세요.

#### 3. 로컬에서 실행

```powershell
# 1) 실제 업로드 없이 태그·mobile/ 변경·AAB·업로드 키 인증서(SHA-256)·CHANGELOG 노트·JWT 점검
pwsh tools/publish-play.ps1 v1.73.0 -DryRun

# 2) 기본 업로드: 비공개 테스트(alpha) 트랙, 상태 completed, CHANGELOG.md(ko-KR / en-US) 자동 첨부
pwsh tools/publish-play.ps1 v1.73.0

# 3) PC 전용 태그(예: v1.73.1)처럼 직전 태그 이후 mobile/ 변경이 없어도 강제 업로드할 때
pwsh tools/publish-play.ps1 v1.73.1 -Force

# 4) 앱 최초 출시 전(draft app)이거나 트랙·단계 배포를 바꿀 때
pwsh tools/publish-play.ps1 v1.73.0 -Status draft
pwsh tools/publish-play.ps1 v1.73.0 -Track production -Status inProgress -UserFraction 0.2

# 5) 이미 승인/검토 대기 중인 변경 때문에 commit 이 거절될 때
pwsh tools/publish-play.ps1 v1.73.0 -ChangesNotSentForReview
```

- `-AabPath <경로>`를 생략하면 해당 태그 커밋에서 성공한 `release.yml`의 `play-bundle` 아티팩트를 자동으로 내려받아 업로드 키 인증서 SHA-256(`4C:65:B6:47:…:22`) 일치 여부를 검사한 뒤 올립니다.
- 비공개 테스트를 기본 `alpha`가 아닌 **사용자 지정 트랙 이름**으로 만들었다면 `-Track "<트랙명>"`으로 지정하면 됩니다.

#### 4. GitHub Actions CI에서 실행

1. GitHub 저장소 → **Actions** → **`Publish to Google Play`** → **Run workflow**
2. 태그(`v1.73.0`), 트랙(`alpha`/`internal`/`beta`/`production` 또는 `custom_track`), 상태(`completed`/`draft`/`inProgress`)를 고르고 실행합니다(`dry_run` 체크 시 검증만 수행).
3. CLI로 바로 실행할 때:

```powershell
gh workflow run play-publish.yml -f tag=v1.73.0 -f track=alpha -f status=completed
```

#### 5. 자주 만나는 오류와 대처

| 오류 내용 | 원인 및 해결 |
|---|---|
| `Only releases with status draft may be created on draft app` | Play Console에서 앱이 아직 초안(첫 심사 제출 전) 상태입니다. `-Status draft`(CI `status=draft`)로 올리거나 콘솔에서 첫 버전을 제출하세요. |
| `changesNotSentForReview` 관련 400 오류 | Play Console '게시 개요'에 아직 검토 보내지 않은 변경이 대기 중입니다. `-ChangesNotSentForReview`(CI `changes_not_sent_for_review=true`)를 붙여 다시 실행하세요. |
| `403 PERMISSION_DENIED` | 서비스 계정에 `com.jeiel85.aiusage` 앱의 출시 권한이 없거나, 방금 저장해 반영 대기 중입니다. |
| `versionCode has already been used` | 같은 `versionCode`가 이미 Play Console에 올라가 있습니다. 새 앱 버전으로 태그를 발행해야 합니다. |
| `직전 태그 이후 mobile/ 변경이 없습니다` | PC 전용 패치 태그를 실수로 올리지 않기 위한 보호 장치입니다. 의도한 경우 `-Force`(`force_without_mobile_diff=true`)를 붙이세요. |

## 로컬에서 흐름 검증 (선택)

같은 키로 서명한 두 APK를 만들어, GitHub 디버그 빌드를 로컬 서버로 향하게 할 수 있습니다.

```powershell
cd mobile
.\gradlew assembleGithubDebug -Pcut.version=1.55.0 -Pcut.updateManifestUrl=http://10.0.2.2:8000/update.json -Pcut.updateAllowHttp=true
```

에뮬레이터(`10.0.2.2` = PC)에서 그 APK를 설치하고, 더 높은 버전 APK와 그 `update.json`을 PC의 `python -m http.server 8000`으로 내보내면 됩니다.

자동 설치는 앱을 한 번 열고 홈으로 나간 뒤 배경 잡을 바로 돌려 확인합니다(잡 ID `0x0c0228` = 786984).

```bash
adb shell cmd jobscheduler run -f com.jeiel85.aiusage 786984
adb shell dumpsys package com.jeiel85.aiusage | grep -E "versionName|installerPackageName"
```
