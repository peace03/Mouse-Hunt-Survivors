---
name: verify-midnight-mouse
description: MouseHuntSurvivors의 MidnightMouse Unity 프로젝트에서 작업 계약에 따른 컴파일, EditMode, PlayMode, Windows 빌드와 Player 시작 확인을 실행하고 증거를 보고한다.
---

# 한밤의 쥐 검증

저장소 루트의 [Workflow/검증.md](../../../Workflow/검증.md)를 읽는다. 이 스킬은 합격 기준을 바꾸지 않고 고정 검증기를 실행한다.

1. 작업 계약과 수정 전 baseline.json, 현재 사용자 변경, Unity 버전을 확인한다. 게임 변경 전에 기준선이 없다면 이번 후보로 과거 기준선을 꾸미지 않는다.
2. 실제 계약으로 `python -B Workflow/Tools/verify_unity.py --contract Workflow/Tasks/<task-id>/contract.json`을 실행한다. `example_only: true`는 실행할 수 없다.
3. 출력한 Runs 폴더의 report.json과 로그·XML·빌드 manifest를 확인한다. 실패를 통과시키려고 필수 case·검증기·workflow.lock.json을 고치지 않는다.
4. 허용 범위의 제품 결함은 고치고 한도 안에서 새 run으로 재검증한다. 첫 실패를 보존한다. 환경 장애와 한도 소진은 원인·재개 조건을 보고한다.
5. 기계 검사·실제 Player 시작·사람 플레이를 구분하고 동일 build_digest의 사람 확인 방법을 전달한다. 자동 시작 확인을 게임 조작·재미 검증으로 표현하지 않는다.

실행기는 소스 사본을 사용한다. 현재 MCP가 다른 프로젝트에 연결되어 있어도 그 Editor를 조작하지 않는다. 이 스킬을 읽었다는 이유로 Git 쓰기·배포·유료 호출·자동화 예약을 수행하지 않는다.
