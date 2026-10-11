# Agent Mascot for Windows

Codex와 Claude Code의 로컬 기록, Gemini CLI·Qwen Code·Copilot CLI·OpenCode·Cursor·Antigravity·Kimi Code·Windsurf/Devin Desktop·Kiro CLI V3·Amp의 Hook/플러그인 이벤트를 읽어 작업 상태를 알려주는 Windows 10/11 x64 트레이 앱입니다. 작업을 끝내면 캐릭터가 팝업과 소리로 알려줍니다.

![메인 창](docs/images/main-window.png)

## 캐릭터가 알려주는 상태

<p align="center">
  <img src="docs/images/state-running.png" width="150" alt="작업 중">
  <img src="docs/images/state-attention.png" width="150" alt="확인/승인 필요">
  <img src="docs/images/state-completed.png" width="150" alt="응답 완료">
  <img src="docs/images/state-failed.png" width="150" alt="실패">
</p>

| 상태 | 캐릭터가 보여주는 것 |
|---|---|
| 작업 중 | 짧은 팝업 |
| 확인/승인 필요 | 계속 표시 · 클릭해서 확인 |
| 응답 완료 | 축하 팝업 + 소리 |
| 실패 | 경고 팝업 + 소리 |
| 중단 | 잠깐 표시 |

## 왜 쓰나요

- **새 작업을 시작할 필요가 없습니다.** 이미 쓰고 있는 Codex/Claude의 진행 상황을 로컬 기록에서 읽어 자동으로 표시합니다.
- **여러 작업을 하나의 캐릭터로** — 작업이 여러 개여도 우선순위에 따라 대표 상태 하나만 보여줍니다.
- **원하는 캐릭터로 교체** — 상태별 GIF·PNG·WebP 이미지와 WAV·MP3 소리를 등록 창에서 지정합니다. 여러 소리를 넣으면 매번 무작위로 재생합니다.
- **여러 마스코트 동시 표시** — 같은 알림을 여러 마스코트가 한 번에 나타나고, 각자 크기·위치·볼륨·속도를 따로 가집니다.
- **클릭하면 원래 도구로** — 팝업을 누르면 Codex/Claude 창을 앞으로 가져옵니다.
- **가벼운 트레이 앱** — X를 눌러도 트레이에서 계속 실행되며, 작업 표시줄에 자리를 차지하지 않습니다.

## 30초 시작하기

1. [최신 릴리스](https://github.com/Obsihill/codex-mascot/releases/latest)에서 `AgentMascot-<version>-Windows-x64.zip`을 내려받습니다(.NET 런타임 포함).
2. 원하는 폴더에 풀고 **AgentMascot.exe**를 실행합니다. DLL과 `assets`, `integration` 폴더를 함께 두세요.
3. 메인 화면 **프로젝트 선택**에서 감시할 폴더·채팅을 체크하고 **확인**을 누릅니다.
4. 평소처럼 Codex나 Claude Code로 작업하세요. 새 이벤트를 3초 이내에 감지합니다.

처음 실행하면 Windows 표시 언어에 따라 한국어 또는 영어로 열립니다. 완전히 종료하려면 Windows 알림 영역의 마스코트 아이콘을 우클릭해 **종료**를 선택하세요.

## 캐릭터 바꾸기

마스코트는 대표 이미지와 6개 상태 이미지·소리를 담은 폴더형 묶음입니다. `library/mascot-<ID>/`에 저장되며, 메인 화면 오른쪽 설정에서 등록·편집합니다.

```text
AgentMascot/
  AgentMascot.exe
  assets/          기본 이미지·소리
  config/          자동 생성되는 개인 설정
  library/         등록한 마스코트 묶음
```

상태별로 **재생/반복·볼륨(최대 200%)·속도·이미지 재생시간·위치·크기**를 조절할 수 있고, **테스트** 버튼으로 팝업과 소리를 바로 확인할 수 있습니다. 기본 제공 마스코트는 **Base mascot**과 **MasCat**(기본 활성)이며, MasCat에는 야옹 소리 7개가 들어 있습니다.

## 설정

![설정](docs/images/settings.png)

- **일반** — Windows 로그인 시 시작, 항상 위, 대기 중 표시
- **알림** — 팝업 클릭 시 Codex 창 열기, 알림 유지
- **연결** — Codex/Claude 데이터 폴더, 감시 상태·프로젝트 선택
- **언어** — 한국어 / English 즉시 전환

### 추가 에이전트 연결

연결 설정에서 각 도구의 데이터 폴더를 선택하면 Hook 또는 로컬 플러그인을 자동 등록합니다. 빈 경로는 해당 도구를 감시하지 않습니다. Gemini CLI는 `GEMINI_CLI_HOME`(기본 `~/.gemini`), Qwen Code는 `QWEN_HOME`(기본 `~/.qwen`), Copilot CLI는 `COPILOT_HOME`(기본 `~/.copilot`), OpenCode는 `OPENCODE_CONFIG_DIR`로 지정한 설정 폴더를 선택하세요. Cursor는 사용자 Hook 폴더 `~/.cursor`, Antigravity는 `~/.gemini`, Kimi Code는 `~/.kimi-code`, Windsurf/Devin Desktop은 `~/.codeium/windsurf`, Kiro CLI V3는 `~/.kiro`, Amp는 로컬 설정 폴더 `~/.config/amp`(또는 `XDG_CONFIG_HOME/amp`)를 선택합니다. 설정 파일의 기존 항목은 보존하고 갱신 전 백업합니다.

이 도구들은 Hook/플러그인을 등록한 **이후의 이벤트**부터 감시합니다. 기록 형식이 Codex/Claude와 달라 과거 채팅은 자동으로 채우지 않으며, 수신된 채팅이 감시 프로젝트 선택창에 나타납니다. Antigravity는 첫 모델 호출과 완전히 대기 상태가 된 완료 이벤트를 사용하고, Kimi Code는 시작·승인 요청/결과·완료·실패·중단 이벤트를 사용합니다. Windsurf/Devin Desktop은 질문 제출·응답 완료만 감지합니다. Kiro CLI V3는 사용자 메시지 제출·작업 완료·세션 종료를 감지합니다. Amp 로컬 플러그인은 작업 시작·완료·실패·취소를 감지합니다. Kiro의 전역 Hook은 CLI V3에서 지원하며 IDE 전역 연동을 뜻하지 않습니다. Amp는 이 컴퓨터에서 실행되는 로컬 플러그인만 감시합니다. Cursor와 Antigravity, Windsurf/Devin, Kiro, Amp는 승인 대기 전용 Hook을 연결하지 않았습니다. CLI 전용 도구의 알림을 클릭하면 Agent Mascot 창이 열립니다. 임의의 터미널이나 Codex 창을 대신 활성화하지 않습니다. Cursor 완료 알림을 클릭하면 Cursor 창을 엽니다. 계정 로그인과 실제 알림 검증은 각 도구에서 별도로 진행해야 합니다.

DeepSeek는 독립 앱 Hook이 아닌 다른 CLI의 모델로 사용할 때 해당 CLI의 연동으로 감시할 수 있습니다. Aider는 안정적인 공식 작업 생명주기 Hook을 확인할 수 없어 자동 감시 대상에 넣지 않았습니다.

## 문서·빌드

- 상세 동작, 상태 판정 기준, Hook 설정, 진단 방법은 [사용 가이드](docs/GUIDE.md)에 있습니다.
- 빌드: `.NET SDK 9.0.306` 이상, .NET 8 WPF 대상

```powershell
dotnet build CodexMascot.sln -c Release
dotnet run --project tests/CodexMascot.Core.Tests -c Release
dotnet run --project tests/CodexMascot.App.Tests -c Release
dotnet publish src/CodexMascot.App -c Release -r win-x64 --self-contained true -o Release
```

GitHub Actions가 push·tag마다 테스트와 self-contained Windows 빌드를 수행합니다.

## 라이선스

MIT License. 외부 라이브러리 조건은 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)를 참고하세요.
