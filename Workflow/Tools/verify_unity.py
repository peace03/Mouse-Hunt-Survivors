"""감독형 Unity 검증. 소스·원시 증거·최종 빌드를 남기고 실행 임시물은 정리한다."""
import argparse
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
GATES = ("scope", "compile", "editmode", "playmode", "player_build", "player_smoke")
PLAN = "Workflow/Verification/unity-plan.json"
SUPPORT = "Workflow/Verification/UnitySupport"
EXCLUDED = ("Workflow/Archive/", "Workflow/Runs/", "Workflow/Tasks/")


class InvalidEvidence(ValueError):
    """내용·범위·실행 증거가 검증 조건을 충족하지 않는다."""


def utc():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def digest(path):
    with Path(path).open("rb") as stream:
        return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def load(path):
    def unique_fields(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise InvalidEvidence(f"중복 JSON 필드: {key}")
            result[key] = value
        return result

    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_fields)
    except (OSError, ValueError) as exc:
        raise InvalidEvidence(f"JSON을 읽을 수 없음: {path}: {exc}") from exc


def safe_path(root, name):
    if not isinstance(name, str) or not name or "\\" in name or ":" in name:
        raise InvalidEvidence(f"루트 상대 경로가 아님: {name}")
    path = Path(name)
    if path.is_absolute() or any(part in ("..", ".") for part in name.split("/")):
        raise InvalidEvidence(f"경로 탈출: {name}")
    target = root / path
    if not target.resolve().is_relative_to(root.resolve()):
        raise InvalidEvidence(f"경로 경계 밖: {name}")
    for parent in (target, *target.parents):
        if parent == root:
            break
        if parent.is_symlink() or getattr(parent, "is_junction", lambda: False)():
            raise InvalidEvidence(f"링크 경로는 지원하지 않음: {name}")
    return target


def source_manifest(root):
    paths = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root
    ).decode("utf-8").split("\0")
    files = {}
    for name in sorted(set(paths)):
        if not name or name.startswith(EXCLUDED) or "__pycache__" in name.split("/"):
            continue
        path = safe_path(root, name)
        if path.is_file():
            files[name] = digest(path)
    oid = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root).decode().strip()
    return {"schema_version": "1.0", "git_oid": oid, "files": files}


def validate_contract(data):
    fields = {"schema_version", "example_only", "contract_id", "project_id", "objective", "non_goals",
              "base_oid", "base_manifest_digest", "workflow_release_id", "environment_id", "assurance_level",
              "allowed_write", "protected_write", "acceptance", "required_gates", "human_playtest_required",
              "risk_class", "git_permissions", "limits", "cost_cap_micro_usd", "cost_control"}
    if not isinstance(data, dict) or set(data) != fields:
        raise InvalidEvidence("작업 계약의 필드 누락 또는 미지원 필드")
    if data["schema_version"] != "2.0" or data["example_only"] is not False:
        raise InvalidEvidence("미지원 버전 또는 실행할 수 없는 예제 계약")
    for field in ("contract_id", "project_id", "objective", "workflow_release_id", "environment_id"):
        if not isinstance(data[field], str) or not data[field].strip():
            raise InvalidEvidence(f"계약 필드가 비어 있음: {field}")
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", data["contract_id"]):
        raise InvalidEvidence("contract_id는 영문·숫자·밑줄·하이픈만 사용")
    if not isinstance(data["base_oid"], str) or not re.fullmatch(r"(?:[a-f0-9]{40}|[a-f0-9]{64})", data["base_oid"]):
        raise InvalidEvidence("전체 Git OID가 필요함")
    if not isinstance(data["base_manifest_digest"], str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", data["base_manifest_digest"]):
        raise InvalidEvidence("기준선 SHA-256이 필요함")
    for field in ("allowed_write", "protected_write"):
        if not isinstance(data[field], list) or not data[field]:
            raise InvalidEvidence(f"{field} 목록 필요")
        for path in data[field]:
            safe_path(ROOT, path)
    if data["assurance_level"] != "supervised_local" or data["risk_class"] not in ("R0", "R1", "R2", "R3"):
        raise InvalidEvidence("이 실행기는 supervised_local만 지원")
    if (type(data["human_playtest_required"]) is not bool or not isinstance(data["non_goals"], list)
            or not all(isinstance(item, str) for item in data["non_goals"])):
        raise InvalidEvidence("사람 확인 여부·비목표 형식 오류")
    if (not isinstance(data["required_gates"], list) or not all(isinstance(item, str) for item in data["required_gates"])
            or sorted(data["required_gates"]) != sorted(GATES)):
        raise InvalidEvidence("Unity 실행기는 scope·compile·editmode·playmode·player_build·player_smoke가 모두 필요")
    if not isinstance(data["acceptance"], list) or not data["acceptance"]:
        raise InvalidEvidence("관찰 가능한 수용 조건 필요")
    for item in data["acceptance"]:
        if not isinstance(item, dict) or set(item) != {"id", "expectation", "check_ids"} or not all(item.values()):
            raise InvalidEvidence("수용 조건 필드 오류")
        if (not isinstance(item["id"], str) or not isinstance(item["expectation"], str)
                or not isinstance(item["check_ids"], list) or not all(isinstance(check, str) for check in item["check_ids"])
                or not set(item["check_ids"]).issubset(GATES)):
            raise InvalidEvidence("수용 조건의 check_ids는 실제 gate ID를 사용")
    if len({item["id"] for item in data["acceptance"]}) != len(data["acceptance"]):
        raise InvalidEvidence("중복 수용 조건 ID")
    limits = data["limits"]
    if not isinstance(limits, dict) or set(limits) != {"max_wall_minutes", "max_repair_rounds", "max_infra_retries"}:
        raise InvalidEvidence("실행 한도 누락")
    if any(type(value) is not int or value < 0 for value in limits.values()) or limits["max_wall_minutes"] == 0:
        raise InvalidEvidence("시간은 양수, 재시도 한도는 0 이상의 정수")
    if data["git_permissions"] != [] or data["cost_control"] != "manual_review_before_paid_external_calls":
        raise InvalidEvidence("이 실행기는 Git 쓰기와 유료 외부 호출을 수행하지 않음")
    cost = data["cost_cap_micro_usd"]
    if cost is not None and (type(cost) is not int or cost < 0):
        raise InvalidEvidence("비용 상한은 null 또는 0 이상 정수")


def check_scope(before, after, contract):
    changed = sorted(name for name in set(before) | set(after) if before.get(name) != after.get(name))
    for name in changed:
        if any(fnmatch.fnmatchcase(name.casefold(), rule.casefold()) for rule in contract["protected_write"]):
            raise InvalidEvidence(f"보호 경로 변경: {name}")
        if not any(fnmatch.fnmatchcase(name.casefold(), rule.casefold()) for rule in contract["allowed_write"]):
            raise InvalidEvidence(f"허용 범위 밖 변경: {name}")
    return changed


def read_test_xml(path, expected):
    try:
        root = ET.parse(path).getroot()
        cases = root.findall(".//test-case")
        names = [case.get("fullname") for case in cases]
        if (root.tag != "test-run" or root.get("result") != "Passed" or not cases
                or int(root.get("total", "-1")) != len(cases) or len(set(names)) != len(names)
                or not all(names) or not set(expected).issubset(names)
                or any(case.get("result") != "Passed" for case in cases)):
            raise InvalidEvidence("테스트 0개, 실패/skip, 필수 case 누락 또는 XML 집계 불일치")
        return sorted(names)
    except (OSError, ET.ParseError, ValueError) as exc:
        raise InvalidEvidence(f"유효한 테스트 증거 없음: {path}: {exc}") from exc


def read_bound_report(path, nonce, candidate, stage):
    data = load(path)
    if (not isinstance(data, dict) or data.get("nonce") != nonce or data.get("candidate_digest") != candidate
            or data.get("stage") != stage or data.get("success") is not True):
        raise InvalidEvidence(f"실패 보고 또는 이번 실행·후보와 다른 증거: {path}")
    return data


def tree_files(root):
    return {path.relative_to(root).as_posix(): digest(safe_path(root, path.relative_to(root).as_posix()))
            for path in sorted(root.rglob("*")) if path.is_file()}


def build_files(build):
    for name in ("MidnightMouse.exe", "UnityPlayer.dll", "MidnightMouse_Data/globalgamemanagers"):
        path = build / name
        if not path.is_file() or path.stat().st_size == 0:
            raise InvalidEvidence(f"빌드 산출물 없음: {path}")
    return tree_files(build)


def overall_result(gates, human_required, stop_requested=False):
    if stop_requested:
        return "INCONCLUSIVE", "INCONCLUSIVE", "CANCELLED"
    results = {gate["result"] for gate in gates}
    if results == {"PASS"}:
        return "PASS", "INCONCLUSIVE" if human_required else "PASS", "REVIEW_READY"
    if "FAIL" in results:
        return "FAIL", "FAIL", "FAILED"
    if "BLOCKED_ENV" in results:
        return "BLOCKED_ENV", "BLOCKED_ENV", "BLOCKED_ENV"
    return "INCONCLUSIVE", "INCONCLUSIVE", "FAILED"


def check_lock(root):
    lock = load(root / "workflow.lock.json")
    if lock.get("schema_version") != "1.0" or not isinstance(lock.get("files"), dict):
        raise InvalidEvidence("지원하지 않는 workflow.lock.json")
    required = {"AGENTS.md", "CLAUDE.md", "Workflow/AI_지침.md", "Workflow/Tools/verify_unity.py", PLAN}
    required.update(p.relative_to(root).as_posix() for p in (root / SUPPORT).rglob("*") if p.is_file())
    if not required.issubset(lock["files"]):
        raise InvalidEvidence("lock에서 활성 검증 파일 누락")
    for name, expected in lock["files"].items():
        path = safe_path(root, name)
        if not path.is_file() or digest(path) != expected:
            raise InvalidEvidence(f"활성 버전 불일치: {name}")
    return lock


def cleanup_run_work(directory):
    """이번 실행의 재생성 가능한 폴더만 제거한다. 이전 실행은 탐색하지 않는다."""
    result = {"removed": [], "removed_bytes": 0, "retained": []}
    try:
        if directory.parent != ROOT / "Workflow/Runs":
            raise InvalidEvidence("정리 대상은 Runs 바로 아래의 실행 폴더여야 함")
        safe_path(ROOT, directory.relative_to(ROOT).as_posix())
        for path in directory.glob("*.process.json"):
            process = load(path)
            if not isinstance(process, dict) or (process.get("pid") and type(process.get("exit_code")) is not int):
                raise InvalidEvidence(f"프로세스 종료를 확인할 수 없음: {path.name}")
    except (InvalidEvidence, OSError) as exc:
        result["retained"].append({"path": ".", "reason": str(exc)})
        return result
    for name in ("Project/Library", "Project/Temp", "Project/obj", "PreparationPlayer"):
        try:
            path = safe_path(ROOT, (directory / name).relative_to(ROOT).as_posix())
            if not path.exists():
                continue
            if not path.is_dir():
                raise InvalidEvidence("예상한 임시 폴더 형식이 아님")
            pending, size = [path], 0
            while pending:
                with os.scandir(pending.pop()) as entries:
                    for entry in entries:
                        if entry.is_symlink() or getattr(Path(entry.path), "is_junction", lambda: False)():
                            raise InvalidEvidence(f"임시 폴더 안의 링크 경로: {entry.name}")
                        if entry.is_dir(follow_symlinks=False):
                            pending.append(entry.path)
                        else:
                            size += entry.stat(follow_symlinks=False).st_size
            shutil.rmtree(path)
            result["removed"].append(name)
            result["removed_bytes"] += size
        except (InvalidEvidence, OSError) as exc:
            result["retained"].append({"path": name, "reason": str(exc)})
    return result


def execute(args, env, directory, stage, deadline):
    """자신이 시작한 프로세스만 정리하며 실행 증거를 남긴다."""
    record = {"args": [str(arg) for arg in args], "started_at": utc()}
    process = None
    try:
        with (directory / f"{stage}.stdout.log").open("wb") as stream:
            process = subprocess.Popen(args, env=env, cwd=directory, stdout=stream, stderr=subprocess.STDOUT,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            record["pid"] = process.pid
            try:
                record["exit_code"] = process.wait(timeout=max(0.01, deadline - time.monotonic()))
            except BaseException:
                if process.poll() is None:
                    if os.name == "nt":
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                       capture_output=True, timeout=20, creationflags=subprocess.CREATE_NO_WINDOW)
                    else:
                        process.kill()
                    process.wait(timeout=20)
                raise
        if record["exit_code"] != 0:
            log_path = directory / f"{stage}.log"
            log = log_path.read_text(encoding="utf-8", errors="replace") if log_path.is_file() else ""
            if any(message in log for message in ("Could not establish a connection with the Unity Package Manager",
                                                   "No valid Unity Editor license found", "No valid license found")):
                raise OSError(f"{stage}: Unity 라이선스 또는 Package Manager 환경 장애 (원시 로그 확인)")
            raise InvalidEvidence(f"{stage} 종료 코드 {record['exit_code']} (원시 로그 확인)")
    except subprocess.TimeoutExpired as exc:
        record["timed_out"] = True
        raise InvalidEvidence(f"{stage} 총 실행 시간 한도 초과") from exc
    finally:
        if process is not None:
            record["exit_code"] = process.poll()
        record["ended_at"] = utc()
        save(directory / f"{stage}.process.json", record)


def run(contract_path, editor):
    contract_path = contract_path.resolve()
    if not contract_path.is_relative_to(ROOT / "Workflow/Tasks"):
        raise InvalidEvidence("계약은 Workflow/Tasks/<작업>/contract.json에 보관")
    contract = load(contract_path)
    validate_contract(contract)
    baseline_path = contract_path.with_name("baseline.json")
    if not baseline_path.is_file() or digest(baseline_path) != contract["base_manifest_digest"]:
        raise InvalidEvidence("기준선 파일 누락 또는 계약 digest 불일치")
    baseline = load(baseline_path)
    if (not isinstance(baseline, dict) or set(baseline) != {"schema_version", "git_oid", "files"}
            or baseline["schema_version"] != "1.0" or not isinstance(baseline["files"], dict)):
        raise InvalidEvidence("기준선 manifest 형식 오류")
    for name, value in baseline["files"].items():
        safe_path(ROOT, name)
        if not isinstance(value, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", value):
            raise InvalidEvidence(f"기준선 파일 digest 오류: {name}")
    if baseline["git_oid"] != contract["base_oid"]:
        raise InvalidEvidence("기준선 Git OID 불일치")
    nonce = uuid.uuid4().hex
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + nonce[:8]
    directory = ROOT / "Workflow/Runs" / run_id
    directory.mkdir(parents=True, exist_ok=False)
    report = {"schema_version": "2.0", "run_id": run_id, "contract_id": contract["contract_id"],
              "contract_digest": digest(contract_path), "started_at": utc(), "assurance_level": "supervised_local",
              "human_playtest": "pending" if contract["human_playtest_required"] else "not_required",
              "cost": {"status": "unavailable", "micro_usd": None}, "human_active_minutes": None,
              "repair_rounds": 0, "infra_retries": 0, "stop_requested": False,
              "gates": [{"gate_id": gate, "result": "INCONCLUSIVE", "expected_case_ids": [],
                         "observed_case_ids": [], "evidence_refs": [], "reason": "미실행"} for gate in GATES]}
    save(directory / "contract.json", contract)
    gate = report["gates"][0]
    candidate = None
    project_before = None
    project = directory / "Project"
    started = time.monotonic()
    print(f"실행 기록: {directory}", flush=True)
    try:
        lock = check_lock(ROOT)
        if lock.get("workflow_release_id") != contract["workflow_release_id"] or lock.get("environment_id") != contract["environment_id"]:
            raise InvalidEvidence("계약과 활성 워크플로우·환경 버전 불일치")
        plan = load(ROOT / PLAN)
        plan_fields = {"schema_version", "environment_id", "project_path", "unity_version", "build_target", "scenes", "expected_cases", "smoke_frames", "preparation_writes"}
        if set(plan) != plan_fields or plan["schema_version"] != "1.1" or plan["build_target"] != "StandaloneWindows64":
            raise InvalidEvidence("이 검증 계획은 Windows 64bit v1만 지원")
        if plan["environment_id"] != contract["environment_id"] or plan["project_path"] != "MidnightMouse":
            raise InvalidEvidence("검증 계획의 환경·프로젝트 불일치")
        if not plan["scenes"] or any(not name.startswith("Assets/") or not name.endswith(".unity") for name in plan["scenes"]):
            raise InvalidEvidence("실제 빌드 씬 목록 필요")
        if set(plan["expected_cases"]) != {"editmode", "playmode"} or any(not value for value in plan["expected_cases"].values()):
            raise InvalidEvidence("필수 테스트 목록 필요")
        if type(plan["smoke_frames"]) is not int or not 1 <= plan["smoke_frames"] <= 10000:
            raise InvalidEvidence("시작 확인 프레임 수 오류")
        unity_source = safe_path(ROOT, plan["project_path"])
        version = re.search(r"m_EditorVersion: (\S+)", (unity_source / "ProjectSettings/ProjectVersion.txt").read_text()).group(1)
        if version != plan["unity_version"]:
            raise InvalidEvidence("프로젝트 Editor 버전이 검증 계획과 다름")
        editor = editor or Path(f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe")
        if os.name != "nt" or not editor.is_file():
            raise OSError(f"지정된 Windows Unity 실행 파일 없음: {editor}")
        candidate = source_manifest(ROOT)
        if candidate["git_oid"] != baseline["git_oid"]:
            raise InvalidEvidence("Git base 변경: 새 계약·기준선·run 필요")
        changed = check_scope(baseline["files"], candidate["files"], contract)
        save(directory / "source.json", candidate)
        save(directory / "changes.json", {"changed": changed, "baseline_digest": digest(baseline_path)})
        source_digest = digest(directory / "source.json")
        report.update(source_digest=source_digest, verifier_digest=digest(Path(__file__)),
                      plan_digest=digest(ROOT / PLAN), workflow_release_id=lock["workflow_release_id"], nonce=nonce)
        for name in candidate["files"]:
            if name.startswith("MidnightMouse/"):
                relative = name.removeprefix("MidnightMouse/")
                if relative.split("/")[0] in ("Assets", "Packages", "ProjectSettings"):
                    dest = safe_path(project, relative)
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(safe_path(ROOT, name), dest)
        destination = project / "Assets/WorkflowVerification"
        if destination.exists():
            raise InvalidEvidence("검증 지원 폴더와 프로젝트 에셋 이름 충돌")
        shutil.copytree(ROOT / SUPPORT, destination)
        shutil.copy2(ROOT / (SUPPORT + ".meta"), project / "Assets/WorkflowVerification.meta")
        for scene in plan["scenes"]:
            if not safe_path(project, scene).is_file():
                raise InvalidEvidence(f"빌드 씬 없음: {scene}")
        copied = {name: tree_files(project / name) for name in ("Assets", "Packages", "ProjectSettings")}
        save(directory / "copied-inputs.json", copied)
        save(directory / "plan.json", plan)
        env = {**os.environ, "MH_WORKFLOW_RUN": str(directory), "MH_WORKFLOW_NONCE": nonce,
               "MH_WORKFLOW_CANDIDATE": source_digest, "MH_WORKFLOW_PLAN": str(directory / "plan.json")}
        deadline = started + contract["limits"]["max_wall_minutes"] * 60
        base = [str(editor), "-batchmode", "-projectPath", str(project), "-buildTarget", "StandaloneWindows64"]
        # Unity/URP가 첫 빌드 때 직렬화하는 값은 준비 단계에서 기록하고, 그 뒤의 입력을 동결한다.
        print("시작: prepare (복사본 초기화·준비 빌드)", flush=True)
        gate = report["gates"][1]
        execute(base + ["-quit", "-executeMethod", "MidnightMouse.Workflow.Editor.WorkflowBuild.Prepare",
                        "-logFile", str(directory / "prepare.log")], env, directory, "prepare", deadline)
        proof = read_bound_report(directory / "prepare.json", nonce, source_digest, "prepare")
        if proof.get("unity_version") != version:
            raise InvalidEvidence("준비 단계 Unity 버전 불일치")
        prepared = {name: tree_files(project / name) for name in copied}
        gate = report["gates"][0]
        flatten = lambda data: {f"{folder}/{name}": value for folder, files in data.items() for name, value in files.items()}
        preparation_changes = check_scope(flatten(copied), flatten(prepared),
                                          {"allowed_write": plan["preparation_writes"], "protected_write": []})
        save(directory / "preparation-changes.json", {"changed": preparation_changes,
             "before": {name: flatten(copied).get(name) for name in preparation_changes},
             "after": {name: flatten(prepared).get(name) for name in preparation_changes}})
        project_before = prepared
        save(directory / "project-inputs.json", project_before)
        save(directory / "candidate.json", {"source_digest": source_digest,
                                             "project_inputs_digest": digest(directory / "project-inputs.json")})
        candidate_digest = digest(directory / "candidate.json")
        env["MH_WORKFLOW_CANDIDATE"] = candidate_digest
        report.update(candidate_digest=candidate_digest, preparation_refs=["prepare.json", "prepare.log", "prepare.process.json",
                      "copied-inputs.json", "preparation-changes.json", "project-inputs.json"])
        gate.update(result="PASS", reason="원본 범위와 준비 변경을 확인하고 실제 검증 입력을 동결함",
                    evidence_refs=["changes.json", "source.json", "candidate.json", "project-inputs.json", "preparation-changes.json"])
        for gate in report["gates"][1:]:
            stage = gate["gate_id"]
            print(f"시작: {stage}", flush=True)
            save(directory / "report.json", report)
            if time.monotonic() >= deadline:
                raise InvalidEvidence("총 실행 시간 한도 소진")
            if stage in ("editmode", "playmode"):
                expected = plan["expected_cases"][stage]
                gate["expected_case_ids"] = expected
                args = base + ["-runTests", "-testPlatform", "EditMode" if stage == "editmode" else "PlayMode",
                               "-testResults", str(directory / f"{stage}.xml"), "-logFile", str(directory / f"{stage}.log")]
                execute(args, env, directory, stage, deadline)
                gate["observed_case_ids"] = read_test_xml(directory / f"{stage}.xml", expected)
                gate["evidence_refs"] = [f"{stage}.xml", f"{stage}.log", f"{stage}.process.json"]
            elif stage in ("compile", "player_build"):
                method = "Compile" if stage == "compile" else "Build"
                execute(base + ["-quit", "-executeMethod", f"MidnightMouse.Workflow.Editor.WorkflowBuild.{method}",
                                "-logFile", str(directory / f"{stage}.log")], env, directory, stage, deadline)
                proof = read_bound_report(directory / f"{stage}.json", nonce, candidate_digest, stage)
                if proof.get("unity_version") != version:
                    raise InvalidEvidence("실행한 Unity 버전이 계약과 다름")
                gate["evidence_refs"] = [f"{stage}.json", f"{stage}.log", f"{stage}.process.json"]
                if stage == "player_build":
                    if proof.get("build_target") != plan["build_target"] or proof.get("scenes") != plan["scenes"]:
                        raise InvalidEvidence("실제 빌드 타깃·씬 불일치")
                    artifacts = build_files(directory / "Player")
                    save(directory / "build-manifest.json", artifacts)
                    report["build_digest"] = digest(directory / "build-manifest.json")
                    gate["evidence_refs"].append("build-manifest.json")
            else:
                execute([str(directory / "Player/MidnightMouse.exe"), "-batchmode", "-windowed", "-screen-width", "640",
                         "-screen-height", "360", "-logFile", str(directory / "player_smoke.log"), "--workflow-smoke"],
                        env, directory, stage, deadline)
                proof = read_bound_report(directory / "player_smoke.json", nonce, candidate_digest, stage)
                if (proof.get("scene") != plan["scenes"][0] or proof.get("frames", 0) < plan["smoke_frames"]
                        or proof.get("errors") != 0 or proof.get("graphics_device") in (None, "Null")):
                    raise InvalidEvidence("Player 시작·씬·프레임·그래픽 장치·오류 조건 불충족")
                if build_files(directory / "Player") != load(directory / "build-manifest.json"):
                    raise InvalidEvidence("시작 확인 도중 빌드 파일 변경")
                gate["evidence_refs"] = ["player_smoke.json", "player_smoke.log", "player_smoke.process.json", "build-manifest.json"]
            gate.update(result="PASS", reason="이번 실행의 원시 증거 확인")
            print(f"통과: {stage}", flush=True)
    except (InvalidEvidence, OSError, subprocess.SubprocessError, KeyboardInterrupt) as exc:
        report["stop_requested"] = isinstance(exc, KeyboardInterrupt)
        gate.update(result="BLOCKED_ENV" if isinstance(exc, OSError) else "FAIL", reason=str(exc) or "사용자 중단")
    finally:
        try:
            if candidate is not None and source_manifest(ROOT) != candidate:
                raise InvalidEvidence("검증 중 원본 후보 내용 변경: 기존 결과 무효")
            if project_before is not None:
                after = {name: tree_files(project / name) for name in project_before}
                if after != project_before:
                    save(directory / "project-inputs-after.json", after)
                    raise InvalidEvidence("검증 복사본의 소스·설정 변경: 입력 동일성 불일치")
            if digest(contract_path) != report["contract_digest"]:
                raise InvalidEvidence("실행 중 계약 변경")
            if candidate is not None:
                check_lock(ROOT)
        except (InvalidEvidence, OSError, subprocess.SubprocessError) as exc:
            report["gates"][0].update(result="FAIL", reason=str(exc))
        machine, result, state = overall_result(report["gates"], contract["human_playtest_required"], report["stop_requested"])
        report.update(machine_result=machine, result=result, state=state, ended_at=utc(), elapsed_seconds=round(time.monotonic() - started, 2))
        save(directory / "report.json", report)
        report["storage_cleanup"] = cleanup_run_work(directory)
        save(directory / "report.json", report)
        print(f"기계 검사: {machine}; 납품 판정: {result}; 상태: {state}", flush=True)
        for item in report["gates"]:
            print(f"  {item['gate_id']}: {item['result']} — {item['reason']}", flush=True)
        for item in report["storage_cleanup"]["retained"]:
            print(f"임시물 정리 미완료: {item['path']} — {item['reason']}", flush=True)
        print(f"보고서: {directory / 'report.json'}", flush=True)
    return 0 if report["machine_result"] == "PASS" else 1


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--snapshot", type=Path, help="작업 시작 전 baseline.json 기록")
    mode.add_argument("--contract", type=Path, help="Workflow/Tasks/<작업>/contract.json")
    parser.add_argument("--editor", type=Path, help="설치된 정확한 Unity.exe 경로")
    args = parser.parse_args()
    try:
        if args.snapshot:
            path = args.snapshot.resolve()
            if not path.is_relative_to(ROOT / "Workflow/Tasks") or path.exists():
                raise InvalidEvidence("새 기준선은 Workflow/Tasks 아래 존재하지 않는 파일에만 기록")
            save(path, source_manifest(ROOT))
            print(f"기준선: {path}\nbase_manifest_digest: {digest(path)}")
            return 0
        return run(args.contract, args.editor)
    except (InvalidEvidence, OSError, subprocess.SubprocessError) as exc:
        print(f"검증 시작 거부: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
