---
slug: multi-pc-usage-sync
status: planned
intent: clear
pending-action: none
approach: Add optional shared-folder device snapshots; use newest successful account-quota snapshot, aggregate local token totals per device, never sync credentials or raw logs.
---

# Draft: multi-pc-usage-sync

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->
- C1 | Snapshot data contract and file I/O are explicit, token-free, and device-scoped. | active | `.omo/plans/multi-pc-usage-sync.md`
- C2 | Refresh pipeline reads/writes snapshots without increasing API pressure and falls back to local state. | active | `.omo/plans/multi-pc-usage-sync.md`
- C3 | Settings/UI exposes sync opt-in and folder choice with four-language localization. | active | `.omo/plans/multi-pc-usage-sync.md`
- C4 | Tests and manual QA prove stale, corrupt, missing, and multi-device cases. | active | `.omo/plans/multi-pc-usage-sync.md`

## Open assumptions (announced defaults)
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
<!-- assumption | adopted default | rationale | reversible? -->
- Sync transport | Shared local folder such as OneDrive/Dropbox, configured by the user | No server, no new account, realistic for two Windows PCs. | yes
- Default enabled state | Off by default | Avoid writing data to a cloud-synced folder unless the user opts in. | yes
- Snapshot TTL | 5 minutes for API quota snapshots, 24 hours for same-day local token totals | API data must be fresh; local daily totals remain useful through the day but must expire across dates. | yes
- Merge rule | Account-level quotas use newest successful snapshot; local token totals are summed by `deviceId` after deduping one file per device/provider/date. | Prevents stale API errors from winning and prevents two PCs from overwriting each other. | yes
- Security boundary | Never sync `.credentials.json`, refresh/access tokens, raw `*.jsonl`, Codex `auth.json`, or OpenCode DB. | Required to avoid credential leakage and duplicate/partial log parsing across machines. | no

## Findings (cited - path:lines)
- `UsageApiService.FetchUsageAsync` calls `https://api.anthropic.com/api/oauth/usage`, records `LastRawResponse`, `LastError`, and `LastRetryAfterSeconds`, and backs off 429/403 responses. Evidence: `ClaudeUsageTray/Services/UsageApiService.cs:17`, `ClaudeUsageTray/Services/UsageApiService.cs:30`, `ClaudeUsageTray/Services/UsageApiService.cs:52`.
- Claude local token totals come from `%USERPROFILE%/.claude/projects/**/*.jsonl` and are scanner-local. Evidence: `ClaudeUsageTray/Services/SessionMonitor.cs:9`, `ClaudeUsageTray/Services/SessionMonitor.cs:42`, `ClaudeUsageTray/Services/SessionMonitor.cs:53`.
- History is already provider/account scoped but persisted under `%USERPROFILE%/.claude` as local JSON; sharing this file directly would invite last-writer-wins conflicts. Evidence: `ClaudeUsageTray/Services/HistoryService.cs:15`, `ClaudeUsageTray/Services/HistoryService.cs:25`, `ClaudeUsageTray/Services/HistoryService.cs:174`.
- `MainViewModel.RefreshAsync` refreshes providers in parallel every polling interval; Claude currently fetches API data and immediately scans local sessions in one method. Evidence: `ClaudeUsageTray/ViewModels/MainViewModel.cs:981`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:989`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1143`.
- Settings are stored in `%USERPROFILE%/.claude/claude-usage-tray.json`, loaded/saved through `NotificationSettings`; new sync options must join this model and preserve existing settings. Evidence: `ClaudeUsageTray/Services/SettingsService.cs:9`, `ClaudeUsageTray/Models/NotificationSettings.cs:5`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:451`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:617`.
- Settings UI localization is manual in `SettingsWindow.ApplyLocalization`, so new strings must be added to `LocalizationService` for ko/zh/ja/en and wired in code-behind. Evidence: `ClaudeUsageTray/Views/SettingsWindow.xaml.cs:114`, `ClaudeUsageTray/Services/LocalizationService.cs:5`.
- Codex mixes direct API quota and local session log totals; Gemini CLI and OpenCode are local-log/local-DB only. Evidence: `ClaudeUsageTray/Services/CodexUsageMonitor.cs:22`, `ClaudeUsageTray/Services/GeminiCliUsageMonitor.cs:39`, `ClaudeUsageTray/Services/OpenCodeUsageMonitor.cs:24`.

## Decisions (with rationale)
- Use one JSON file per device/provider/date, not one shared mutable aggregate file. Rationale: reduces merge conflict risk in cloud folders and avoids last-writer-wins data loss.
- Store display snapshots only. Rationale: enough to recover UI from another PC's successful API call while keeping credentials and source logs local.
- Add a dedicated `UsageSyncService` rather than extending `HistoryService`. Rationale: history is date chart persistence; sync has device identity, TTL, merge, corruption handling, and atomic write concerns.
- Use atomic write (`.tmp` then replace/move) for snapshot files. Rationale: cloud sync clients can read mid-write otherwise.
- Keep sync optional and disabled by default. Rationale: users must choose the shared folder and accept cloud storage of usage metadata.
- Do not release/bump for this planning-only commit. Rationale: no app code or user-facing binary behavior changes.

## Scope IN
- Plan issue: GitHub #78.
- Optional shared-folder snapshot sync for Claude, Codex, Gemini CLI, and OpenCode display data.
- Snapshot data contract, settings contract, merge semantics, stale/corrupt fallback behavior, tests, and manual QA instructions.
- README issue entry and `.omo/plans/multi-pc-usage-sync.md`.

## Scope OUT (Must NOT have)
- No implementation in this planning commit.
- No syncing credentials, OAuth tokens, raw Claude/Codex/Gemini logs, or OpenCode DB.
- No central hosted backend in the first implementation.
- No version bump, tag, release, or binary artifact for the planning-only commit.

## Open questions
- None blocking. The plan adopts shared-folder sync as the first implementation target and leaves central-server sync as a later optional roadmap item.

## Approval gate
status: approved-by-user-request
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
- User requested: "구현계획만 하고 푸시하고 마무리".
- Interpretation: write the implementation plan artifact, register tracking issue, commit, and push; do not implement app code.
- Review note: Metis subagent review was not run because this turn did not explicitly authorize subagents and the available multi-agent tool policy forbids spawning without explicit subagent/delegation permission. Main session performed a self-audit instead.
