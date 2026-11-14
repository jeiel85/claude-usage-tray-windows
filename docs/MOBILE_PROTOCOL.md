# 휴대폰 앱 연동 프로토콜 v1 (#220)

휴대폰 앱(`mobile/`)과 각 PC의 트레이 앱 사이 계약입니다. 구조는 [Homebase](https://github.com/jeiel85/homebase)의
Mobile Protocol v1(`docs/MOBILE_PROTOCOL.md`, DECISIONS M-1~M-20)을 따릅니다. 형식은 [`mobile-protocol/`](mobile-protocol/)의
픽스처로 고정하며, .NET(`MobileProtocolGoldenTests`)과 Kotlin(`ProtocolTest`)이 같은 파일을 검증합니다.
의도적으로 형식을 바꿀 때는 `CUT_UPDATE_GOLDEN=1 dotnet test`로 픽스처를 다시 쓰고, 호환이 깨지면 `schema`를 올립니다.

## 전체 구조

```
 PC A (계정 X) ─┐                         ┌─ 계정 X 카드: PC A + PC B
 PC B (계정 X) ─┼── 각 PC에 직접 연결 ──► 휴대폰 앱 ──┤   (할당량: 가장 최근 관측 / 오늘 토큰: 합계)
 PC C (계정 Y) ─┘   (중계 서버 없음)          └─ 계정 Y 카드: PC C
```

- 휴대폰은 PC마다 QR로 한 번씩 페어링하고, 앱이 화면에 보이는 동안 PC마다 WebSocket 하나를 엽니다.
- PC는 새로 수집하지 않습니다. 이미 갱신 주기마다 조회하는 사용량을 그대로 넘기므로 API 호출 수가 늘지 않습니다.
- **계정 묶기**: PC는 원시 계정 ID 대신 `SHA-256("cut-mobile-v1|account|<provider>|<계정 키>")`만 보냅니다.
  같은 계정이면 어느 PC에서도 같은 해시가 되어 앱이 한 카드로 합치고, 다른 계정은 절대 합쳐지지 않습니다.
  - Claude: `orgUuid|accountUuid`(`accountUuid`를 못 읽으면 `orgUuid`). Team·Enterprise 조직은 여러 사람이 `orgUuid`를 공유하므로 사람 단위로 나누려고 `accountUuid`를 붙입니다.
  - Codex: `~/.codex/auth.json`의 `account_id|user_id`.
  - Antigravity·OpenCode: 계정을 알 수 없어 `…|device|<provider>|<pcId>`로 해시하고 `accountScope: "device"`로 보냅니다.
    앱은 이것을 PC별 카드로 따로 보입니다(서로 다른 계정을 "알 수 없음"으로 합치지 않게).
- **할당량은 합치지 않고 고릅니다**: 계정 단위 값(서버가 어느 PC에나 같은 값을 줌)이라, 데이터가 있는 관측 중 가장 최근 것을 씁니다.
  API가 실패한 PC(`hasData: false`)는 다른 PC의 정상 값을 지우지 않습니다. 리셋 시각이 지난 창은 0%로 보입니다.
- **오늘 토큰은 더합니다**: 각 PC 로컬 로그에서 센 기기 단위 값입니다.

## 연결 경로

HTTP/1.1 + WebSocket, 요청당 연결 하나(`Connection: close`). 인증은 모든 경로에서 같은 Bearer 토큰입니다. 기본 포트 **47830**(Homebase 47820과 겹치지 않게).

| transport | 주소 | 암호화 | PC 바인딩 · 허용 상대 | 설정 |
|---|---|---|---|---|
| `lan` | 기본 게이트웨이가 있는 어댑터의 사설 IPv4 | TLS 1.2/1.3, 자체 서명 ECDSA P-256. 앱은 `certSha256`(DER의 SHA-256)과 일치하는 인증서만 받음 | 그 LAN IP · 같은 서브넷의 사설 주소 | `MobileLan` |
| `tailscale` | MagicDNS 이름, 100.x IP | 평문(WireGuard가 암호화) | tailnet IP · 100.64.0.0/10과 루프백 | `MobileTailscale` |
| `loopback` | 127.0.0.1 | 평문 | 루프백 | `MobileBindLoopback`(개발·에뮬레이터 전용, 설정 파일에서만) |

- 와일드카드(0.0.0.0)에는 바인딩하지 않습니다. 주소가 바뀌면 30초 안에 다시 바인딩합니다.
- 방화벽: 연결 창의 **방화벽 규칙 추가(관리자)** 버튼이 규칙 두 개를 넣습니다 — LAN(원격 LocalSubnet, **개인 프로필만**), Tailscale(원격 100.64.0.0/10). 둘 다 이 exe·TCP 포트로 한정합니다.
- 경로 선택(앱): PC 서브넷 안의 LAN → (휴대폰 Tailscale VPN이 켜져 있으면) Tailscale → 나머지 순으로 모두 시도합니다. 같은 순위에서는 마지막 성공 경로를 먼저 씁니다.
- 경로 갱신: 앱은 연결할 때마다 `GET /api/v1/endpoints`로 현재 경로를 받아 저장합니다(DHCP 변경, Tailscale 켬).

## 페어링 QR

QR 텍스트는 링크입니다([`pairing-link.v1.txt`](mobile-protocol/pairing-link.v1.txt)).

```
https://jeiel85.github.io/claude-usage-tray-windows/pair/#<base64url(UTF-8 JSON {cu, pcId, pcName, code, expiresAt, endpoints})>
```

- 오퍼는 fragment(`#`)에만 있어 서버로 가지 않습니다. `cu: 1`이 이 앱 QR 표시입니다(Homebase QR은 `hb`).
- 앱이 없으면 휴대폰 카메라가 [`docs/pair/`](pair/index.html) 설치 안내 페이지를 엽니다. 그 페이지의 **앱에서 열기**는 같은 내용을 `claudeusage://pair/<payload>`로 앱에 넘깁니다.
- 링크로 열린 경우 앱은 내용을 채워 두고 사용자가 **연결**을 눌러야 페어링합니다(웹 페이지가 임의 링크를 보낼 수 있으므로).
- 코드: 128bit, 5분, 1회용, 새 QR이 이전 것을 무효화, 5분에 10번 틀리면 잠금. 연결 창을 닫으면 코드도 버립니다.

## REST

| 메서드 | 경로 | 인증 | 응답 |
|---|---|---|---|
| GET | `/api/v1/info` | 없음 | `{schema, protocol, pcId, pcName, appVersion, os}` |
| POST | `/api/v1/pair` | 본문의 `code` | 200 `{schema, deviceId, token, pcId, pcName}` · 400 `invalid_code` · 410 `code_expired` · 429 `locked` |
| GET | `/api/v1/endpoints` | Bearer | 200 `{schema, endpoints}`([`endpoints.v1.json`](mobile-protocol/endpoints.v1.json)) · 401 |
| GET | `/api/v1/usage` | Bearer | 200 `UsageSnapshot` · 401 |

`POST /api/v1/pair` 본문은 `{code, deviceName, deviceModel}`. 오류 응답은 항상 `{schema, error, message}`.
토큰은 256bit이며 PC는 SHA-256만 `%APPDATA%\ClaudeUsageTray\mobile\state.json`에 저장합니다. 앱은 Android Keystore AES-GCM으로 암호화해 둡니다.

## Live (WebSocket)

`GET /api/v1/live`에 `Authorization: Bearer <token>`을 붙여 업그레이드합니다. 401은 업그레이드 전에 일반 HTTP로 돌아옵니다.
연결 직후 한 번, 그 뒤 사용량이 바뀔 때마다(=PC의 갱신 주기), 변화가 없으면 60초마다 `UsageSnapshot` 텍스트 프레임을 보냅니다.
서버는 전송 전마다 페어링 상태를 다시 확인하고, 트레이에서 해제된 휴대폰은 close `1008 "revoked"`로 끊습니다(앱은 401과 같게 처리).

## UsageSnapshot ([`usage-snapshot.v1.json`](mobile-protocol/usage-snapshot.v1.json))

| 필드 | 의미 |
|---|---|
| `schema` | 1. 다르면 앱은 반쪽 파싱하지 않고 "버전 불일치"로 멈춤 |
| `ts`, `pcId`, `pcName`, `appVersion` | 보낸 PC |
| `providers[]` | 화면에서 켠 provider만, 6시간 안에 관측한 것만 |
| `providers[].provider` | `claude`·`codex`·`opencode`·`antigravity` |
| `providers[].accountHash`, `accountScope` | 위 "계정 묶기" 참고(`account`·`device`) |
| `providers[].accountLabel` | 표시 이름(Claude만, "이름 · 팀 조직"). 이메일·UUID는 넣지 않음 |
| `providers[].observedAt`, `errorKind` | 이 PC가 마지막으로 조회한 시각, 실패 종류 |
| `providers[].quota` | 다중 PC 동기화와 같은 `UsageSyncQuotaSnapshot`(사용률은 0~1 비율) |
| `providers[].today` | 오늘 이 PC의 토큰·세션·요청 수(없으면 생략) |
