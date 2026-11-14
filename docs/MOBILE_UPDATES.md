# 휴대폰 앱 자동 업데이트 (#228)

휴대폰 앱은 GitHub Releases의 APK를 직접 설치하는 앱이라, 앱이 스스로 새 버전을 확인하고 설치까지 이어 줍니다.
구조는 [Homebase](https://github.com/jeiel85/homebase)의 `update/` 패키지(Markleaf에서 온 구조, Homebase DECISIONS M-4·M-22)를 그대로 옮겼습니다.

## 흐름

1. **확인**: 앱이 `https://github.com/jeiel85/claude-usage-tray-windows/releases/latest/download/update.json`을 **하루 한 번** GET 합니다.
   - GitHub REST API(`api.github.com`)는 쓰지 않습니다. 비인증 한도(시간당 60건)가 IP에 붙어 통신사 NAT 뒤에서는 남의 요청에 막히기 때문입니다.
   - 요청에는 URL과 표준 헤더만 실립니다(쿼리스트링·쿠키·기기 식별자 없음).
   - 온전히 읽힌 응답만 캐시하고 그때만 확인 시각을 기록합니다. 실패는 조용히 넘어가고 다음에 다시 확인합니다.
2. **알림**: 설치된 versionCode보다 큰(정수 비교) 버전이면 계정 탭 위에 배너가 뜨고, Android 알림을 **버전당 한 번** 띄웁니다.
   - 배경 확인은 JobScheduler 주기 잡(12시간, 네트워크 필요, 재부팅 뒤 유지)이고, 실제 GET은 위의 하루 1회 규칙을 따릅니다.
   - minSdk가 기기보다 높으면 제안하지 않습니다. "이 버전 건너뛰기"를 누른 버전도 제안하지 않습니다("지금 확인"을 누르면 건너뛰기가 풀립니다).
3. **설치**(사용자가 누를 때만): 앱 캐시로 내려받고(최대 3회 시도) **SHA-256을 대조**한 뒤, 시스템 설치 앱(`ACTION_INSTALL_PACKAGE` + FileProvider)으로 넘깁니다.
   - '알 수 없는 앱 설치' 권한이 없으면 설정 화면으로 안내합니다.
   - 실패하면 "다시 시도" 또는 "브라우저에서 열기".
4. **설정**: 홈 위쪽 톱니바퀴 → 설정 → **업데이트**에서 끄기(기본 켜짐), 지금 확인, 설치된 버전을 봅니다.

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

## 로컬에서 흐름 검증 (선택)

같은 키로 서명한 두 APK를 만들어, 디버그 빌드를 로컬 서버로 향하게 할 수 있습니다.

```powershell
cd mobile
.\gradlew assembleDebug -Pcut.version=1.55.0 -Pcut.updateManifestUrl=http://10.0.2.2:8000/update.json -Pcut.updateAllowHttp=true
```

에뮬레이터(`10.0.2.2` = PC)에서 그 APK를 설치하고, 더 높은 버전 APK와 그 `update.json`을 PC의 `python -m http.server 8000`으로 내보내면 됩니다.
