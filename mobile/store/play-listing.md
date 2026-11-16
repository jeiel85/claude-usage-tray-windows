# Google Play 등록 초안 (#256)

Play Console(EL Studio → "AI 사용량", `com.jeiel85.aiusage`)에 넣을 값의 원본입니다. Play 빌드(`play` flavor) 기준이며, GitHub 빌드와 달리 앱 안 업데이트 확인이 없습니다.

> **상태: 결정 반영(2026-11-16), Play Console 입력 전.** 결정: 데이터 보안 A안, 연락처 jeiel85@gmail.com, 거래자 아님, 앱 액세스는 샘플 데이터 둘러보기(#261)로 안내.

---

## 1. 기본 스토어 등록정보 (ko-KR)

| 항목 | 값 | 제한 |
|---|---|---|
| 앱 이름 | AI 사용량 | 30자 |
| 간단한 설명 | PC의 Claude·Codex 사용량과 한도 알림을 휴대폰에서 바로 확인하세요. | 80자 |
| 앱 아이콘 | `icon-512.png` (등록 완료, 임시 저장) | 512×512 |
| 그래픽 이미지 | `feature-graphic.png` | 1024×500 |
| 휴대전화 스크린샷 | `screenshots/01-accounts.png`~`04-settings.png` (1080×1920, 한국어, 샘플 데이터) | 2~8장, 9:16 |

### 자세한 설명 (4000자 이내)

```
Windows PC에서 쓰는 AI 코딩 도구의 사용량을 휴대폰에서 한눈에 봅니다.

PC 트레이 앱 "Claude Usage Tray"(무료, 오픈 소스)가 이미 모으고 있는 사용량을 QR 한 번으로 휴대폰에 연결합니다. 책상을 떠나 있어도 한도가 얼마나 남았는지, 언제 초기화되는지 바로 확인할 수 있습니다.

■ 이런 걸 보여 줍니다
• Claude: 5시간·7일 사용 한도, 모델별 한도, 초기화까지 남은 시간
• Codex: 단기·장기 사용 한도
• Antigravity·OpenCode: PC에서 확인되는 사용량
• 오늘 사용한 토큰과 세션 수 (여러 PC 합계)

■ 여러 PC를 계정 단위로
집 PC와 회사 PC가 같은 계정이면 한 카드로 묶어 보여 줍니다. 다른 계정은 절대 섞지 않습니다.

■ 알림
사용량 50·75·90·100%, 초기화 전 소진 예상, 사용 한도 도달, 한도 초기화를 휴대폰 알림으로 받습니다. 알림은 PC에서 종단 간 암호화(RFC 8291)되어 푸시 앱(ntfy 등 UnifiedPush 앱)을 거쳐 옵니다. 받은 알림은 앱 안 알림센터에 30일 동안 남습니다.

■ 개인정보
• 개발자 서버가 없습니다. 휴대폰은 내가 QR로 연결한 PC에만 직접 접속합니다(같은 Wi-Fi, Tailscale, 또는 PC에서 켠 Cloudflare Tunnel).
• 광고·분석·사용 기록 수집이 없습니다.
• 계정 이메일이나 ID는 휴대폰으로 보내지 않습니다(계정 구분은 해시로).

■ 필요한 것
• Windows PC에 "Claude Usage Tray" 설치: https://github.com/jeiel85/claude-usage-tray-windows
• 알림을 받으려면 UnifiedPush 앱(예: ntfy, 무료·계정 불필요)

이 앱은 개인이 만든 오픈 소스 프로젝트이며 Anthropic, OpenAI, Google과 관련이 없습니다. Claude, Codex, Gemini 등은 각 회사의 상표입니다.
```

---

## 2. 앱 콘텐츠 (정책 선언)

### 개인정보처리방침
- URL: `https://jeiel85.github.io/claude-usage-tray-windows/privacy/` — 원본 `docs/privacy/index.html`(GitHub Pages). Play는 데이터를 수집하지 않는 앱도 방침 URL을 요구합니다.

### 앱 액세스 (로그인 세부정보)
- 선택: **일부 기능이 제한됨** — 로그인은 없지만, 실제 사용량은 PC와 페어링해야 보입니다.
- 검토자 안내 (Play Console에 영어로 입력):

```
No account or password. This app shows AI usage that a companion Windows tray app ("Claude Usage Tray", free and open source) collects on the user's own PC; the phone connects directly to paired PCs, there is no developer server.

To review without a PC: on the first screen tap "Try with sample data" (샘플 데이터로 둘러보기). It shows the real Accounts and PCs screens with three example PCs; "End" returns.

To pair a real PC: run ClaudeUsageTray.exe from https://github.com/jeiel85/claude-usage-tray-windows/releases/latest on Windows, open the tray icon > Settings > Phone app, turn it on and click "Connect phone… (show QR)", then tap "Add PC" in the app and scan the QR. Phone and PC must be on the same Wi-Fi.
```

### 광고
- 아니요, 광고 없음.

### 콘텐츠 등급 (IARC 설문)
- 카테고리: **기타 모든 앱 유형**(유틸리티·생산성)
- 폭력·성적 콘텐츠·욕설·약물·도박: 모두 아니요
- 사용자 간 상호작용/콘텐츠 공유: 아니요 (알림은 사용자 본인의 PC에서만 옴)
- 위치 공유: 아니요 / 디지털 상품 구매: 아니요 / 웹 브라우저·검색: 아니요
- 예상 등급: 전체이용가(3+)

### 타겟층
- 대상 연령: **18세 이상만** (유료 AI 구독을 쓰는 개발자용 도구)
- 아동의 관심을 끌 수 있는가: 아니요

### 데이터 보안 — A안으로 결정
Play 정의: "기기 밖으로 전송하면 수집". 사용자 본인의 PC로 보내는 경우의 예외 규정은 없습니다. 예외는 종단 간 암호화(중간자·개발자 모두 못 읽음)와 기기 안 처리뿐입니다.

이 앱(Play 빌드)이 기기 밖으로 보내는 것:

| 데이터 | 받는 곳 | 목적 | 비고 |
|---|---|---|---|
| 기기 이름·모델 | 사용자가 페어링한 PC | PC의 '연결된 휴대폰' 목록 표시 | 페어링 때 1회 |
| 푸시 구독(엔드포인트 URL·공개 키·인증 비밀값) | 사용자가 페어링한 PC | PC가 알림을 암호화해 보내기 | 재연결 때마다 |
| 받을 알림 종류 설정 | 사용자가 페어링한 PC | 끈 종류는 PC가 보내지 않음 | |

- 사용량 데이터는 PC → 휴대폰으로 **받기만** 합니다. QR 카메라 영상은 기기 안에서만 처리합니다.
- 알림 본문은 PC에서 종단 간 암호화되어 중계 서버가 읽을 수 없습니다 → 수집 예외.

**권장안 A (보수적 신고)**:
- 데이터 수집: 예 / 공유: 아니요
- 유형: **기기 또는 기타 ID** — 수집, 공유 안 함, 필수, 목적 "앱 기능"
- 전송 중 암호화: 예 (LAN은 인증서 고정 TLS, Tailscale은 WireGuard, Cloudflare는 TLS)
- 삭제 요청 방법: PC 앱의 '연결된 휴대폰'에서 연결 해제하면 그 PC의 기기 이름·모델·푸시 구독이 지워짐(앱에서 PC 삭제는 푸시 구독만 삭제 요청). 개발자는 데이터를 받지 않음.

**대안 B ("수집 안 함")**: 데이터가 개발자·제3자가 아닌 사용자 본인 PC로만 간다는 해석. 근거가 되는 명시 예외가 없어 정책 위반으로 지적될 위험이 있습니다.

### 정부 앱 / 금융 기능 / 건강
- 정부 앱 아님 / 금융 기능 없음 / 건강 기능 없음

### 판매자 상태 (EU 디지털 서비스법)
- **거래자 아님** (무료·무수익 개인 프로젝트, 사용자 결정). 거래자로 신고하면 주소·전화번호가 EU 스토어에 공개됩니다.

---

## 3. 스토어 설정

- 앱 카테고리: **도구** (대안: 생산성)
- 연락처 이메일 (스토어에 공개됨): `jeiel85@gmail.com`
- 웹사이트: `https://github.com/jeiel85/claude-usage-tray-windows`

---

## 스크린샷 다시 만들기
디버그 빌드의 `DesignPreviewActivity`(실제 앱 화면 + `domain/SampleData`)를 9:16(1080×1920)·한국어·데모 상태 표시줄로 찍습니다.

```bash
adb shell cmd locale set-app-locales com.jeiel85.aiusage --locales ko-KR
adb shell wm size 1080x1920 && adb shell wm density 420
adb shell settings put global sysui_demo_allowed 1
adb shell am broadcast -a com.android.systemui.demo -e command enter
adb shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm 1200
adb shell am broadcast -a com.android.systemui.demo -e command notifications -e visible false
# screen = accounts | pcs | history | settings
adb shell am start -W -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.DesignPreviewActivity -e screen accounts
adb exec-out screencap -p > screenshots/01-accounts.png
adb shell am broadcast -a com.android.systemui.demo -e command exit
adb shell wm size reset && adb shell wm density reset
```

## 남은 일
- 비공개 테스트 트랙: 국가 선택, 테스터 목록(12명 이상) → 검토 전송 → 14일.
