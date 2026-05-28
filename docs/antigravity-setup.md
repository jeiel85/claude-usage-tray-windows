# Antigravity 통합 셋업

Antigravity (Google Gemini Code Assist IDE) 의 모델별 quota 패널을 트레이에 표시하려면 OAuth client 자격 증명이 필요합니다. Antigravity 의 비공식 API 를 호출하기 위함이며, 값은 사용자 PC 의 Antigravity 바이너리/메모리에서 추출합니다.

## 어떤 값이 필요한가

- **client_id**: `<숫자>-<영숫자>.apps.googleusercontent.com` 형태
- **client_secret**: `GOCSPX-` 로 시작하는 문자열 (35자 안팎)

## 추출 방법

### 옵션 A — 메모리 추출 (Antigravity 실행 중일 때만, 권장)

Antigravity 가 실행 중인 상태에서 `language_server.exe` 프로세스의 메모리에 client_id/secret 이 평문으로 보관됩니다. PowerShell 로 추출:

```powershell
# 1. language_server.exe PID 확인
$pid = (Get-CimInstance Win32_Process -Filter "Name='language_server.exe'").ProcessId

# 2. 메모리 dump (사용자 권한이면 가능, 사이즈 약 400MB)
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.IO; using System.Diagnostics;
public static class MD {
  [DllImport("Dbghelp.dll")]
  public static extern bool MiniDumpWriteDump(IntPtr h, uint pid, IntPtr file, uint t, IntPtr e, IntPtr u, IntPtr c);
  public static void Dump(int pid, string path) {
    var p = Process.GetProcessById(pid);
    using (var fs = new FileStream(path, FileMode.Create))
      MiniDumpWriteDump(p.Handle, (uint)pid, fs.SafeFileHandle.DangerousGetHandle(), 2, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
  }
}
'@ -Language CSharp
[MD]::Dump($pid, "$env:TEMP\ls.dmp")

# 3. dump 에서 client_id 와 secret 패턴 추출
$bytes = [IO.File]::ReadAllBytes("$env:TEMP\ls.dmp")
$text = -join ($bytes | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { ' ' } })
$ids     = ([regex]::Matches($text, '[0-9]{8,15}-[a-z0-9]{20,40}\.apps\.googleusercontent\.com')).Value | Sort-Object -Unique
$secrets = ([regex]::Matches($text, 'GOCSPX-[A-Za-z0-9_-]{20,40}')).Value | Sort-Object -Unique
$ids
$secrets
```

### 옵션 B — 정적 추출 (`language_server.exe` 바이너리)

Antigravity 가 실행 중이지 않을 때도 동일한 client_id 가 바이너리에 박혀있습니다. 같은 PowerShell regex 를 `C:\Users\<USER>\AppData\Local\Programs\Antigravity\resources\bin\language_server.exe` 에 돌리면 됩니다 (130MB 바이너리라 시간 좀 걸림).

## 파일 작성

추출한 값을 다음 경로에 저장 (디렉터리는 자동 생성):

```
%APPDATA%\ClaudeUsageTray\antigravity-oauth-client.json
```

```json
{
  "client_id": "1071006060591-...apps.googleusercontent.com",
  "client_secret": "GOCSPX-..."
}
```

## 짝 맞는지 검증

저장 후 트레이 앱 새로고침 → "Antigravity" 섹션이 사용자의 plan + 모델별 quota 와 함께 뜨면 성공. 아무 일도 안 나면:

- `gemini:antigravity` 라는 항목이 Windows Credential Manager 에 있는지 (`cmdkey /list`) 확인 — 없으면 Antigravity 로그인부터.
- secret 길이가 35자가 안 되면 boundary 잘못 잡힌 것 (인접한 다른 secret 의 prefix 가 섞임). 가능한 다른 후보로 재시도.

## 왜 코드에 박지 않나

Google 의 secret-scanning 정책상 public repo 에 client_secret 노출 시 자동 revoke 가능성이 있고, Antigravity 의 마이너 업데이트로 client 가 회전될 수도 있어 사용자 PC 단위로 분리해 관리합니다.
