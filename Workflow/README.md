# 개발·자기개선 워크플로우

「한밤의 쥐」는 **작업 계약 → 구현·검증 → 사람 플레이 → 전달 → 실행 기록에서 개선점 선택** 순서로 개발합니다. [운영 지침](AI_지침.md)이 현재 작업 기준입니다.

## 바로 사용할 절차

| 할 일 | 시작점 | 남길 결과 |
|---|---|---|
| 게임 기능 구현·수정 | 기획 확인 → [작업계약](Templates/작업계약.json) | 계약, 기준선, 변경, 실행 보고 |
| Unity 자동 검증 | [검증 방법](검증.md) | 컴파일·테스트 XML·빌드·시작 확인·후보 해시 |
| 게임 의도 확인 | [플레이 확인](Templates/플레이확인.md) | 플레이한 동일 빌드의 판단 |
| 절차 개선 | [자기개선](자기개선.md) | 관측, 별도 후보, 비교, 도입·복구 기록 |
| 기획 정리 | [노트](Templates/노트.md)·[기획서](Templates/기획서.md) | 필요한 D-번호와 기획 변경 이력 |

AI 종류에 따른 고정 인계는 없습니다. 이미 허가된 작업은 조사부터 보고까지 이어서 수행합니다. 사용자가 정한 게임 목표·권한·수용 기준이 경계입니다.

[보관 규칙](AI_지침.md#6-보관과-정리)은 현재 원본·최신 결과·필수 근거를 남기는 방식입니다. 단순 조회는 채팅으로 끝내고 Git에 있는 수정 전 파일을 별도 백업하지 않습니다. 새 검증 실행의 캐시·준비 빌드는 종료 후 자동 정리하며, 이번 작업의 프레임·미리보기·중복본은 최종 결과 확인 뒤 정리합니다. 기존 자료와 과제별 최신 제출본을 소급해 삭제하지 않습니다.

## 경로와 원본

| 정보 | 원본 |
|---|---|
| 현재 운영 규칙 | [AI_지침.md](AI_지침.md) |
| 전체 v2 명세 | [게임 개발 설계서](../자기개선%20워크플로우/01_unity_ai_game_development_workflow_v2.md), [시스템 개선 설계서](../자기개선%20워크플로우/02_safe_self_improving_ai_workflow_v2.md) |
| 설명형 가이드 | [원문 HTML](../자기개선%20워크플로우/03_workflow_build_guide.html) |
| 게임 기획·범위·일정 | [Design](Design/README.md), [프로젝트](Design/00_프로젝트.md) |
| 기획 결정과 과거 생각 | [Decisions.md](Decisions.md), `Notes/` |
| 구현 규칙·사용법 | [코드컨벤션](Rules/코드컨벤션.md), `Features/` |
| 작업 계약·수동 기록 | [Tasks](Tasks/README.md) |
| 자동 실행의 원시 증거 | `Runs/<run-id>/` (로컬 생성물) |
| 개선 후보·도입 기록 | [Improvements](Improvements/README.md) |
| 운영 파일 버전 | [workflow.lock.json](../workflow.lock.json) |
| 프로젝트 검증 스킬 | [verify-midnight-mouse](../.agents/skills/verify-midnight-mouse/SKILL.md) |
| 기획 반영 대기·미결 사항 | [현황.md](현황.md) (자동 생성) |

설계서의 `game-project/`는 저장소 루트, Unity 소스는 `MidnightMouse/`에 대응합니다. `tasks/`, `runs/`, `scripts/verify-unity.ps1`은 각각 `Workflow/Tasks/`, `Workflow/Runs/`, `Workflow/Tools/verify_unity.py`를 사용합니다. 설치된 Python 표준 라이브러리를 사용하므로 별도 PowerShell 래퍼나 패키지가 필요하지 않습니다.

개선 후보는 비교하는 동안 저장소 안의 비활성 폴더에 둡니다. 적용 후에는 활성 파일·검증 결과·Git 복구 위치를 남기고 중복 후보를 정리합니다. 별도 `workflow-lab` 저장소·계정 격리·상태 서비스는 실제 필요와 권한이 정해지면 분리합니다. 원문은 보존하고 이 문서가 프로젝트별 경로 대응을 설명합니다.

## 실행 명령

저장소 루트에서 실행합니다.

```powershell
python -B Workflow/Tools/check.py --write
python -B -m unittest discover -s Workflow/Tools -p "test_*.py"
python -B Workflow/Tools/verify_unity.py --contract Workflow/Tasks/<task-id>/contract.json
```

마지막 명령의 `<task-id>`는 실제 작업 ID로 바꿉니다. [검증 방법](검증.md)에 따라 현재 운영 버전에 맞는 새 기준선·계약을 만듭니다. 교체 당시의 구축 계약과 과거 결과는 이력이며 현재 변경에 맞춰 덮어쓰지 않습니다.

## 요청 예

- “기본조작 기획을 기준으로 이동을 구현해 줘. 작업 범위·수용 조건을 정리하고 검증까지 진행해 줘.”
- “이 계약의 Unity 검증을 실행하고, 사람이 확인할 항목을 알려 줘.”
- “최근 실행 기록에서 반복 손실 하나를 찾아 개선 후보와 비교 계획을 만들어 줘.”
- “이 비교 보고서의 후보를 정해진 작업군에 적용해 줘. 기준 버전과 복구 기록을 남겨 줘.”

원문 가이드의 예제 요청을 읽었다는 사실만으로 실제 작업이나 권한이 추가되지는 않습니다. 자동 빌드 성공과 사람이 확인한 게임 완성을 구분합니다.
