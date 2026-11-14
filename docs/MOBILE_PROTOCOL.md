# 휴대폰 앱 연동·PC 동기화 프로토콜 v1 (#220·#222·#223)

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
| POST | `/api/v1/push` | Bearer | 200 `{schema, deviceId, registered: true}` · 400 `invalid_subscription` · 401 |
| DELETE | `/api/v1/push` | Bearer | 200 `{schema, deviceId, registered: false}` · 401 |
| GET (WS) | `/api/v1/sync` | Bearer | PC 동기화 WebSocket(아래) |

`POST /api/v1/pair` 본문은 `{code, deviceName, deviceModel, kind?}` — `kind`는 `phone`(기본) 또는 `pc`(PC 동기화). 오류 응답은 항상 `{schema, error, message}`.
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

## 앱 푸시 알림 (#222, Homebase M-19·M-21·M-25)

QR로 PC를 연결하면 알림도 함께 연결됩니다. 토픽 이름을 정하거나 입력할 필요가 없습니다.

1. 앱은 휴대폰의 UnifiedPush 배포자(보통 ntfy 앱)에서 엔드포인트를 받고, P-256 키쌍과 16바이트 auth를 만듭니다(Keystore로 감싸 저장). 배포자가 하나면 첫 PC 연결 때 자동으로 켭니다.
2. 앱은 연결된 모든 PC에 `POST /api/v1/push` `{schema, endpoint, p256dh, auth, mutedCategories?}`([`push-subscription.v1.json`](mobile-protocol/push-subscription.v1.json))로 등록합니다. 엔드포인트가 바뀔 때, 페어링 직후, 실시간 연결이 될 때마다 다시 등록합니다.
3. PC는 알림마다 RFC 8291(`aes128gcm`)로 암호화해 엔드포인트로 POST합니다. 중계 서버(ntfy.sh)는 암호문만 봅니다. `TTL` 86400초, `Urgency`는 경고·심각이면 `high`.
   - 429: `Retry-After`(기본 60초, 최대 1시간) 동안 그 엔드포인트를 쉼. 404·410: 구독 삭제(앱이 다음 연결 때 다시 등록).
   - 같은 엔드포인트를 가진 다른 기기 항목은 같은 휴대폰의 옛 페어링이므로 철회합니다(알림 중복 방지).
4. **기존 ntfy 토픽 이전**: 앱 푸시를 등록한 휴대폰이 있으면 PC는 평문 토픽으로 보내지 않습니다. 앱 푸시가 모두 실패했거나 등록된 휴대폰이 없을 때만 토픽으로 보냅니다(iOS·앱 미설치 호환). 휴대폰이 그 범주를 껐으면 토픽으로도 보내지 않습니다.

복호화된 메시지([`push-message.v1.json`](mobile-protocol/push-message.v1.json)):

| 필드 | 의미 |
|---|---|
| `schema` | 1 |
| `id`, `pcId`, `pcName` | 알림 id, 보낸 PC. 앱은 페어링하지 않은 PC의 메시지를 버림 |
| `category` | `usage`·`exhaustion`·`rate_limit`·`reset`·`weather`·`test`. 앱의 "받을 알림" 스위치 단위 |
| `priority` | `info`·`warning`·`critical` |
| `title`, `body` | 표시할 평문 |
| `eventKey`, `eventToleranceSec` | 사건 키(예전 ntfy `sequence_id`, #175). 여러 PC가 같은 사건을 보내면 앱이 한 번만 띄움 — 접두사가 같고 시각 차가 허용 오차(+59초) 안이면 같은 사건 |
| `clickUrl` | 날씨 알림의 예보 링크(https만) |
| `ts` | 생성 시각 |

두 구현이 맞는지는 [`push-message.v1.aes128gcm.txt`](mobile-protocol/push-message.v1.aes128gcm.txt)로 확인합니다. .NET이 RFC 8291 부록 A의 서버 키·salt로 위 메시지를 암호화하고, Kotlin 테스트가 부록 A의 휴대폰 키로 복호화합니다.

## PC 동기화 (#223)

공유 폴더 없이 PC끼리 직접 사용량을 주고받습니다.

- **동기화 코드** = 휴대폰 QR과 같은 일회용 링크(5분·1회용). PC A의 설정 → 연결 → "이 PC의 동기화 코드 복사"(또는 휴대폰 연결 창의 같은 버튼) → PC B의 설정 → 연결에 붙여 넣기 → B가 `POST /api/v1/pair`에 `kind: "pc"`로 페어링하고 토큰을 DPAPI로 감싸 `peers.json`에 저장합니다.
- **연결**: B → A `GET /api/v1/sync` WebSocket(Bearer). B가 여는 쪽이라 A만 B에서 닿으면 됩니다(B의 포트·방화벽은 필요 없음). 경로는 LAN(TLS 핀) → Tailscale 순.
- **프레임**: 양쪽 모두 `{schema: 1, kind: "sync", fromPcId, fromPcName, snapshots: UsageSyncSnapshot[]}`를 연결 직후, 표가 바뀔 때, 변화가 없으면 60초마다 보냅니다. `snapshots`는 공유 폴더 동기화 파일과 **같은 형식·같은 계정 해시**라, 받은 쪽은 기존 선택 규칙(`SelectQuotaCandidates`·`MergeLocalTotals`)을 그대로 씁니다.
- **가십**: 각 PC는 (원래 PC, provider)별 최신 스냅샷 표를 갖고, 받은 것 중 더 새로운 것만 반영하며, 바뀌면 모든 연결로 다시 보냅니다. 그래서 B·C가 A에만 연결해도 B와 C가 서로의 값을 갖습니다. 자기 원본 값은 다른 PC를 거쳐 돌아와도 받지 않고, 24시간 넘은 관측·5분 넘는 미래 시각은 버립니다.
- **Claude Code가 없는 PC**: 계정 키(orgUuid)가 없으면 그룹에서 할당량을 가장 최근에 관측한 Claude 계정을 대신 보여 줍니다.
- **해제**: A의 연결 탭(또는 휴대폰 연결 창)에서 B를 해제하면 다음 전송 전에 1008 `revoked`로 끊기고, B는 다시 연결하지 않습니다.
- **한계**: PC끼리 같은 LAN이나 Tailscale로 닿아야 합니다. 다른 네트워크(회사·집)이고 Tailscale이 없다면 기존 공유 폴더 동기화를 함께 쓰세요 — 두 경로의 값은 한 목록으로 합쳐지고, 같은 PC가 두 경로로 들어오면 더 새로운 하나만 씁니다.
