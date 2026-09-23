import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import complexity_gate
from complexity_gate import (
    ROOT,
    GateError,
    build_reports,
    compare_findings,
    get_symbol_identity,
    read_findings,
    settings_digest,
    verify_reports,
)


class ComplexityGateTests(unittest.TestCase):
    def test_read_findings_extracts_scores_and_ignores_other_rules(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            report_path = Path(temp_dir) / "project-net9.0.sarif"
            source_path = Path(temp_dir) / "src" / "Service.cs"
            source_path.parent.mkdir()
            source_path.write_text(
                "public Task<Result> ExecuteAsync(string name, CancellationToken token)\n{\n}\n",
                encoding="utf-8",
            )
            report_path.write_text(
                json.dumps(
                    {
                        "version": "1.0.0",
                        "runs": [
                            {
                                "results": [
                                    {
                                        "ruleId": "CA1502",
                                        "message": "'Handle' has a cyclomatic complexity of '22'.",
                                        "locations": [
                                            {
                                                "resultFile": {
                                                    "uri": source_path.as_uri(),
                                                    "region": {"startLine": 1, "startColumn": 19, "endColumn": 30},
                                                }
                                            }
                                        ],
                                    },
                                    {
                                        "ruleId": "S3776",
                                        "message": "Refactor this method to reduce its Cognitive Complexity from 27 to the 15 allowed.",
                                        "locations": [
                                            {
                                                "resultFile": {
                                                    "uri": source_path.as_uri(),
                                                    "region": {"startLine": 1, "startColumn": 19, "endColumn": 30},
                                                }
                                            }
                                        ],
                                    },
                                    {"ruleId": "S1118", "message": "ignored"},
                                ]
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            findings = read_findings([report_path], repo_root=Path(temp_dir))

        self.assertEqual(["CA1502", "S3776"], [finding["rule"] for finding in findings])
        self.assertEqual([22, 27], [finding["score"] for finding in findings])
        self.assertEqual([1, 1], [finding["line"] for finding in findings])

    def test_verify_reports_fails_closed_when_project_report_is_missing(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            report_dir = Path(temp_dir)
            (report_dir / "OtherProject-net9.0.sarif").write_text(
                '{"version":"1.0.0","runs":[{"results":[]}]}',
                encoding="utf-8",
            )

            with self.assertRaisesRegex(GateError, "No analyzer report was produced"):
                verify_reports(report_dir, [ROOT / "src/Automaton.Api/Automaton.Api.csproj"])

    def test_baseline_comparison_accepts_improvements_and_rejects_new_or_worse_findings(self):
        baseline = {
            "schemaVersion": 1,
            "rules": ["CA1502", "CA1506", "S3776"],
            "settingsSha256": settings_digest(),
            "dotnetSdkVersion": "9.0.318",
            "compilerVersions": ["4.13.0"],
            "findings": [
                {"identity": "existing|method-a", "score": 30},
                {"identity": "existing|method-b", "score": 12},
            ],
        }
        current = [
            {"identity": "existing|method-a", "score": 20},
            {"identity": "existing|method-b", "score": 14},
            {"identity": "new|method-c", "score": 20},
        ]

        new, worsened, resolved = compare_findings(
            baseline,
            current,
            sdk_version="9.0.318",
            compiler_versions=["4.13.0"],
        )

        self.assertEqual(["new|method-c"], [finding["identity"] for finding in new])
        self.assertEqual(["existing|method-b"], [finding["identity"] for finding in worsened])
        self.assertEqual(0, resolved)

    def test_nested_editorconfig_suppression_invalidates_the_baseline(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            (repo_root / ".editorconfig").write_text("root = true\n", encoding="utf-8")
            source_root = repo_root / "src"
            source_root.mkdir()
            (source_root / "CodeMetricsConfig.txt").write_text("{}\n", encoding="utf-8")
            (source_root / "Directory.Build.targets").write_text("<Project />\n", encoding="utf-8")
            baseline_settings = settings_digest(repo_root)

            nested_config = source_root / "UnitTests" / ".editorconfig"
            nested_config.parent.mkdir()
            nested_config.write_text(
                "[*.cs]\ndotnet_diagnostic.S3776.severity = none\n",
                encoding="utf-8",
            )
            baseline = {
                "schemaVersion": 1,
                "rules": ["CA1502", "CA1506", "S3776"],
                "settingsSha256": baseline_settings,
                "dotnetSdkVersion": "9.0.318",
                "compilerVersions": ["4.13.0"],
                "findings": [],
            }

            self.assertNotEqual(baseline_settings, settings_digest(repo_root))
            with self.assertRaisesRegex(GateError, "Analyzer settings changed"):
                compare_findings(
                    baseline,
                    [],
                    sdk_version="9.0.318",
                    compiler_versions=["4.13.0"],
                    current_settings_digest=settings_digest(repo_root),
                )

    def test_sdk_pin_disables_roll_forward_and_rejects_a_different_selected_sdk(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_root = Path(temp_dir)
            solution = temp_root / "Automaton.Quality.sln"
            report_dir = temp_root / "reports"
            report_dir.mkdir()

            with (
                patch.object(complexity_gate, "installed_net9_sdk"),
                patch.object(complexity_gate, "selected_sdk_version", return_value="9.0.319"),
                patch.object(complexity_gate, "run_checked") as run_checked,
                self.assertRaisesRegex(GateError, "requested .NET SDK 9.0.318, but dotnet selected 9.0.319"),
            ):
                build_reports(solution, report_dir, "Release", "9.0.318")

            global_json = json.loads((temp_root / "global.json").read_text(encoding="utf-8"))
            self.assertEqual("disable", global_json["sdk"]["rollForward"])
            run_checked.assert_not_called()

    def test_symbol_identity_uses_method_signature_and_ignores_body_lines(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "Service.cs"
            source_path.write_text(
                "public Task<Result> ExecuteAsync(string name, CancellationToken token)\n"
                "{\n"
                "    return Task.FromResult(new Result(name));\n"
                "}\n",
                encoding="utf-8",
            )

            first = get_symbol_identity(source_path, 1, 21, 33, "")
            source_path.write_text(
                "public Task<Result> ExecuteAsync(string name, CancellationToken token)\n"
                "{\n"
                "    Validate(name);\n"
                "    return Task.FromResult(new Result(name));\n"
                "}\n",
                encoding="utf-8",
            )
            second = get_symbol_identity(source_path, 1, 21, 33, "")
            source_path.write_text(
                "public Task<Result> ExecuteAsync(Stream stream, CancellationToken token)\n{\n}\n",
                encoding="utf-8",
            )
            overload = get_symbol_identity(source_path, 1, 21, 33, "")

        self.assertEqual(first, second)
        self.assertNotEqual(first[1], overload[1])


if __name__ == "__main__":
    unittest.main()
