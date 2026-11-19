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
3. 본문 순서: 등급 배지 → 게이지 행들 → 부가 줄(한도 도달·크레딧) → 안내 → 출처 노트(10pt `#5A5F7A`).
4. 섹션 사이 간격은 바깥 StackPanel `Margin="0,0,0,8"`.
5. **간단히 보기** 목록(`IsCompact…` 영역)에도 같은 공급자 줄을 넣는다 — 이름 11pt 강조색 · 막대(스타일 기본 높이) · 퍼센트 11pt.

## 3. 게이지

**막대**
- 높이는 막대 스타일의 값(4)을 그대로 쓴다. `ProgressBar` 에 `Height` 를 직접 주지 않는다.
- 막대는 높이 8 틀 안에 두고(`<Grid Height="8" Margin="0,4,0,0" ToolTip="{…PaceTip}">`), 같은 틀에 시간선 마커를 겹친다: 너비 1.5 · 높이 8 · `#E2E8F0` · 모서리 1, 위치는 `PercentToStar` 두 열.
- 창 길이를 모르면 마커를 숨긴다(`HasTimeline`).
- 값은 `Value` 가 아니라 `views:GaugeAnimation.Value="{Binding …}"` 로 묶는다 — 값이 바뀌면 막대가 차오른다(#322). 겹친 막대(페이스 오버레이)도 둘 다 이렇게 묶어야 같이 움직인다.

**행 — 두 종류만 쓴다**

| | 주 게이지 | 목록 행 |
| --- | --- | --- |
| 쓰는 곳 | 섹션의 대표 창(Claude·Codex 5시간·주간, DeepSeek 오늘) | 여러 개가 나열되는 창(Antigravity 모델, Codex 추가 한도, Claude 모델 한정 한도) |
| 이름 | 11.5pt, **Auto 열 — 잘리지 않는다** | 10.5pt, `*` 열, 길면 말줄임 + 전체 이름 툴팁 |
| 퍼센트 · 초기화 | 오른쪽 `DockPanel`: 퍼센트 11pt 굵게(강조색) + 초기화 10pt `#8B92B3`, 폭이 모자라면 **초기화 문구만 오른쪽 정렬 줄바꿈**(#234) | 오른쪽 가로 StackPanel: 퍼센트 11pt 굵게 + 초기화 10pt |
| 요약 | 막대 아래 10pt `#8B92B3`, 줄바꿈 허용 | 같음 |
| 행 간격 | 행 아래 10 | 같음 |

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
