# Unity 평가용 프로토타입

2026-10-20 요청에 따라 「한밤의 쥐」의 PC 1스테이지를 구현했습니다. 원문 기획의 장기 범위는 보존하고, 이번 평가에서 실행할 수 있는 범위는 [작업 설계](../Tasks/2026-10-20-unity-assessment/설계.md)에 정리했습니다.

## 실행과 조작

Unity 6000.6.0f1에서 `MidnightMouse/Assets/Scenes/SampleScene.unity`를 엽니다. 시작 화면의 버튼 또는 Enter로 게임을 시작합니다.

| 조작 | 기능 |
|---|---|
| WASD | 카메라 기준 이동 |
| Ctrl / Shift | 살금살금 / 달리기 |
| Space | 대시 |
| 마우스 왼쪽 버튼 | 커서 위치에 치즈 미끼 던지기 |
| E | 가까운 컵 또는 청소기 작동 |
| Esc | 가장 위의 설정·도움말·종료 확인을 닫기. 열린 창이 없으면 일시정지 / 재개 |
| 1 / 2 / 3 또는 버튼 | 성장 선택 |

300초를 생존하면 승리하고 하트가 0이 되면 패배합니다. 치즈를 모으면 성장 선택이 열립니다. 소음은 고양이의 조사·추적에 영향을 주며, 컵과 청소기는 재사용 대기 시간 동안 다시 작동하지 않습니다. 일시정지·성장 선택·결과 화면에서는 게임 시간이 멈춥니다.

제목과 일시정지 메뉴에 **설정**과 **게임 종료**가 있습니다. 설정은 전체 음량 0~100%와 창 모드/전체 화면(테두리 없음)을 즉시 적용하고, 창을 닫을 때 저장해 다음 실행에 복원합니다. 종료 확인은 취소가 기본 선택이며 확인하면 앱이 닫힙니다. 진행 중인 판은 저장하지 않습니다. 설정 창에서는 뒤쪽 메뉴의 키보드·포인터 조작을 차단합니다.

## 구현 위치

- `Assets/_Project/Assessment/Runtime`: 규칙, 입력, 고양이, 함정, 보상, UI와 효과음.
- `Assets/_Project/Assessment/Environment`: 프리팹, 재질, 길찾기 데이터와 씬 생성기.
- `Assets/_Project/Assessment/Environment/AssessmentRules.asset`: 조절 가능한 게임 수치.
- `Assets/_Project/Assessment/Tests`: 순수 규칙과 실제 씬 연결 검사.
- `Assets/_Project/Assessment/UI`: Noto Sans KR 글꼴과 OFL 라이선스.

기존 쥐·컵·로봇 청소기 FBX와 재질은 수정하지 않고 프리팹 안에서 사용합니다. 기존 미리보기 씬은 `Assets/_Project/Assessment/Environment/Scenes/OriginalPreview.unity`에 보존했습니다. TextMeshPro 기본 리소스는 설치된 Unity UI 패키지의 원본 GUID를 유지해 가져왔습니다.

## 검증과 제출

[작업 계약 r2](../Tasks/2026-10-20-unity-assessment-r2/contract.json)의 고정 검증으로 컴파일·EditMode·PlayMode·Windows 빌드·Player 시작을 확인합니다. 실제 결과와 원시 증거는 해당 실행 폴더 및 작업 보고서에 기록합니다. 이 문서는 검증 성공을 미리 선언하지 않습니다.

설정·종료 추가는 [후속 계약](../Tasks/2026-10-20-settings-exit/contract.json)으로 새 검증과 제출본을 만듭니다. `GameSettings`의 PlayerPrefs 두 키만 사용하며 기존 저장 데이터 전체를 삭제하지 않습니다.

`AssessmentCapture`는 개발 빌드에서 명시적인 명령행 인자가 있을 때만 동작하는 시연 기록기입니다. 일반 실행에서는 사용자 입력을 받습니다. 자동 입력 영상은 사람 플레이 평가를 대신하지 않습니다. 외부 업로드·커밋·push는 이번 요청에 포함하지 않았습니다.
