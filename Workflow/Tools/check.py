"""워크플로우 문서 검사·생성 스크립트.

사용법 (Workflow/ 에서):
    python Tools/check.py          검사만. 문제가 있거나 생성물이 낡았으면 종료 코드 1
    python Tools/check.py --write  생성물(기능 표, 현황.md)까지 다시 만든다

표준 라이브러리만 쓴다.
"""
import os
import re
import sys
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent.parent  # Workflow/
DESIGN = ROOT / "Design"
SKIP_DIRS = {"_원문", "_제외", "images", "Archive", "Runs", "candidate"}
STATUSES = {"초안", "확정", "구현 중", "완료", "보류"}
D_RE = re.compile(r"D-(\d{3,})")
TABLE_START, TABLE_END = "<!-- 기능 표 시작 -->", "<!-- 기능 표 끝 -->"


def read(p):
    return p.read_text(encoding="utf-8")


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def docs():
    """활성 문서와 저장소 시작 지침. 실행 사본과 과거 증거는 제외한다."""
    out = []
    for directory, folders, files in os.walk(ROOT):
        folders[:] = [name for name in folders if name not in SKIP_DIRS]
        out.extend(Path(directory) / name for name in files if name.endswith(".md"))
    out.extend(ROOT.parent / name for name in ("AGENTS.md", "CLAUDE.md", "README.md") if (ROOT.parent / name).is_file())
    return sorted(out)


def feature_designs():
    """Design/<분류>/<기능>.md 만 기획서로 본다."""
    return [p for p in docs() if p.parent.parent == DESIGN]


def strip_code(text):
    # 코드 블록·인라인 코드 안의 예시 링크·번호는 검사하지 않는다
    text = re.sub(r"```.*?```", "", text, flags=re.S)
    return re.sub(r"`[^`\n]*`", "", text)


def links(text):
    """[글](대상) 의 대상을 괄호 짝을 맞춰 찾는다 (파일명에 괄호가 있어도 됨)."""
    out = []
    for m in re.finditer(r"\]\(", text):
        i, depth = m.end(), 1
        while i < len(text) and depth:
            depth += {"(": 1, ")": -1}.get(text[i], 0)
            i += 1
        if depth == 0:
            out.append(text[m.end():i - 1].strip())
    return out


def section(text, name):
    """'## N. 이름' 제목 아래 본문 (다음 ## 전까지)."""
    m = re.search(rf"^##\s+(?:\d+\.\s*)?{re.escape(name)}\s*$(.*?)(?=^##\s|\Z)", text, re.M | re.S)
    return m.group(1) if m else None


def max_d(text):
    nums = [int(n) for n in D_RE.findall(text or "")]
    return max(nums) if nums else 0


def head_field(text, name):
    m = re.search(rf"\*\*{name}:\*\*\s*(.*)", text)
    return m.group(1).strip() if m else None


def parse_decisions():
    """Decisions.md 표 → {번호: (반영 문서 칸, 상태)}"""
    out = {}
    for line in read(ROOT / "Decisions.md").splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) >= 6 and re.fullmatch(r"D-\d{3,}", cells[0]):
            out[int(cells[0][2:])] = (cells[4], cells[5])
    return out


def resolve(src, target):
    return (src.parent / unquote(target.split("#")[0])).resolve()


def is_local(target):
    return target and not re.match(r"[a-z]+:", target) and not target.startswith("#")


def check(decisions):
    problems = []
    for p in docs():
        text = strip_code(read(p))
        # 1) 깨진 상대 링크 (Templates 예시 링크는 제외)
        if "Templates" not in p.parts:
            for t in links(text):
                if is_local(t) and not resolve(p, t).exists():
                    problems.append(f"{rel(p)}: 깨진 링크 {t}")
            # 2) 결정 로그에 없는 D-번호
            for n in sorted({int(n) for n in D_RE.findall(text)}):
                if n not in decisions:
                    problems.append(f"{rel(p)}: 결정 로그에 없는 D-{n:03d}")
    # 3) 유효 결정인데 반영 문서 없음
    for n, (where, status) in decisions.items():
        if status.startswith("유효") and not links(where):
            problems.append(f"Decisions.md: D-{n:03d} 반영 문서 비어 있음")
    # 4) 기획서 머리
    for p in feature_designs():
        text = read(p)
        status = head_field(text, "상태")
        if not status:
            problems.append(f"{rel(p)}: 머리에 상태 없음")
        elif status not in STATUSES:
            problems.append(f"{rel(p)}: 상태 값 '{status}' 은 목록 밖 ({' / '.join(sorted(STATUSES))})")
        if not D_RE.search(head_field(text, "관련 결정") or ""):
            problems.append(f"{rel(p)}: 머리에 관련 결정 없음")
    return problems


def feature_table():
    rows = ["| 분류 | 기능 | 상태 | 마지막 반영 결정 |", "|---|---|---|---|"]
    for p in feature_designs():
        text = read(p)
        last = max_d(section(text, "변경 이력"))
        rows.append(f"| {p.parent.name} | [{p.stem}]({p.relative_to(DESIGN).as_posix()}) "
                    f"| {head_field(text, '상태') or '?'} | {f'D-{last:03d}' if last else '-'} |")
    return "\n".join(rows)


def status_doc(decisions):
    out = ["# 현황", "", "> 생성물입니다. 저장소 루트에서 `python -B Workflow/Tools/check.py --write`로 갱신합니다.", "",
           "## 작업·검증·개선", "", "- [작업 계약과 기록](Tasks/README.md)",
           "- [Unity 검증과 결과 확인](검증.md)", "- [개선 후보와 도입 기록](Improvements/README.md)",
           "- 아래 목록은 기획 문서의 현황이며 Unity 실행 성공을 뜻하지 않습니다.", ""]

    # 반영 대기: 반영 문서의 변경 이력에 그 번호가 없음 (머리의 관련 결정은 세지 않음)
    out += ["## 반영 대기", ""]
    pending = []
    for n, (where, status) in sorted(decisions.items()):
        if not status.startswith("유효"):
            continue
        for t in links(where):
            target = resolve(ROOT / "Decisions.md", t)
            hist = section(read(target), "변경 이력") if target.exists() else None
            if not hist or n not in {int(x) for x in D_RE.findall(hist)}:
                pending.append(f"- D-{n:03d} → [{target.stem}]({t})")
    out += (pending or ["- 없음"]) + [""]

    # 확인 필요 모음: 링크는 Workflow/ 기준으로 고친다
    out += ["## 확인 필요", ""]
    found = False
    for p in [DESIGN / "00_프로젝트.md"] + feature_designs():
        body = section(read(p), "확인 필요") if p.exists() else None
        items = [l for l in (body or "").splitlines() if l.strip() and l.strip() not in ("- 없음", "없음")]
        if not items:
            continue
        found = True
        out.append(f"### [{p.stem}]({rel(p)})")

        def fix(m):
            t = m.group(1)
            if not is_local(t):
                return m.group(0)
            return f"]({resolve(p, t).relative_to(ROOT).as_posix()})"

        out += [re.sub(r"\]\(([^)\s]+)\)", fix, l) for l in items] + [""]
    if not found:
        out += ["- 없음", ""]

    # 기능 문서 낡음: 기준 결정 < 기획서의 최신 반영 결정
    out += ["## 기능 문서 낡음", ""]
    stale = []
    for p in sorted((ROOT / "Features").glob("*.md")):
        text = read(p)
        m = re.search(r"관련 기획서:\s*\[[^\]]*\]\(([^)]+)\)", text)
        base = max_d(re.search(r"기준 결정:\s*(D-\d+)", text).group(1)) if "기준 결정:" in text else 0
        if not m:
            continue
        design = resolve(p, m.group(1))
        latest = max_d(section(read(design), "변경 이력")) if design.exists() else 0
        if base < latest:
            stale.append(f"- [{p.stem}]({rel(p)}): 기준 D-{base:03d} < 기획서 D-{latest:03d}")
    out += (stale or ["- 없음"]) + [""]
    return "\n".join(out)


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    write = "--write" in sys.argv
    decisions = parse_decisions()
    problems = check(decisions)

    # 생성물 비교·쓰기
    readme = DESIGN / "README.md"
    old_readme = read(readme)
    a, b = old_readme.find(TABLE_START), old_readme.find(TABLE_END)
    if a < 0 or b < 0:
        problems.append("Design/README.md: 기능 표 표시(<!-- 기능 표 시작/끝 -->) 없음")
        new_readme = old_readme
    else:
        new_readme = old_readme[:a + len(TABLE_START)] + "\n" + feature_table() + "\n" + old_readme[b:]
    status_path = ROOT / "현황.md"
    new_status = status_doc(decisions)
    old_status = read(status_path) if status_path.exists() else ""

    outdated = [n for n, old, new in (("Design/README.md", old_readme, new_readme), ("현황.md", old_status, new_status)) if old != new]
    if write:
        readme.write_text(new_readme, encoding="utf-8")
        status_path.write_text(new_status, encoding="utf-8")
        if outdated:
            print("다시 만듦: " + ", ".join(outdated))
    elif outdated:
        problems += [f"{n}: 생성물이 낡음 (--write 로 다시 만드십시오)" for n in outdated]

    for p in problems:
        print("문제: " + p)
    print(f"문제 {len(problems)}건")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
