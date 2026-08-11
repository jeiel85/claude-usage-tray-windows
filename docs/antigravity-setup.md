# Antigravity 통합 셋업

> **보통은 아무 설정도 필요 없습니다.** Antigravity 에 로그인만 되어 있으면 트레이 앱이 알아서 사용량을 읽습니다.
> 이 문서는 자동 탐색이 실패했을 때 직접 값을 지정하는 방법을 다룹니다.

## 어떻게 동작하나

트레이 앱은 Antigravity 를 실행하지 않아도 사용량을 읽습니다. 앱의 로컬 서버가 아니라 Google 백엔드를 직접 호출하기 때문입니다.

1. **토큰 읽기** — Windows 자격 증명 관리자의 `gemini:antigravity` 항목에서 Antigravity 가 저장해 둔 토큰을 읽습니다.
2. **그대로 사용** — 저장된 access token 이 아직 유효하면(발급 후 약 1시간) 그대로 씁니다. 이 경로에는 OAuth client 자격 증명이 **필요 없습니다.**
3. **만료 시 갱신** — 토큰이 만료됐으면 refresh token 으로 갱신합니다. 이때만 client_id/secret 이 필요하며, 설치된 `language_server.exe` 에서 자동으로 찾습니다(약 2초). 통한 조합은 `%APPDATA%\ClaudeUsageTray\antigravity-oauth-client.json` 에 저장해 다음부터는 다시 훑지 않습니다.
4. **조회** — `v1internal:retrieveUserQuotaSummary` 로 그룹별(Gemini / Claude·GPT) 주간·5시간 잔여량을 받습니다. Antigravity 앱의 `Models & Usage` 화면이 보여주는 값과 같습니다.

로그인하지 않았거나, 세션이 만료됐는데 갱신에 실패하면 오류 대신 섹션이 숨겨집니다. **Antigravity 를 한 번 실행하면** 앱이 토큰을 새로 발급해 두므로 다시 표시됩니다. 대개 이것으로 해결됩니다.

## 자동 탐색이 실패할 때

Antigravity 가 설치 위치를 바꾸거나 자격 증명 형식을 바꾸면 자동 탐색이 실패할 수 있습니다. 그때는 값을 직접 넣습니다.

### 1. 값 추출

설치된 바이너리에서 뽑습니다. Antigravity 를 실행 중이 아니어도 됩니다.

```powershell
$exe = "$env:LOCALAPPDATA\Programs\antigravity\resources\bin\language_server.exe"
$bytes = [IO.File]::ReadAllBytes($exe)
$text = [Text.Encoding]::ASCII.GetString($bytes)
([regex]::Matches($text, '[0-9]{6,}-[a-z0-9]{15,}\.apps\.googleusercontent\.com')).Value | Sort-Object -Unique
([regex]::Matches($text, 'GOCSPX-[A-Za-z0-9_-]{28}')).Value | Sort-Object -Unique
```

- **client_id**: `<숫자>-<영숫자>.apps.googleusercontent.com`
- **client_secret**: `GOCSPX-` + 28자 (총 35자). 길이가 35자가 아니면 경계를 잘못 잡은 것입니다.

각각 여러 개가 나올 수 있습니다. 바이너리에 짝 정보가 없으므로 어느 조합이 맞는지는 시도해 봐야 알 수 있습니다(앱은 자동으로 순서대로 시도합니다).

### 2. 파일 작성

```
%APPDATA%\ClaudeUsageTray\antigravity-oauth-client.json
```

```json
{
  "client_id": "<숫자>-<영숫자>.apps.googleusercontent.com",
  "client_secret": "GOCSPX-<28자>"
}
```

### 3. 확인

트레이 앱 새로고침 → `Antigravity` 섹션에 플랜과 네 개의 게이지가 뜨면 성공입니다.

아무 일도 없다면 자격 증명 관리자에 항목이 있는지부터 확인하세요.

```powershell
cmdkey /list:gemini:antigravity
```

없으면 Antigravity 로그인이 먼저입니다.

## 왜 secret 을 코드에 박지 않나

Google 의 secret-scanning 정책상 공개 저장소에 client_secret 이 노출되면 자동으로 폐기될 수 있고, Antigravity 업데이트로 값이 바뀔 수도 있습니다. 그래서 저장소에 넣지 않고 각 PC 에 설치된 바이너리에서 읽습니다.
