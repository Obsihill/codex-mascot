# Codex Mascot for Windows — 0.2

Windows 10/11 x64용 캐릭터 작업 알림 앱입니다. Codex가 이미 실행 중인 프로젝트 작업을 로컬 기록에서 읽습니다. 새 Codex 작업을 시작할 필요가 없습니다.

## 실행

배포 ZIP 전체를 원하는 폴더에 풀고 **CodexMascot.App.exe**를 실행하세요. DLL/assets/integration 폴더를 EXE와 함께 두세요. 배포판은 .NET 런타임을 포함합니다.

실행 시 기본 Codex 데이터 폴더를 자동 감시합니다. 목록에는 최근 100개 기록 중 일반 작업을 표시하며 서브에이전트는 제외합니다.

1. **데스크톱 작업 감시** 탭에서 데이터 폴더가 실제 사용자 계정의 `.codex`인지 확인하세요. `CODEX_HOME` 환경 변수도 인식합니다.
2. 모든 프로젝트를 감시하거나 프로젝트 폴더를 선택한 다음 **감시 시작 / 새로 고침**을 누르세요.
3. 평소처럼 데스크톱 Codex 또는 CLI에서 작업하세요. 새 파일은 약 3초 이내 발견하고, 알려진 파일의 새 이벤트는 약 650ms 간격으로 확인합니다. Codex가 디스크에 기록하는 지연은 별도입니다.
4. 이미지·사운드 테스트는 상단 버튼에서 실행하세요. 테스트는 실제 작업을 만들지 않습니다.
5. X 버튼은 앱 종료, **트레이로 숨기기**는 감시 유지입니다. 트레이에서 창 표시/설정/캐릭터 숨기기/종료를 할 수 있습니다.

## 상태의 의미

- **작업 중**: 실제 `task_started` 또는 진행 이벤트를 관찰했습니다.
- **응답 완료**: `task_complete` / Hook `Stop`을 관찰했습니다. 요청한 목표 달성이나 코드 검증 성공을 보증하는 뜻은 아닙니다.
- **중단**: `turn_aborted` / Hook `Interrupt`를 관찰했습니다.
- **확인/승인 필요**: Hook 또는 앱이 시작한 app-server의 요청입니다. 외부 데스크톱 작업의 승인/답변은 원래 Codex에서 합니다.
- **실패**: 앱이 시작한 작업의 명시적인 실패 이벤트입니다. 도구 경고를 전체 작업 실패로 판단하지 않습니다.
- **상태 확인 필요**: 진행 중이던 기록이 5분간 갱신되지 않았거나 연결이 끊겼습니다. 느린 작업도 여기에 포함될 수 있으며 완료로 추측하지 않습니다.
- **대기 중**: 활성 작업과 미확인 결과가 없습니다.

우선순위는 승인/질문 → 실패 → 미확인 응답 완료 → 미확인 중단 → 작업 중 → 확인 필요 → 대기입니다.
같은 집계 상태가 유지되는 동안 팝업·소리를 반복하지 않습니다. 오래된 결과는 앱 시작 시 알림을 띄우지 않습니다.
결과 확인 버튼/캐릭터 클릭/닫기는 미확인 결과를 해제하지만 **승인 요청을 승인하거나 지우지 않습니다**.

## 승인·질문 추가 알림: Hooks (선택)

기록 감시만으로 시작/응답 종료/중단을 사용할 수 있습니다. 세부 승인 알림이 필요하면:

1. **추가 알림 설정 (Hooks)**을 누릅니다.
2. 지정한 Codex 데이터 폴더의 `hooks.json`에 Mascot 전달 스크립트가 병합됩니다. 기존 설정은 보존하고 `.mascot-backup-날짜` 파일을 만듭니다.
3. Codex CLI의 `/hooks`에서 Mascot Hook 내용을 검토하고 신뢰하도록 설정합니다. 앱은 신뢰 검토를 우회하지 않습니다. 기존 작업은 다시 열어야 설정이 적용될 수 있습니다.
4. Codex에서 새 메시지를 보내고 Mascot의 **Hook 수신 확인** 문구를 확인합니다. 설정 저장과 실제 이벤트 수신은 다릅니다.

`UserPromptSubmit / PermissionRequest / PreToolUse(request_user_input) / PostToolUse / Stop / Interrupt / SessionEnd`를 처리합니다.
Hook은 승인 정책이나 프롬프트를 변경하지 않으며 실패해도 Codex를 막지 않습니다. 원문 프롬프트·명령·응답은 저장하지 않고 작업 ID/작업 폴더/이벤트명/시각/도구명만 전달합니다.
전달 파일 위치: `%LOCALAPPDATA%/CodexMascot/events`. 앱이 꺼져 있던 기간의 이벤트는 다음 실행에서 재알림하지 않습니다.
Hook 설정 해제는 Mascot가 추가한 항목만 제거합니다.

**중요한 범위:** JSONL 기록 형식은 공식적으로 안정된 외부 API가 아닙니다. Codex 업데이트에 따라 어댑터 수정이 필요할 수 있습니다. 클라우드·다른 컴퓨터의 작업, 이 PC에 기록되지 않는 작업, 기록되지 않는 모든 승인/실패를 완전히 감시한다고 보장하지 않습니다. Hook이 전달되지 않으면 해당 승인 상태를 추측하지 않습니다.
작업 식별자와 Hook의 세션 식별자가 다른 Codex 버전에서는 매핑을 추가로 조정해야 할 수 있습니다.

## 캐릭터·사운드·위치

기본 위치는 **기본 모니터 오른쪽 아래**입니다. 설정에서 모니터와 모서리/중앙을 선택하거나 **위치 표시 · 드래그**로 직접 배치할 수 있습니다. 위치는 자동 저장되고 모니터가 없어지면 기본 모니터로 복구합니다.

- GIF, PNG, WebP (투명 배경 지원). GIF/WebP 애니메이션은 프레임별 시간을 반영합니다.
- 정적 이미지의 스프라이트 시트: 가로/세로 칸과 칸당 시간을 설정합니다. 기본 1×1.
- WAV/MP3. 전체 볼륨×상태별 볼륨을 사용하며 이전 소리는 새 소리가 시작될 때 멈춥니다.
- 소리만/이미지만/동시/전체 상태 테스트, 테스트 중지, 이미지 미리보기.
- 테스트도 음소거 설정을 따릅니다. 화면의 재생 시작은 미디어 플레이어가 파일을 연 것을 뜻하며 실제 스피커 소리는 직접 확인하세요.
- 이미지 파일 선택 시 assets로 복사하고 즉시 미리봅니다. 원본을 삭제해도 복사본을 사용합니다.
- 이미지 누락은 상태별 기본 PNG, 디코딩 실패는 내장 기호로 대체합니다.
- 테마 ZIP 내보내기/가져오기, 기본 테마 복구.
- 크기/항상 위/클릭 통과/대기 캐릭터/Windows 로그인 시 시작.

```text
CodexMascot/
  CodexMascot.App.exe
  assets/images/idle.png, running.png, attention.png, completed.png, failed.png, interrupted.png
  assets/sounds/attention.wav, completed.wav, failed.wav
  config/mascot.json                  ← 첫 실행 시 생성/자동 저장
  config/connection-status.json       ← 연결·작업 상태 진단
  config/events.jsonl                 ← 원문 없는 상태 전환 기록
  integration/Send-MascotEvent.ps1
```

설치 폴더에 쓰기 권한이 없으면 `%LOCALAPPDATA%/CodexMascot/data`에 설정과 에셋을 저장합니다. **assets 폴더 열기**와 **수신 진단 열기**는 실제 사용 중인 폴더를 엽니다.

## 앱에서 새 작업 실행 (선택)

두 번째 탭에서 프로젝트·프롬프트·모델·권한 모드를 선택합니다.
- app-server: 승인/거절 및 질문 답변 지원. 해당 앱이 실행한 서버의 이벤트만 구독합니다.
- CLI JSON: 선택한 샌드박스 안에서 승인 대화 없이 실행합니다. 거절된 동작의 권한을 자동으로 넓히지 않습니다.
- app-server 실패 시 CLI로 자동 재실행하지 않으므로 중복 작업을 만들지 않습니다.
- 중단 버튼은 이 앱이 시작한 작업만 제어합니다. 기존 데스크톱 작업에는 `thread/resume`을 호출하지 않습니다.

GUI에서 PATH를 못 찾는 상황을 고려해 데스크톱 Codex 설치 폴더를 탐색합니다. 찾지 못하면 **EXE 선택**으로 `codex.exe`를 지정하세요.

## 검증 및 빌드

.NET SDK 9.0.306 이상 안정 버전, target .NET 8 WPF.

```powershell
dotnet build CodexMascot.sln -c Release
dotnet run --project tests/CodexMascot.Core.Tests -c Release
dotnet run --project tests/CodexMascot.App.Tests -c Release
dotnet publish src/CodexMascot.App -c Release -r win-x64 --self-contained true -o Release
```

Core 테스트는 중복/지연 이벤트, 여러 승인, 우선순위, 초기 기록 복원, JSONL 부분 쓰기/잘림, 실제 형식의 파일 추가 이벤트를 확인합니다.
App 테스트는 GIF 프레임 시간, WebP, 스프라이트, 테마, 손상 JSON, Hook 설정 병합/해제/전달, 숫자형 승인 ID, 프로세스 종료 시 요청 해제를 확인합니다.
실제 데스크톱 기록을 읽는 감시와 실행 화면을 확인했습니다. 실제 Codex에서 신뢰된 Hook을 발신하는 종단 검증과 다양한 DPI/모니터 하드웨어 조합은 별도입니다.

사용 라이브러리 조건은 THIRD-PARTY-NOTICES.txt를 참고하세요.
공식 문서: [Hooks](https://learn.chatgpt.com/docs/hooks), [App Server](https://learn.chatgpt.com/docs/app-server).
