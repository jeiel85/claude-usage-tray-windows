# Google Play 스토어 그래픽

Play Console "기본 스토어 등록정보"에 올리는 그래픽과 문구의 원본입니다(#256). 문구·정책 선언 값과 스크린샷 만드는 법은 [`play-listing.md`](play-listing.md).

- `feature-graphic.svg` → `feature-graphic.png`: 그래픽 이미지(1024×500). 아래 명령에서 창 크기만 `1024,500`.
- `screenshots/`: 휴대전화 스크린샷(1080×1920).
- `icon.svg` → `icon-512.png`: 앱 아이콘(512×512). 런처 아이콘(`app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml`)과 같은 그림입니다 — `ic_background` 위에 `ic_launcher_foreground` 막대 셋, 108 단위 좌표 그대로. 모서리 둥글리기는 Play가 합니다.
- 런처 아이콘을 바꾸면 `icon.svg`도 같이 고치고 PNG를 다시 만든 뒤 Play Console에 다시 올립니다:

```powershell
# 1:1 로 그리도록 배율 1, 512×512 창에서 SVG 를 띄워 캡처
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new --disable-gpu --hide-scrollbars `
  --force-device-scale-factor=1 --window-size=512,512 --screenshot="$PWD\icon-512.png" "file:///$PWD/icon.svg"
```
