# UI 가이드라인 — 사용량 팝업·설정 창

새 공급자·게이지·안내 줄을 넣을 때 기존 화면과 같아 보이게 하기 위한 규칙이다.
값은 `ClaudeUsageTray/Views/UsagePopup.xaml`·`SettingsWindow.xaml` 에 이미 쓰이는 것을 모은 것이고,
규칙과 XAML 이 다르면 **XAML 쪽이 틀린 것**으로 보고 고친다(규칙을 바꿀 때는 이 문서를 같은 PR 에서 고친다).

정리 계기: v1.70.1 의 DeepSeek 게이지가 막대 높이를 `Height="8"` 로 덮어써 다른 게이지보다 두 배 두꺼웠고,
이름이 `오늘 토큰…` 으로 잘렸다(#316).

## 1. 색

| 용도 | 값 |
| --- | --- |
| 본문(이름·제목) | `#F1F5F9` |
| 보조(초기화 문구·요약·안내) | `#8B92B3` |
| 흐린 안내(출처·노트, 빈 상태) | `#5A5F7A` |
| 경고(한도 도달·키 거절 등 사용자가 조치) | `#FBBF24` |
| 오류 | `#EF4444` |
| 막대 바탕(track) | `#2D2F45` |
| 줄 hover 배경 | `#1A1D2E` |
| 시간선 마커 | `#E2E8F0` |

공급자 강조색 — 아이콘 칸·퍼센트 글자·막대 그라디언트는 한 공급자 안에서 같은 계열을 쓴다.

| 공급자 | 아이콘 칸 | 퍼센트 글자 | 막대 스타일(그라디언트) |
| --- | --- | --- | --- |
| Claude | `#8B5CF6` | `#A78BFA` | `UsageBarStyle` (`#8B5CF6`→`#6366F1`) |
| Codex | `#10B981` | `#6EE7B7` | `CodexBarStyle` (`#6EE7B7`→`#10B981`) |
| OpenCode | `#F97316` | `#FB923C` | `OpenCodeBarStyle` (`#FB923C`→`#F97316`) |
| DeepSeek(100milionseek) | `#4D6BFE` | `#93C5FD` | `DeepSeekBarStyle` (`#93C5FD`→`#4D6BFE`) |
| Antigravity | `#6366F1` | `#A5B4FC` | `AntigravityBarStyle` (`#A5B4FC`→`#818CF8`) |

새 공급자는 이 표에 한 줄을 더하고, 막대 스타일은 기존 스타일을 복제해 그라디언트 색만 바꾼다.

## 2. 공급자 섹션

1. **접힌 줄** — `AgentRowBtn` 버튼 안에 3열 Grid: 12×12 아이콘 칸(모서리 2, 흰 글자 8pt 굵게) · 이름 11pt 굵게 `#F1F5F9` · 대표 퍼센트 13pt 굵게(강조색). 값이 없으면 `—`.
2. **펼친 본문** — `Margin="10,6,10,0"`, 펼침 상태(`IsXxxFocused`)일 때만 보인다. 한 번에 한 섹션만 펼친다(`FocusedProvider`).
3. 본문 순서: 등급 배지 → 게이지 행들 → 부가 줄(한도 도달·크레딧) → 안내 → 출처 노트(10pt `#5A5F7A`). 크레딧 줄(Claude 추가 크레딧·Console 크레딧, Codex 크레딧, Antigravity AI 크레딧)은 Style 트리거에 `ShowCredits` False → Collapsed 를 넣어 설정 `크레딧 표시` 를 따른다(#354). 새 크레딧 줄도 같은 트리거를 둔다.
4. 섹션 사이 간격은 바깥 StackPanel `Margin="0,0,0,8"`.
5. **간단히 보기** 목록(`IsCompact…` 영역)에도 같은 공급자 줄을 넣는다 — 이름 11pt 강조색 · 막대(스타일 기본 높이) · 퍼센트 11pt.

## 3. 게이지

**막대**
- 높이는 막대 스타일의 값(4)을 그대로 쓴다. `ProgressBar` 에 `Height` 를 직접 주지 않는다.
- 막대는 높이 8 틀 안에 두고(`<Grid Height="8" Margin="0,4,0,0" ToolTip="{…PaceTip}">`), 같은 틀에 시간선 마커를 겹친다: `<views:TimelineMarker Percent="{Binding …TimePercent}" Visibility="{Binding Has…Timeline, Converter={StaticResource BoolToVisibility}}"/>` (너비 1.5 · 높이 8 · `#E2E8F0` · 모서리 1 은 `Views/TimelineMarker.cs` 가 그린다, #237). 마커를 Grid 열로 손수 짜지 않는다 — `PopupGaugeBindingRuleTests` 가 막는다.
- 창 길이를 모르면 마커를 숨긴다(`HasTimeline`).
- 값은 `Value` 가 아니라 `views:GaugeAnimation.Value="{Binding …}"` 로 묶는다 — 값이 바뀌면 막대가 차오른다(#322). 겹친 막대(페이스 오버레이)도 둘 다 이렇게 묶어야 같이 움직인다.

**행 — 두 종류만 쓴다**

| | 주 게이지 | 목록 행 |
| --- | --- | --- |
| 쓰는 곳 | 섹션의 대표 창(Claude·Codex 5시간·주간, DeepSeek 오늘), Antigravity 그룹 안 창(`5시간`·`주간`) | 여러 개가 나열되는 창(Codex 추가 한도, Claude 모델 한정 한도, 그룹 없는 레거시 행) |
| 이름 | 11.5pt(OpenCode 10.5pt), **Auto 열 — 잘리지 않는다** | 10.5pt, `*` 열, 길면 말줄임 + 전체 이름 툴팁 |
| 퍼센트 · 초기화 | 오른쪽 `DockPanel`: 퍼센트 11pt 굵게(강조색) + 초기화 10pt `#8B92B3`, 폭이 모자라면 **초기화 문구만 오른쪽 정렬 줄바꿈**(#234) | 오른쪽 가로 StackPanel: 퍼센트 11pt 굵게 + 초기화 10pt |
| 요약 | 퍼센트 전용(Claude·Codex·OpenCode·Antigravity)은 막대 아래 중복 줄을 두지 않고 라벨 툴팁에 둔다(#224). 토큰·횟수 등 추가 수치가 있는 행(DeepSeek)만 막대 아래 10pt `#8B92B3`, 줄바꿈 허용 | 막대 아래 10pt `#8B92B3`, 줄바꿈 허용 |
| 행 간격 | 행 아래 10 | 같음 |

- **Antigravity 그룹 블록**(#345, #346): 그룹 첫 행 위에 그룹 제목(`10.5pt SemiBold #F1F5F9`)과 서버 포함 모델 요약(`9.5pt #5A5F7A` 줄바꿈, 있을 때만)을 두고, 두 번째 이후 그룹 위에 `1px #2D2F45` 구분선을 넣는다. 크레딧 줄 위에는 구분선을 두지 않는다(#351) — 0% 막대의 빈 트랙이 선처럼 보여 바로 아래 구분선과 두 줄로 겹쳐 보인다. 게이지 뒤 부가 줄(크레딧·추가 사용)은 Claude 추가 크레딧 줄처럼 여백만으로 나눈다. 그룹 안의 각 창은 Claude·Codex 주 게이지 규칙(`11.5pt` `Auto` 짧은 창 이름 + 라벨 툴팁 요약 + `*` `DockPanel`, 막대 아래 중복 요약 줄 없음)을 따르고, 아직 쓰지 않아 `resetTime` 없이 온 0% 창은 초기화 자리에 `미사용` 을 표시한다.
- 절대 시각(`(11-20 09:00)`)은 줄바꿈으로 둘로 갈리지 않게 NBSP 로 묶는다(`UsageCalculator.FormatResetLabel`).
- 퍼센트 글자 색은 그 공급자의 강조색, 막대 스타일도 같은 공급자 것.

## 4. 안내 줄

| 종류 | 모양 |
| --- | --- |
| 정보(잠시 못 읽음, 동기화 값 대기) | 10pt `#8B92B3` 이탤릭, 줄바꿈 |
| 조치 필요(키 거절, 한도 도달, 결제 실패) | 10pt `#FBBF24`, 줄바꿈 |
| 오류 | 10pt `#EF4444`, 줄바꿈 |
| 출처·노트 | 10pt `#5A5F7A` |

빈 문자열이면 자리도 차지하지 않게 `StringToVisibility` 로 접는다.

## 5. 설정 창

- 구역 제목 `SectionLabel`, 구역 사이 1px `#2D2F45` 구분선.
- 설명 11pt `#5A5F7A` 줄바꿈, 상태 문구 11pt `#7C83A4`.
- 입력 칸은 `DarkTextBox`·`DarkPasswordBox`. 저장값을 다시 보여 주지 않는 비밀 칸은 "저장된 값 있음" 문구 + 지우기 `LinkBtn`.
- 보조 동작은 `LinkBtn`. 칸 옆의 주 동작 버튼(저장 등)은 기본 WPF 템플릿을 쓰지 않는다 — 비활성일 때 흰 상자로 그려진다. 모서리 6의 Border 템플릿 + 비활성 배경 `#2D2F45`·글자 `#5A5F7A`(DeepSeek 키 [저장] 참고).

## 6. 문구

- 모든 문구는 `LocalizationService.cs` 에 ko·zh·ja·en 네 언어로(`AGENTS.md` 3).
- 줄에 들어가는 이름은 짧게 — 서비스 이름처럼 긴 고유명사는 섹션 제목이나 출처 노트로 보낸다.

## 7. 확인

- 팝업·설정 창을 이미지로 렌더해 눈으로 본다: `CUT_UI_SNAPSHOT_DIR=<폴더> CUT_UI_SNAPSHOT_LANG=ko|en dotnet test --filter UiSnapshotTests`. 새 섹션은 `UiSnapshotTests` 에 펼친 화면을 추가한다.
- 주 게이지 행은 `WindowRowLayoutTests` 처럼 네 언어·가장 긴 초기화 문구에서 이름·퍼센트·초기화가 겹치거나 잘리지 않는지 본다.
