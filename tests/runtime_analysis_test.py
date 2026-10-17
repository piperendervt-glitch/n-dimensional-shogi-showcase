"""Small synthetic fixtures for evidence attribution, not rendering performance."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location("runtime_analysis", Path(__file__).resolve().parents[1] / "tools/analyze_runtime.py")
analysis = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(analysis)


def frame(index, **values):
    return {"frame": index + 1, "_sampleIndex": index, "_measuredSeconds": index * .25,
            "elapsed": index * .25, "frameMs": 250, "cpuMainMs": 4, "gpuMs": 5,
            "angle": 0, "ply": 11, "stateHash": "absent", "visibleIds": "B-King-0", **values}


class RuntimeAnalysisTests(unittest.TestCase):
    def test_angle_partition_covers_boundaries_without_double_counting_holds(self):
        angles = [0, .00001, 4.999, 5, 44.5, 89.9, 90, -1, None]
        result = analysis.angle_windows([frame(i, angle=a) for i, a in enumerate(angles)], 30)
        self.assertEqual(result["zeroDegreeHold"]["frames"], 1)
        self.assertEqual(result["ninetyDegreeHold"]["frames"], 1)
        self.assertEqual(result["bins"][0]["summary"]["frames"], 2)
        self.assertEqual(result["bins"][1]["summary"]["frames"], 1)
        self.assertEqual(result["bins"][17]["summary"]["frames"], 1)
        self.assertEqual(sum(b["summary"]["frames"] for b in result["bins"]), 5)
        self.assertEqual(result["excludedOrUnavailableSamples"], 2)

    def test_each_return_keeps_absence_reappearance_and_missing_evidence_separate(self):
        rows = [frame(i) if i < 4 else frame(i, ply=27, stateHash="present", visibleIds="A-Pawn-0,B-King-0") for i in range(8)]
        events = [{"kind": "ReturnedH0", "time": 0, "ply": 11, "stateHash": "absent"},
                  {"kind": "ReturnedH0", "time": 1, "ply": 27, "stateHash": "present"},
                  {"kind": "ReturnedH0", "time": 1, "ply": 27, "stateHash": "different"},
                  {"kind": "ReturnedH0", "time": 9, "ply": 29, "stateHash": "missing"}]
        result = analysis.return_h0_windows(rows, events, 30)
        self.assertEqual(len(result), 4)
        self.assertEqual([x["visibility"] for x in result], ["HERO_ABSENT", "HERO_PRESENT", "HERO_PRESENT", "NOT_AVAILABLE"])
        self.assertEqual(result[0]["summary"]["frames"], 4)  # Exact one-second boundary excluded.
        self.assertEqual(result[1]["summary"]["frames"], 4)
        self.assertIs(result[1]["anchorStateMatchesEvent"], True)
        self.assertIs(result[2]["anchorStateMatchesEvent"], False)
        self.assertIsNone(result[3]["firstMatchingFrame"])
        self.assertEqual([x["sourceEventIndex"] for x in result], list(range(4)))

    def test_unrecorded_visibility_is_not_proof_of_absence(self):
        rows = [frame(0)]
        del rows[0]["visibleIds"]
        result = analysis.return_h0_windows(rows, [{"kind": "ReturnedH0", "time": 0, "ply": 11}], 30)[0]
        self.assertEqual(result["visibility"], "NOT_AVAILABLE")
        self.assertEqual(result["visibilityUnavailableSamples"], 1)

    def test_transition_peak_ignores_disabled_and_suppressed_large_strength(self):
        rows = [frame(0, transitionEnabled=False, transitionStrength=1),
                frame(1, transitionEnabled=True, transitionStrength=.9, transitionSuppressed=True),
                frame(2, transitionEnabled=True, transitionStrength=.82),
                frame(3, transitionEnabled=True, transitionStrength=.82),
                frame(4, transitionEnabled=True, transitionStrength=.62)]
        result = analysis.transition_peak(rows, 30)
        self.assertEqual(result["observedPeakStrength"], .82)
        self.assertEqual(result["summary"]["frames"], 2)
        self.assertFalse(result["suppressionRecordedForAllEligibleFrames"])
        self.assertIsNone(analysis.transition_peak([frame(0)], 30)["observedPeakStrength"])

    def test_ending_peak_requires_recorded_lumens_and_uses_declared_fraction(self):
        self.assertEqual(analysis.ending_glow_peak([frame(0)], 30)["status"], "NOT_RECORDED")
        self.assertEqual(analysis.ending_glow_peak([frame(0, endingGlowLumens=0)], 30)["status"], "NO_POSITIVE_LUMENS")
        result = analysis.ending_glow_peak([frame(i, endingGlowLumens=v) for i, v in enumerate([0, 94.9, 95, 100])], 30)
        self.assertEqual(result["observedPeakLumens"], 100)
        self.assertEqual(result["summary"]["frames"], 2)

    def test_resolution_requests_are_not_substituted_for_measurements(self):
        rows = [frame(0, outputWidth=1920, outputHeight=1080, internalWidth=1280, internalHeight=720),
                frame(1, outputWidth=2560, outputHeight=1440, internalWidth=1920, internalHeight=1080),
                frame(2, outputWidth=2560, outputHeight=1440), frame(3, outputWidth=0, outputHeight=0)]
        warnings = []
        result = analysis.resolution_evidence({"width": 1920, "height": 1080}, rows, {"requestedResolution": {"width": 2560, "height": 1440}}, warnings)
        self.assertTrue(result["headerRequestMismatch"])
        self.assertEqual(result["outputFrames"]["requestedMismatchSamples"], 1)
        self.assertEqual(len(result["outputFrames"]["changes"]), 2)
        self.assertEqual(result["internalFrames"]["samples"], 2)
        self.assertEqual(result["internalFrames"]["unavailableSamples"], 2)
        self.assertEqual(len(warnings), 2)
        fallback = analysis.resolution_evidence({"width": 2560, "height": 1440, "requestedWidth": 2560, "requestedHeight": 1440}, [], {}, [])
        self.assertEqual(fallback["requestedSource"], "header")
        self.assertEqual(fallback["internalFrames"]["status"], "NOT_AVAILABLE")

    def test_two_process_loops_are_attributed_with_legacy_fields_preserved(self):
        with tempfile.TemporaryDirectory(prefix="showcase-analysis-fixture-") as temporary:
            root = Path(temporary)
            (root / "launcher.json").write_text(json.dumps({"schema": "showcase-validation-launch/1", "processId": 123,
                "startedUtc": "2026-10-17T00:00:00Z", "endedUtc": "2026-10-17T00:02:00Z", "width": 2560, "height": 1440,
                "status": "EXITED_ZERO", "exitCode": 0, "runKind": "Benchmark"}), encoding="utf-8")
            (root / "process-memory.jsonl").write_text("", encoding="utf-8")
            for index in range(2):
                folder = root / f"loop-{index + 1}"
                folder.mkdir()
                (folder / "header.json").write_text(json.dumps({"startedUtc": f"2026-10-17T00:0{index}:01Z", "width": 1920,
                    "height": 1080, "captureMode": "none / clean benchmark", "targetFrameRate": 30, "expectedPlies": 27}), encoding="utf-8")
                (folder / "frames.jsonl").write_text(json.dumps(frame(0)) + "\n", encoding="utf-8")
                (folder / "events.jsonl").write_text("", encoding="utf-8")
            args = argparse.Namespace(runtime=root, cold_seconds=5, cold_frames=None)
            first = analysis.analyze_run(root / "loop-1", args, [])
            second = analysis.analyze_run(root / "loop-2", args, [])
            self.assertEqual(first["startupClassification"]["status"], "FIRST_RECORDED_RUN_IN_PROCESS")
            self.assertEqual(second["startupClassification"]["status"], "WARM_RESTART_SAME_PROCESS")
            self.assertEqual(second["startupClassification"]["processRunIndex"], 2)
            self.assertEqual(second["category"], "benchmark-no-capture")
            self.assertEqual(second["completion"]["status"], "INCOMPLETE_OR_DIAGNOSTIC")
            self.assertEqual(second["cold"]["frames"], 1)
            self.assertEqual(second["runContract"]["targetFrameRate"], 30)
            self.assertEqual(second["stressWindows"]["angles40to50Degrees"]["frames"], 0)
            self.assertTrue(any("Requested output" in warning for warning in second["warnings"]))
            rendered = analysis.markdown({"generatedUtc": "fixture", "coldWarmMethodJa": "fixture", "runs": [first, second]})
            self.assertIn("WARM_RESTART_SAME_PROCESS", rendered)
            self.assertIn("NOT_RECORDED", rendered)
            self.assertEqual(analysis.run_contract({})["targetFrameRate"], 60)


if __name__ == "__main__":
    unittest.main()
