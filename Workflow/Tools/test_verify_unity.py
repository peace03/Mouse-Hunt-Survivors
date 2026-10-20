"""검증기가 잘못된 증거를 성공으로 처리하지 않는지 확인한다."""
import copy
import json
import shutil
import uuid
import unittest
from pathlib import Path

try:
    import verify_unity as verify
except ModuleNotFoundError:
    verify = None


class VerificationTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(verify, "Unity 검증 실행기가 아직 구현되지 않음")
        base = Path(__file__).resolve().parents[1] / "Archive/TestFixtures"
        base.mkdir(parents=True, exist_ok=True)
        # Python 3.14의 Windows 임시 폴더 ACL은 제한 토큰의 접근을 막을 수 있다.
        self.root = base / uuid.uuid4().hex
        self.root.mkdir()
        self.assertEqual(self.root.resolve().parent, base.resolve())
        self.addCleanup(shutil.rmtree, self.root)

    def xml(self, cases, **summary):
        attributes = {"result": "Passed", "total": str(len(cases)), **summary}
        text = "<test-run " + " ".join(f'{k}="{v}"' for k, v in attributes.items()) + ">"
        text += "".join(f'<test-case fullname="{name}" result="{result}"/>' for name, result in cases)
        path = self.root / "tests.xml"
        path.write_text(text + "</test-run>", encoding="utf-8")
        return path

    def test_real_cases_pass(self):
        observed = verify.read_test_xml(self.xml([("Required", "Passed"), ("Other", "Passed")]), ["Required"])
        self.assertEqual(observed, ["Other", "Required"])

    def test_missing_empty_or_malformed_xml_cannot_pass(self):
        with self.assertRaises(verify.InvalidEvidence):
            verify.read_test_xml(self.root / "missing.xml", ["Required"])
        with self.assertRaises(verify.InvalidEvidence):
            verify.read_test_xml(self.xml([]), ["Required"])
        (self.root / "bad.xml").write_text("<test-run>", encoding="utf-8")
        with self.assertRaises(verify.InvalidEvidence):
            verify.read_test_xml(self.root / "bad.xml", ["Required"])

    def test_missing_skipped_failed_or_inconsistent_cases_cannot_pass(self):
        for cases, summary in [([("Other", "Passed")], {}), ([("Required", "Skipped")], {}),
                               ([("Required", "Failed")], {}), ([("Required", "Passed")], {"total": "2"}),
                               ([("Required", "Passed")], {"result": "Failed"})]:
            with self.subTest(cases=cases, summary=summary), self.assertRaises(verify.InvalidEvidence):
                verify.read_test_xml(self.xml(cases, **summary), ["Required"])

    def test_nonce_and_candidate_must_match(self):
        path = self.root / "report.json"
        data = {"nonce": "this-run", "candidate_digest": "sha256:" + "a" * 64,
                "stage": "compile", "success": True}
        path.write_text(json.dumps(data), encoding="utf-8")
        self.assertTrue(verify.read_bound_report(path, "this-run", data["candidate_digest"], "compile")["success"])
        for nonce, digest, stage in [("old-run", data["candidate_digest"], "compile"),
                                     ("this-run", "sha256:" + "b" * 64, "compile"),
                                     ("this-run", data["candidate_digest"], "build")]:
            with self.assertRaises(verify.InvalidEvidence):
                verify.read_bound_report(path, nonce, digest, stage)

    def test_scope_sees_additions_deletions_and_protected_changes(self):
        rules = {"allowed_write": ["Assets/Game/**"], "protected_write": ["Assets/Game/Locked.cs"]}
        before = {"Assets/Game/Old.cs": "a", "Assets/Game/Locked.cs": "a", "Packages/manifest.json": "a"}
        after = {**before, "Assets/Game/New.cs": "b"}
        del after["Assets/Game/Old.cs"]
        self.assertEqual(verify.check_scope(before, after, rules), ["Assets/Game/New.cs", "Assets/Game/Old.cs"])
        for path in ["Packages/manifest.json", "Assets/Game/Locked.cs", "Unexpected.cs"]:
            with self.subTest(path=path), self.assertRaises(verify.InvalidEvidence):
                verify.check_scope(before, {**after, path: "changed"}, rules)

    def test_path_cannot_escape_or_target_an_absolute_file(self):
        for name in ["../outside", "/outside", "C:/outside", "Assets/../../outside", "Assets\\outside"]:
            with self.subTest(name=name), self.assertRaises(verify.InvalidEvidence):
                verify.safe_path(self.root, name)
        self.assertEqual(verify.safe_path(self.root, "Assets/Game.cs"), self.root / "Assets/Game.cs")

    def test_contract_rejects_examples_unknown_fields_and_missing_limits(self):
        template = Path(__file__).resolve().parents[1] / "Templates/작업계약.json"
        data = json.loads(template.read_text(encoding="utf-8"))
        data.update(example_only=False, contract_id="test-r1", base_oid="a" * 40,
                    base_manifest_digest="sha256:" + "a" * 64)
        verify.validate_contract(data)
        for change in [{"example_only": True}, {"unknown": 1}, {"schema_version": "9"},
                       {"base_oid": None}, {"base_manifest_digest": {}}, {"required_gates": ["scope", 1]},
                       {"limits": {"max_wall_minutes": 0, "max_repair_rounds": 2, "max_infra_retries": 2}},
                       {"allowed_write": ["../outside"]}, {"required_gates": []}]:
            with self.subTest(change=change), self.assertRaises(verify.InvalidEvidence):
                verify.validate_contract({**copy.deepcopy(data), **change})

    def test_build_success_requires_player_and_data(self):
        build = self.root / "Player"
        build.mkdir()
        with self.assertRaises(verify.InvalidEvidence):
            verify.build_files(build)
        (build / "MidnightMouse.exe").write_bytes(b"fixture executable")
        with self.assertRaises(verify.InvalidEvidence):
            verify.build_files(build)
        (build / "UnityPlayer.dll").write_bytes(b"fixture library")
        (build / "MidnightMouse_Data").mkdir()
        (build / "MidnightMouse_Data/globalgamemanagers").write_bytes(b"fixture data")
        first = verify.build_files(build)
        (build / "MidnightMouse.exe").write_bytes(b"changed")
        self.assertNotEqual(first, verify.build_files(build))

    def test_required_failure_and_pending_human_are_not_delivery_pass(self):
        gates = [{"gate_id": "compile", "result": "PASS"}]
        self.assertEqual(verify.overall_result(gates, True), ("PASS", "INCONCLUSIVE", "REVIEW_READY"))
        self.assertEqual(verify.overall_result(gates, False), ("PASS", "PASS", "REVIEW_READY"))
        self.assertNotEqual(verify.overall_result(gates + [{"gate_id": "playmode", "result": "INCONCLUSIVE"}], False)[0], "PASS")
        self.assertEqual(verify.overall_result([{"gate_id": "compile", "result": "FAIL"}], False)[2], "FAILED")

    def test_cancelled_run_does_not_promote_late_success(self):
        self.assertEqual(verify.overall_result([{"gate_id": "compile", "result": "PASS"}], False, True),
                         ("INCONCLUSIVE", "INCONCLUSIVE", "CANCELLED"))

    def test_duplicate_json_fields_are_rejected(self):
        path = self.root / "contract.json"
        path.write_text('{"example_only":true,"example_only":false}', encoding="utf-8")
        with self.assertRaises(verify.InvalidEvidence):
            verify.load(path)


if __name__ == "__main__":
    unittest.main()
