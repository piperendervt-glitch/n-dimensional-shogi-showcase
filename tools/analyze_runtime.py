"""Aggregate measured standalone evidence without mixing capture or automation runs.

Standard library only. Run from any directory:
  python tools/analyze_runtime.py
  python tools/analyze_runtime.py --cold-frames 300
No runtime files are altered. Missing timers and provenance remain explicitly missing.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TIMERS = {
    "frameDeltaMs": ("frameMs", True),
    "cpuMainMs": ("cpuMainMs", True),
    "cpuRenderMs": ("cpuRenderMs", True),
    "cpuPresentWaitMs": ("cpuPresentWaitMs", False),
    "gpuMs": ("gpuMs", True),
    "lastSectionUpdateMs": ("sectionMs", False),
}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return str(path.resolve())


def read_json(path: Path, warnings: list[str]):
    if not path.is_file():
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        warnings.append(f"{relative(path)}: {error}")
        return None


def read_jsonl(path: Path, warnings: list[str]) -> list[dict]:
    if not path.is_file():
        warnings.append(f"Missing {relative(path)}")
        return []
    rows = []
    for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
            if not isinstance(row, dict):
                raise ValueError("Expected JSON object")
            rows.append(row)
        except ValueError as error:
            warnings.append(f"{relative(path)}:{number}: {error}; row excluded")
    return rows


def finite_number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def distribution(values) -> dict:
    values = sorted(float(v) for v in values if finite_number(v))
    if not values:
        return {"samples": 0, "availability": "NOT_AVAILABLE", "median": None, "p95": None, "p99": None, "worst": None}
    percentile = lambda p: values[min(len(values) - 1, max(0, math.ceil(p * len(values)) - 1))]
    return {"samples": len(values), "availability": "MEASURED", "median": statistics.median(values),
            "p95": percentile(.95), "p99": percentile(.99), "worst": values[-1]}


def memory_series(rows: list[dict], key: str) -> dict:
    samples = [(r["_measuredSeconds"], r[key]) for r in rows if finite_number(r.get(key)) and r[key] >= 0]
    if not samples:
        return {"availability": "NOT_AVAILABLE"}
    xs, ys = zip(*samples)
    mean_x, mean_y = statistics.mean(xs), statistics.mean(ys)
    denominator = sum((x - mean_x) ** 2 for x in xs)
    slope = sum((x - mean_x) * (y - mean_y) for x, y in samples) / denominator if denominator else None
    return {"availability": "MEASURED", "samples": len(samples), "firstBytes": ys[0], "lastBytes": ys[-1],
            "minBytes": min(ys), "maxBytes": max(ys), "lastMinusFirstBytes": ys[-1] - ys[0],
            "linearTrendBytesPerMeasuredSecond": slope,
            "meaning": "Sampled memory occupancy trend, NOT allocated bytes per second or proof of a leak."}


def summarize(rows: list[dict], target_fps: int = 60) -> dict:
    timers = {}
    for label, (key, positive) in TIMERS.items():
        values = [r[key] for r in rows if finite_number(r.get(key)) and (r[key] > 0 if positive else r[key] >= 0)
                  and (label != "cpuPresentWaitMs" or finite_number(r.get("cpuMainMs")) and r["cpuMainMs"] > 0)]
        timers[label] = distribution(values)
        timers[label]["excludedOrUnavailableSamples"] = len(rows) - len(values)
    frame_values = [r["frameMs"] for r in rows if finite_number(r.get("frameMs")) and r["frameMs"] > 0]
    def counter(key):
        values = [r[key] for r in rows if finite_number(r.get(key))]
        return {"samples": len(values), "first": values[0] if values else None, "last": values[-1] if values else None,
                "max": max(values) if values else None, "lastMinusFirst": values[-1] - values[0] if values else None}
    separated = any("gcGen1Delta" in r or "gcGen2Delta" in r for r in rows)
    gc = {"schema": "separate-generation-counters/1" if separated else "legacy-generation-sum/0" if rows else "NOT_AVAILABLE",
          "generation0" if separated else "legacyGenerationSum": counter("gcDelta"),
          "warning": "Generation counters are reported separately and must not be summed; occupancy is not allocation throughput." if separated else
                     "Legacy sum of GC.CollectionCount(0/1/2) deltas may duplicate collection counts. Do not compare with newer generation0 counters or treat as allocation throughput."}
    if separated:
        gc["generation1"] = counter("gcGen1Delta")
        gc["generation2"] = counter("gcGen2Delta")
    budget = 1000 / target_fps
    tolerance = max(.25, budget * .01)
    return {"frames": len(rows), "timers": timers,
            "measuredFrameDurationSeconds": sum(frame_values) / 1000,
            "framesOver16_6667ms": sum(v > 1000 / 60 for v in frame_values),
            "over16_6667Fraction": sum(v > 1000 / 60 for v in frame_values) / len(frame_values) if frame_values else None,
            "framesOver33_3333ms": sum(v > 1000 / 30 for v in frame_values),
            "over33_3333Fraction": sum(v > 1000 / 30 for v in frame_values) / len(frame_values) if frame_values else None,
            "targetFrameBudget": {"targetFrameRate": target_fps, "milliseconds": budget,
                "framesOverBudget": sum(v > budget for v in frame_values),
                "overBudgetFraction": sum(v > budget for v in frame_values) / len(frame_values) if frame_values else None,
                "jitterToleranceMilliseconds": tolerance,
                "framesOverBudgetPlusTolerance": sum(v > budget + tolerance for v in frame_values),
                "overBudgetPlusToleranceFraction": sum(v > budget + tolerance for v in frame_values) / len(frame_values) if frame_values else None,
                "meaning": "Descriptive counts, not automatic acceptance. Strict thresholds include refresh pacing jitter; the separately stated tolerance does not remove or rewrite samples."},
            "monoUsedMemory": memory_series(rows, "monoBytes"),
            "unityTotalAllocatedMemory": memory_series(rows, "totalAllocatedBytes"),
            "garbageCollectionCounters": gc}


def classify(path: Path, header: dict, events: list[dict]) -> tuple[str, list[str]]:
    parts = [p.lower() for p in path.parts]
    capture = str(header.get("captureMode", "")).lower().strip()
    artifacts = any(path.glob("*.png")) or (path / "video-frames").exists() or any(path.glob("*depth*.exr"))
    reasons = []
    if any("automation" in p for p in parts):
        return "automation", ["Path identifies an automation run."]
    if any("smoke" in p for p in parts):
        return "smoke", ["Path identifies a smoke run; never promoted to clean benchmark."]
    interactive = [e.get("kind") for e in events if e.get("kind") in {"Pause", "DebugToggle", "ExitDialog", "HelpersToggle"}]
    if any("playback" in p for p in parts) and interactive:
        return "automation", ["Playback-harness path with shared control-command events."]
    if interactive and capture != "none / clean benchmark":
        return "interactive", ["Control commands were recorded; excluded from both clean benchmark and ordinary capture comparisons."]
    if capture == "none / clean benchmark":
        if artifacts:
            reasons.append("Header claims no capture but image/video artifacts exist.")
        if interactive:
            reasons.append("Interactive/automation commands were logged: " + ", ".join(sorted(set(interactive))))
        return ("benchmark-conflict", reasons) if reasons else ("benchmark-no-capture", ["Explicit clean benchmark header and no capture/interactive evidence."])
    if artifacts or capture and capture != "none":
        return "capture", ["Screenshots, readback, video artifacts or capture-enabled header."]
    return "unclassified", ["No explicit clean benchmark header; not used for clean benchmark comparison."]


def collect_manifests(paths: list[Path]) -> list[dict]:
    candidates = set(paths)
    for folder in (ROOT / "evidence/environment", ROOT / "evidence/build", ROOT / "evidence/builds"):
        if folder.exists():
            candidates.update(folder.rglob("*manifest*.json"))
    archive=ROOT / "evidence/environment/source-manifests"
    if archive.exists():
        candidates.update(archive.glob("*.json"))
    manifests = []
    for path in sorted(candidates):
        warnings = []
        data = read_json(path, warnings)
        if isinstance(data, dict):
            manifests.append({"path": relative(path), "sha256": digest(path), "data": data})
    return manifests


def scalar_values(data, keys: set[str]):
    if isinstance(data, dict):
        for key, value in data.items():
            if key.lower() in keys and isinstance(value, (str, int)):
                yield str(value)
            yield from scalar_values(value, keys)
    elif isinstance(data, list):
        for value in data:
            yield from scalar_values(value, keys)


def provenance(header: dict, manifests: list[dict]) -> dict:
    build_source = str(header.get("buildSource", ""))
    guid = str(header.get("buildGUID") or build_source.split(" / ")[0]).strip()
    source_hash = header.get("sourceHash")
    source_manifest_sha = header.get("sourceManifestSha256")
    matches = []
    for manifest in manifests:
        data = manifest["data"]
        build_ids = set(scalar_values(data, {"buildguid", "buildid", "applicationbuildguid"}))
        source_ids = set(scalar_values(data, {"sourcehash"}))
        if guid and guid in build_ids or source_hash and str(source_hash) in source_ids or source_manifest_sha and source_manifest_sha == manifest["sha256"]:
            matches.append({"manifest": manifest["path"], "manifestSha256": manifest["sha256"],
                            "matchingBuildGUID": guid if guid in build_ids else None,
                            "matchingSourceManifestSha256": source_manifest_sha if source_manifest_sha == manifest["sha256"] else None,
                            "sourceHashes": sorted(source_ids), "gitCommits": sorted(set(scalar_values(data, {"gitcommit", "commit", "head"})))})
    return {"buildSource": build_source, "buildGUID": guid or None, "sourceHash": source_hash,
            "sourceManifestSha256": source_manifest_sha, "buildUtc": header.get("buildUtc"),
            "status": "EXACT_MANIFEST_MATCH" if matches else "UNJOINED",
            "matches": matches,
            "limitation": None if matches else "No exact build GUID/source hash relation found; a newer source manifest is not assigned by date or directory proximity."}


def visible_sets(rows: list[dict]) -> list[dict]:
    groups = {}
    for row in rows:
        ids = tuple(sorted(x for x in str(row.get("visibleIds", "")).split(",") if x))
        key = (row.get("scene"), row.get("ply"), row.get("stateHash"), ids)
        if key not in groups:
            groups[key] = {"scene": key[0], "ply": key[1], "stateHash": key[2], "visibleIds": list(ids),
                           "visibleCount": len(ids), "reportedVisibleCount": row.get("visiblePieces"),
                           "firstFrame": row.get("frame"), "lastFrame": row.get("frame"), "samples": 0}
        groups[key]["samples"] += 1
        groups[key]["lastFrame"] = row.get("frame")
    return list(groups.values())


def visibility_evidence(rows: list[dict], events: list[dict]) -> dict:
    h90 = [r for r in rows if finite_number(r.get("angle")) and abs(r["angle"] - 90) <= .05]
    returns = []
    for event in (e for e in events if e.get("kind") == "ReturnedH0"):
        matching = [r for r in rows if r.get("ply") == event.get("ply") and abs(r.get("angle", -999)) <= .05
                    and r.get("elapsed", -1) >= event.get("time", 0) - 1e-6]
        returns.append({"event": event, "firstMatchingFrame": matching[0] if matching else None,
                        "visibleSets": visible_sets(matching),
                        "heroAbsentFromReportedVisibleIds": all("A-Pawn-0" not in str(r.get("visibleIds", "")).split(",") for r in matching) if matching else None})
    return {"H90": visible_sets(h90), "returnH0": returns,
            "interpretation": "Renderer-reported volumetric sections only. No invented threshold for 'most disappear'; visual clarity requires human review."}


def run_contract(header: dict) -> dict:
    """Old headers retain their original 60 fps / 19-ply contract without mutation."""
    fps = header.get("targetFrameRate", 60)
    plies = header.get("expectedPlies", 19)
    if not isinstance(fps, int) or isinstance(fps, bool) or fps <= 0:
        raise ValueError("Invalid recorded targetFrameRate")
    if not isinstance(plies, int) or isinstance(plies, bool) or plies <= 0:
        raise ValueError("Invalid recorded expectedPlies")
    return {"targetFrameRate": fps, "expectedPlies": plies,
            "scenarioVersion": header.get("scenarioVersion", "central-pawn-directed/1"),
            "targetFrameRateSource": "header" if "targetFrameRate" in header else "legacy-v1-default",
            "expectedPliesSource": "header" if "expectedPlies" in header else "legacy-v1-default"}


def alternating_view_evidence(events: list[dict], frames: list[dict], contract: dict) -> dict:
    """Check the recorded order and angle of actual move animation, not reservation."""
    if contract["scenarioVersion"] != "central-pawn-directed/2":
        return {"status": "NOT_APPLICABLE_LEGACY", "meaning": "The old 19-ply director did not promise an H0 return after every hidden A move."}
    expected = contract["expectedPlies"]
    dimensional = list(range(11, expected + 1, 2))
    indexed = list(enumerate(events))
    problems, moves = [], []
    starts = [(i,e) for i,e in indexed if e.get("kind") == "MoveAnimationStarted"]
    commits = [(i,e) for i,e in indexed if e.get("kind") == "MoveCommitted"]
    for ci, commit in commits:
        ply = commit.get("ply")
        if not isinstance(ply, int):
            problems.append("MoveCommitted has no integer ply")
            continue
        preceding_commit = max((i for i,e in commits if i < ci), default=-1)
        matching_starts = [(i,e) for i,e in starts if preceding_commit < i < ci and e.get("ply") == ply - 1]
        item = {"ply": ply, "side": "A" if ply % 2 else "B", "commit": commit, "problems": []}
        target = 90 if ply in dimensional else 0
        if len(matching_starts) != 1:
            item["problems"].append("Expected exactly one MoveAnimationStarted before this commit")
        else:
            si, start = matching_starts[0]
            item["animationStart"] = start
            sample_rows = [r for r in frames if finite_number(r.get("elapsed")) and start["time"] <= r["elapsed"] <= commit["time"] and r.get("ply") in (ply-1,ply)]
            item["animationFrameSamples"] = len(sample_rows)
            item["targetAngle"] = target
            for event_name, event in (("start", start), ("commit", commit)):
                if not finite_number(event.get("angle")) or abs(event["angle"] - target) > .05:
                    item["problems"].append(f"{event_name} angle is not {target} degrees")
            if not sample_rows or any(not finite_number(r.get("angle")) or abs(r["angle"] - target) > .05 for r in sample_rows):
                item["problems"].append("Animation frames are absent or leave the required fixed angle")
            if ply in dimensional:
                rotations = [(i,e) for i,e in indexed if preceding_commit < i < si and e.get("kind") == "SliceRotationComplete" and e.get("ply") == ply-1 and abs(e.get("angle",-999)-90) <= .05]
                if not rotations or rotations[-1][1].get("stateHash") != start.get("stateHash"):
                    item["problems"].append("H90 completion with unchanged pre-move state is missing")
                next_start = min((i for i,e in starts if i > ci), default=len(events))
                returns = [(i,e) for i,e in indexed if ci < i < next_start and e.get("kind") == "ReturnedH0" and e.get("ply") == ply]
                if len(returns) != 1:
                    item["problems"].append("Expected one H0 return after the A commit and before the next move")
                else:
                    returned = returns[0][1]
                    item["returnedH0"] = returned
                    if abs(returned.get("angle",-999)) > .05 or returned.get("stateHash") != commit.get("stateHash"):
                        item["problems"].append("H0 return angle/state differs from the committed position")
        moves.append(item)
        problems.extend(f"ply {ply}: {p}" for p in item["problems"])
    actual_plies = [item["ply"] for item in moves]
    complete = actual_plies == list(range(1, expected + 1))
    return {"status": "FAIL" if problems else "PASS" if complete else "INCOMPLETE",
            "expectedPlies": expected, "observedCommittedPlies": actual_plies,
            "expectedH90APlies": dimensional,
            "verifiedH90APlies": [m["ply"] for m in moves if m["ply"] in dimensional and not m["problems"]],
            "verifiedH0BPlies": [m["ply"] for m in moves if m["side"] == "B" and not m["problems"]],
            "problems": problems, "moves": moves,
            "meaning": "Event ordering, state hashes and recorded frame angles verify one A move per rotated view, an H0 return before the next move, and every B animation at H0. It does not establish human perception or image quality."}


def utc_datetime(value):
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed.replace(tzinfo=timezone.utc) if parsed.tzinfo is None else parsed.astimezone(timezone.utc)
    except ValueError:
        return None


def angle_windows(rows: list[dict], target_fps: int) -> dict:
    """Partition measured angles, without assuming the middle is the worst case."""
    epsilon = 1e-6
    zero, ninety, bins, excluded = [], [], [[] for _ in range(18)], 0
    for row in rows:
        angle = row.get("angle")
        if not finite_number(angle):
            excluded += 1
        elif abs(angle) <= epsilon:
            zero.append(row)
        elif abs(angle - 90) <= epsilon:
            ninety.append(row)
        elif 0 < angle < 90:
            bins[min(17, int(angle // 5))].append(row)
        else:
            excluded += 1
    return {"binWidthDegrees": 5, "endpointToleranceDegrees": epsilon,
            "zeroDegreeHold": summarize(zero, target_fps),
            "ninetyDegreeHold": summarize(ninety, target_fps),
            "bins": [{"lowerInclusive": i * 5, "upperExclusive": (i + 1) * 5,
                      "summary": summarize(group, target_fps)} for i, group in enumerate(bins)],
            "excludedOrUnavailableSamples": excluded,
            "meaning": "Endpoint-angle samples are separate from interior 5-degree bins. All recorded scenes/plies are included; this is an observed path distribution, not a global worst-angle proof. Latest CPU/GPU timers may refer to an earlier frame; sectionMs repeats the last update on idle frames."}


def return_h0_windows(rows: list[dict], events: list[dict], target_fps: int) -> list[dict]:
    """Keep each return, including the final reappearance and missing observations."""
    result = []
    for index, event in enumerate(events):
        if event.get("kind") != "ReturnedH0":
            continue
        anchor = next((r for r in rows if finite_number(event.get("time")) and
                       finite_number(r.get("elapsed")) and r["elapsed"] >= event["time"] and
                       r.get("ply") == event.get("ply") and finite_number(r.get("angle")) and
                       abs(r["angle"]) <= .05), None)
        window = [] if anchor is None else [r for r in rows if
                  anchor["_measuredSeconds"] <= r["_measuredSeconds"] < anchor["_measuredSeconds"] + 1]
        known = [r for r in window if isinstance(r.get("visibleIds"), str)]
        absent = [r for r in known if "A-Pawn-0" not in r["visibleIds"].split(",")]
        present = [r for r in known if "A-Pawn-0" in r["visibleIds"].split(",")]
        visibility = ("NOT_AVAILABLE" if not known else "PARTIALLY_RECORDED" if len(known) != len(window)
                      else "MIXED" if absent and present else "HERO_ABSENT" if absent else "HERO_PRESENT")
        result.append({"sourceEventIndex": index, "event": event, "firstMatchingFrame": anchor,
                       "anchorStateMatchesEvent": None if anchor is None or not event.get("stateHash") or not anchor.get("stateHash") else anchor["stateHash"] == event["stateHash"],
                       "visibility": visibility, "heroAbsentSamples": len(absent), "heroPresentSamples": len(present),
                       "visibilityUnavailableSamples": len(window) - len(known),
                       "summary": summarize(window, target_fps), "visibleSets": visible_sets(known),
                       "definition": "Each event separately: first observed H0 frame of that ply at/after the event, then a half-open one-second cumulative unscaled window. HERO_PRESENT includes final reappearance and is not disappearance evidence."})
    return result


def transition_peak(rows: list[dict], target_fps: int) -> dict:
    eligible = [r for r in rows if r.get("transitionEnabled") is True and
                r.get("transitionSuppressed") is not True and finite_number(r.get("transitionStrength")) and
                r["transitionStrength"] > 0]
    maximum = max((r["transitionStrength"] for r in eligible), default=None)
    selected = [r for r in eligible if abs(r["transitionStrength"] - maximum) <= 1e-6] if maximum is not None else []
    return {"status": "MEASURED" if selected else "NO_ENABLED_POSITIVE_STRENGTH_SAMPLES",
            "observedPeakStrength": maximum, "strengthTolerance": 1e-6,
            "eligibleFrames": len(eligible), "summary": summarize(selected, target_fps),
            "suppressionRecordedForAllEligibleFrames": bool(eligible) and all(isinstance(r.get("transitionSuppressed"), bool) for r in eligible),
            "meaning": "Observed maximum of enabled, positive dimensional-transition strength; explicitly suppressed frames excluded. Older records do not prove suppression state. This is separate from the ending light, whose measurements are reported independently."}


def ending_glow_peak(rows: list[dict], target_fps: int) -> dict:
    recorded = [r for r in rows if finite_number(r.get("endingGlowLumens")) and r["endingGlowLumens"] >= 0]
    maximum = max((r["endingGlowLumens"] for r in recorded), default=None)
    selected = [r for r in recorded if maximum > 0 and r["endingGlowLumens"] >= maximum * .95] if maximum is not None else []
    return {"status": "MEASURED" if selected else "NO_POSITIVE_LUMENS" if recorded else "NOT_RECORDED",
            "observedPeakLumens": maximum, "peakWindowMinimumFraction": .95,
            "recordedFrames": len(recorded), "unavailableSamples": len(rows) - len(recorded),
            "summary": summarize(selected, target_fps),
            "meaning": "Frames whose recorded applied ending-light lumens reach at least 95% of the observed positive maximum. No intensity or peak is inferred for old logs from GameEnded time or the source-code sine function."}


def resolution_evidence(header: dict, rows: list[dict], external: dict, warnings: list[str]) -> dict:
    def pair(width, height):
        return {"width": width, "height": height} if all(isinstance(v, int) and not isinstance(v, bool) and v > 0 for v in (width, height)) else None
    requested_data = external.get("requestedResolution") or {}
    launcher_request = pair(requested_data.get("width"), requested_data.get("height"))
    header_request = pair(header.get("requestedWidth"), header.get("requestedHeight"))
    requested = launcher_request or header_request
    if launcher_request is not None and header_request is not None and launcher_request != header_request:
        warnings.append("Launcher and runtime-header requested dimensions disagree; both requests are retained and launcher request is used for comparison.")
    recorded = pair(header.get("width"), header.get("height"))
    mismatch = requested != recorded if requested is not None and recorded is not None else None
    if mismatch:
        warnings.append(f"Requested output {requested['width']}x{requested['height']} differs from runtime header {recorded['width']}x{recorded['height']}; requested resolution is not proven by this header.")
    def series(prefix):
        observations, changes, previous, mismatches = [], [], None, 0
        for row in rows:
            dimensions = pair(row.get(prefix + "Width"), row.get(prefix + "Height"))
            if dimensions is None:
                continue
            observations.append(dimensions)
            if dimensions != previous:
                changes.append({"frame": row.get("frame"), "sampleIndex": row.get("_sampleIndex"),
                                "measuredSeconds": row.get("_measuredSeconds"), **dimensions})
                previous = dimensions
            if prefix == "output" and requested is not None and dimensions != requested:
                mismatches += 1
        return {"status": "MEASURED" if observations else "NOT_AVAILABLE", "samples": len(observations),
                "unavailableSamples": len(rows) - len(observations), "changes": changes,
                "requestedMismatchSamples": mismatches if prefix == "output" and requested is not None else None}
    output, internal = series("output"), series("internal")
    if output["requestedMismatchSamples"]:
        warnings.append(f"Runtime per-frame output dimensions disagree with launcher request in {output['requestedMismatchSamples']} measured frames.")
    return {"requestedOutput": requested, "requestedSource": "launcher" if launcher_request else "header" if header_request else None,
            "launcherRequest": launcher_request, "headerRequest": header_request,
            "headerOutput": recorded, "headerRequestMismatch": mismatch,
            "outputFrames": output, "internalFrames": internal,
            "meaning": "Launcher dimensions are requests. Header dimensions are a startup observation, not proof they stayed fixed. Future outputWidth/outputHeight and internalWidth/internalHeight fields provide measured changes. Missing internal dimensions are not inferred from the 100% label."}


def startup_classification(external: dict) -> dict:
    ordinal, pid = external.get("processRunIndex"), external.get("processId")
    known = isinstance(ordinal, int) and ordinal > 0 and isinstance(pid, int) and pid > 0
    return {"status": ("FIRST_RECORDED_RUN_IN_PROCESS" if ordinal == 1 else "WARM_RESTART_SAME_PROCESS") if known else "PROCESS_START_UNKNOWN",
            "processId": pid, "processRunIndex": ordinal, "processStartedUtc": external.get("processStartedUtc"),
            "recordedRunsInProcess": external.get("recordedRunsInProcess"),
            "meaning": "Ordered runtime headers under one launcher's child PID distinguish the first recorded run from later same-process runs. Per-run cold/warm summaries remain compatible; later runs' initial intervals are not process-cold. OS, disk, driver and shader cache coldness is not established."}


def external_measurements(path: Path, header: dict, runtime_root: Path) -> dict:
    launcher_path = None
    for parent in (path, *path.parents):
        candidate = parent / "launcher.json"
        if candidate.is_file():
            launcher_path = candidate
            break
        if parent.resolve() == runtime_root.resolve():
            break
    if launcher_path is None:
        return {"status": "NOT_AVAILABLE", "reason": "No launcher.json in this run's evidence ancestry."}
    warnings = []
    launch = read_json(launcher_path, warnings)
    if not isinstance(launch, dict) or launch.get("schema") != "showcase-validation-launch/1":
        return {"status": "UNRECOGNIZED_LAUNCHER", "warnings": warnings}
    memory_path = launcher_path.parent / launch.get("memoryFile", "process-memory.jsonl")
    samples = read_jsonl(memory_path, warnings)
    start = utc_datetime(header.get("startedUtc"))
    following, siblings = [], []
    if start:
        for other in launcher_path.parent.rglob("header.json"):
            other_header = read_json(other, warnings)
            timestamp = utc_datetime(other_header.get("startedUtc")) if isinstance(other_header, dict) else None
            if timestamp:
                siblings.append((timestamp, str(other.resolve())))
            if timestamp and timestamp > start:
                following.append(timestamp)
    siblings.sort()
    run_index = next((i + 1 for i, (_, filename) in enumerate(siblings) if filename == str((path / "header.json").resolve())), None)
    end = min(following) if following else utc_datetime(launch.get("endedUtc"))
    selected = []
    gpu_corrections = []
    raw_verified_samples = 0
    for sample in samples:
        timestamp = utc_datetime(sample.get("recordedUtc"))
        if timestamp and start and timestamp >= start and (end is None or timestamp < end) and sample.get("processId") == launch.get("processId"):
            sample["_measuredSeconds"] = (timestamp - start).total_seconds()
            raw_instances = sample.get("gpuInstances")
            if isinstance(raw_instances, list) and raw_instances and all(isinstance(i, dict) and i.get("name") and
                    all(finite_number(i.get(k)) and i[k] >= 0 for k in ("dedicatedBytes", "sharedBytes", "committedBytes")) for i in raw_instances):
                unique = {i["name"]: i for i in raw_instances}
                if len(unique) != len(raw_instances):
                    warnings.append(f"Duplicate GPU adapter instance names at {sample.get('recordedUtc')}; unique names used for totals.")
                mapping = {"gpuDedicatedBytes": "dedicatedBytes", "gpuSharedBytes": "sharedBytes", "gpuCommittedBytes": "committedBytes"}
                corrected = {target: sum(i[source] for i in unique.values()) for target, source in mapping.items()}
                reported = {target: sample.get(target) for target in mapping}
                raw_verified_samples += 1
                if corrected != reported:
                    gpu_corrections.append({"recordedUtc": sample.get("recordedUtc"), "reportedTotals": reported,
                                            "derivedTotals": corrected, "uniqueAdapterInstances": len(unique),
                                            "reason": "Reported totals disagree with preserved per-adapter raw gauges; analysis recomputes sums. Original log is unchanged."})
                    sample.update(corrected)
            selected.append(sample)
    def metric(key):
        return {"distributionBytes": distribution(s.get(key) for s in selected if finite_number(s.get(key))), "trend": memory_series(selected, key)}
    return {"status": "MEASURED" if selected else "NO_SAMPLES_IN_RUN_WINDOW", "launcherPath": relative(launcher_path),
            "launcherSha256": digest(launcher_path), "memoryPath": relative(memory_path),
            "memorySha256": digest(memory_path) if memory_path.is_file() else None,
            "processId": launch.get("processId"), "launchKind": launch.get("runKind"), "launchStatus": launch.get("status"),
            "processStartedUtc": launch.get("startedUtc"), "processRunIndex": run_index, "recordedRunsInProcess": len(siblings),
            "requestedResolution": {"width": launch.get("width"), "height": launch.get("height")},
            "exitCode": launch.get("exitCode"), "executableSha256": launch.get("executableSha256"),
            "runWindowStartUtc": start.isoformat() if start else None, "runWindowEndUtc": end.isoformat() if end else None,
            "windowDefinition": "Run header startedUtc through next run header or launcher end; samples must match launcher child PID.",
            "samples": len(selected), "drivers": launch.get("drivers"), "arguments": launch.get("arguments"),
            "speed": launch.get("speed"), "privateMemory": metric("privateBytes"), "workingSet": metric("workingSetBytes"),
            "gpuDedicatedUsage": metric("gpuDedicatedBytes"), "gpuSharedUsage": metric("gpuSharedBytes"), "gpuCommittedUsage": metric("gpuCommittedBytes"),
            "samplingQueryMs": distribution(s.get("queryMilliseconds") for s in selected),
            "gpuCounterStatuses": dict(Counter(s.get("gpuCounterStatus", "UNRECORDED") for s in selected)),
            "gpuTotalValidation": {"rawVerifiedSamples": raw_verified_samples, "correctedSamples": len(gpu_corrections),
                "corrections": gpu_corrections, "originalLogModified": False,
                "policy": "Validate totals against recorded unique adapter gauges; recover mismatches only in analysis with both reported and derived values retained."},
            "meaning": launch.get("gpuCounterMeaning"), "overhead": launch.get("measurementOverhead"), "warnings": warnings}


def analyze_run(path: Path, args, manifests: list[dict]) -> dict:
    warnings = []
    header = read_json(path / "header.json", warnings) or {}
    contract = run_contract(header)
    stats = lambda rows: summarize(rows, contract["targetFrameRate"])
    frames = read_jsonl(path / "frames.jsonl", warnings)
    events = read_jsonl(path / "events.jsonl", warnings)
    performance = read_json(path / "performance.json", warnings)
    transcript = read_json(path / "transcript.json", warnings)
    cumulative = 0.0
    for index, row in enumerate(frames):
        row["_sampleIndex"] = index
        row["_measuredSeconds"] = cumulative
        delta = row.get("frameMs")
        if finite_number(delta) and delta > 0:
            cumulative += delta / 1000
        else:
            warnings.append(f"Invalid frameMs at frame {row.get('frame')}; duration excluded.")
    if args.cold_frames is not None:
        cold = [r for r in frames if r["_sampleIndex"] < args.cold_frames]
        warm = [r for r in frames if r["_sampleIndex"] >= args.cold_frames]
        definition = f"First {args.cold_frames} recorded frames; remaining frames warm. This is per-run, not proven process-cold startup."
    else:
        cold = [r for r in frames if r["_measuredSeconds"] < args.cold_seconds]
        warm = [r for r in frames if r["_measuredSeconds"] >= args.cold_seconds]
        definition = f"Frames beginning in first {args.cold_seconds:g} cumulative unscaled frame-duration seconds; remaining frames warm. Playback elapsed is not used."
    category, category_reasons = classify(path, header, events)
    external = external_measurements(path, header, args.runtime)
    resolution = resolution_evidence(header, frames, external, warnings)
    if external.get("launchKind") == "Automation":
        category, category_reasons = "automation", ["Launcher explicitly records Automation."]
    elif external.get("launchKind") == "Smoke":
        category, category_reasons = "smoke", ["Launcher explicitly records Smoke."]
    by_scene = {}
    for scene in sorted(set(r.get("scene", 0) for r in frames)):
        scene_rows = [r for r in frames if r.get("scene", 0) == scene]
        by_scene[str(scene)] = {"all": stats(scene_rows),
                               "cold": stats([r for r in cold if r.get("scene", 0) == scene]),
                               "warm": stats([r for r in warm if r.get("scene", 0) == scene])}
    ended = [e for e in events if e.get("kind") == "GameEnded"]
    vfx_start = next((r for r in frames if ended and r.get("ply") == ended[0].get("ply") and r.get("elapsed", -1) >= ended[0].get("time", 0)), None)
    final_window = [] if vfx_start is None else [r for r in frames if vfx_start["_measuredSeconds"] <= r["_measuredSeconds"] <= vfx_start["_measuredSeconds"] + 2]
    disappearance = []
    for e in (e for e in events if e.get("kind") == "ReturnedH0"):
        candidate = next((r for r in frames if r.get("ply") == e.get("ply") and r.get("elapsed", -1) >= e.get("time", 0) and abs(r.get("angle", -999)) < .05), None)
        if candidate:
            disappearance.extend(r for r in frames if candidate["_measuredSeconds"] <= r["_measuredSeconds"] <= candidate["_measuredSeconds"] + 1)
    record_count = len(transcript.get("records", [])) if isinstance(transcript, dict) else None
    independent = [e for e in events if e.get("kind") == "IndependentVerificationPassed"]
    failures = [e for e in events if e.get("kind") == "ScenarioMismatch"]
    files = {name: {"path": relative(path / name), "sha256": digest(path / name), "bytes": (path / name).stat().st_size}
             for name in ("header.json", "frames.jsonl", "performance.json", "transcript.json", "events.jsonl") if (path / name).is_file()}
    worst = max((r for r in frames if finite_number(r.get("frameMs"))), key=lambda r: r["frameMs"], default=None)
    return {"path": relative(path), "category": category, "categoryReasons": category_reasons, "header": header, "runContract": contract,
            "provenance": provenance(header, manifests), "externalProcessMeasurements": external, "inputFiles": files, "warnings": warnings,
            "completion": {"transcriptPlies": record_count, "expectedPlies": contract["expectedPlies"], "independentVerificationEvents": independent, "gameEndedEvents": ended,
                           "failureEvents": failures, "performanceClosed": performance is not None,
                           "status": "RECORDED_COMPLETE" if record_count == contract["expectedPlies"] and independent and ended and not failures else "INCOMPLETE_OR_DIAGNOSTIC"},
            "coldWarmDefinition": definition, "startupClassification": startup_classification(external),
            "resolutionEvidence": resolution,
            "all": stats(frames), "cold": stats(cold), "warm": stats(warm),
            "angleWindows": angle_windows(frames, contract["targetFrameRate"]),
            "returnH0Windows": return_h0_windows(frames, events, contract["targetFrameRate"]),
            "transitionPeak": transition_peak(frames, contract["targetFrameRate"]),
            "endingGlowPeak": ending_glow_peak(frames, contract["targetFrameRate"]),
            "byScene": by_scene, "stressWindows": {"angles40to50Degrees": stats([r for r in frames if 40 <= r.get("angle", -1) <= 50]),
                "firstSecondAfterReturnH0": stats(list({r["_sampleIndex"]:r for r in disappearance}.values())),
                "firstTwoSecondsAfterGameEnded": stats(final_window),
                "endingWindowAnchor": {"event": ended[0] if ended else None, "firstMatchingFrame": vfx_start,
                    "definition": "First two cumulative unscaled frame-duration seconds beginning at the first frame at/after GameEnded. The delayed H0 return between terminal commit and VFX is excluded."},
                "limitation": "These windows identify recorded angles/VFX start, not a proven worst angle or measured peak VFX intensity."},
            "worstFrame": worst, "visibility": visibility_evidence(frames, events),
            "alternatingDimensionViews": alternating_view_evidence(events, frames, contract),
            "runtimePerformanceSummary": performance, "transcriptHash": transcript.get("transcriptHash") if isinstance(transcript, dict) else None}


def fmt(value, digits=2) -> str:
    return "未取得" if value is None else f"{value:.{digits}f}"


def markdown(report: dict) -> str:
    lines = ["# Standalone性能実測レポート", "", f"集計日時（UTC）: {report['generatedUtc']}", "",
             "これは実行ログから再計算した測定値です。新しい画質重視版は30fpsを目標とし、旧runの60fps目標は履歴として保持します。平均fpsだけで性能・画質の合否は判定しません。",
             "clean benchmark、smoke、撮影、操作自動試験を分離し、異なるrunのフレームを混ぜていません。", "",
             "## 集計方法と制限", "",
             "- 中央値は通常の中央値、p95/p99はnearest-rank、最悪値は最大値です。CPU/GPUの0値・未記録値は欠測とします。present waitの0値はCPUタイミングが同時取得されている場合のみ有効です。",
             f"- cold/warm: {report['coldWarmMethodJa']}",
             "- cold/warmは各run内の区分です。同一プロセスの再生ループでは、先頭区間をプロセスの完全なcold起動と呼べません。",
             "- 起動区分はlauncherのPIDと記録開始順で判定します。最初の記録runでもOS・GPU・シェーダーキャッシュのcold状態は証明しません。出力解像度は要求値・開始時header・フレーム実測を区別します。",
             "- frameMsは実描画フレーム間隔。CPU main/render、GPU、CPU present waitは取得できたタイマーだけを表示し、加算しません。present waitがない旧runは未取得です。",
             "- sectionMsは最後に行った断面更新の所要時間です。更新がないフレームにも同じ値が記録されるため、毎フレームの実作業時間として加算できません。",
             "- 目標のフレーム予算はheaderに従い、30fpsなら33.333ms、旧60fpsなら16.667msです。厳密境界の超過には微小な描画周期の揺れも含まれます。JSONには両基準と、明示した小さい許容幅を加えた超過数を別々に保存します。",
             "- 終局VFXの窓はGameEndedイベント以後の実時間2秒です。王捕獲commitからH0へ帰還する演出時間をVFXの計測へ混ぜません。",
             "- 全角度の5°区分と0°/90°の端点を別集計します。40–50°は従来の比較窓で、最悪角の証明ではありません。各H0帰還は個別に記録し、最終再出現を消失と区別します。",
             "- 次元効果のpeakは有効な実測strengthの最大値です。終局光は記録lumensが観測最大の95%以上のフレームを別集計し、旧runの未記録強度は推測しません。",
             "- monoBytes/totalAllocatedBytesはメモリー占有値。傾きは占有量の推移で、割当バイト/秒ではありません。新runのGCは世代0/1/2を分離して表示し、合計しません。旧smokeの世代合計は重複を含む可能性があり、新runの世代0カウンターと比較しません。",
             "- ヘッダーのVRAM値はハードウェア容量です。外部WDDMカウンターがある場合はプロセス専用/共有GPUメモリー使用量を別表示し、厳密な物理常駐VRAMと同一視しません。",
             "- build GUID/sourceHash/sourceManifestSha256の完全一致があるmanifestのみ関連付けます。新しいmanifestを古いrunへ推測で割り当てません。", "",
             "## run一覧", "", "| 分類 | run | 解像度 / profile / mode | フレーム | 全体 median / p95 / p99 / worst ms | 完走記録 | source対応 |",
             "|---|---|---|---:|---|---|---|"]
    for run in report["runs"]:
        h = run["header"]; t = run["all"]["timers"]["frameDeltaMs"]
        lines.append(f"| {run['category']} | {h.get('runId',run['path'])} | {h.get('width','?')}×{h.get('height','?')} / {h.get('profile','?')} / {h.get('mode','?')} | {run['all']['frames']} | {' / '.join(fmt(t[k]) for k in ('median','p95','p99','worst'))} | {run['completion']['status']} | {run['provenance']['status']} |")
    if not report["runs"]:
        lines.append("| 未実施 | runなし | — | 0 | 未取得 | NOT_RUN | UNJOINED |")
    clean = [r for r in report["runs"] if r["category"] == "benchmark-no-capture"]
    lines += ["", "## 撮影なしbenchmark", ""]
    if not clean:
        lines.append("撮影なしbenchmarkの条件を満たすrunはまだありません。smokeや撮影runから完成性能を推定しません。")
    for run in report["runs"]:
        h = run["header"]
        contract = run["runContract"]
        budget_ms = 1000 / contract["targetFrameRate"]
        lines += ["", f"## {h.get('runId',run['path'])}", "", f"分類: **{run['category']}**。証跡: `{run['path']}`。",
                  f"GPU: {h.get('device','未取得')} / API: {h.get('graphicsApi','未取得')} / 記録driver欄: {h.get('driver','未取得')}。",
                  f"AA: {h.get('antialiasing','未取得')}、内部解像度: {h.get('internalResolution','未取得')}、出力: {h.get('width','?')}×{h.get('height','?')}、VRAM容量: {h.get('graphicsMemoryMB','未取得')} MB。",
                  f"撮影設定: {h.get('captureMode','未記録')}。build/source対応: {run['provenance']['status']}。",
                  f"起動区分: {run['startupClassification']['status']}、同じlauncher PIDで記録順 {run['startupClassification']['processRunIndex']}。",
                  f"目標: {contract['targetFrameRate']}fps / {budget_ms:.3f}ms、予定{contract['expectedPlies']}手、scenario {contract['scenarioVersion']}。", "",
                  f"| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 目標{budget_ms:.3f}ms超の割合 |",
                  "|---|---:|---|---|---|---|"]
        sections = [("全体", run["all"]), ("cold", run["cold"]), ("warm", run["warm"])]
        sections += [(f"Scene {scene}", data["all"]) for scene, data in run["byScene"].items()]
        sections += [("0°端点", run["angleWindows"]["zeroDegreeHold"]),
                     ("90°端点", run["angleWindows"]["ninetyDegreeHold"]),
                     ("次元効果の観測peak", run["transitionPeak"]["summary"]),
                     ("終局光の観測peak付近", run["endingGlowPeak"]["summary"])]
        sections += [("40–50°", run["stressWindows"]["angles40to50Degrees"]),
                     ("H0帰還後1秒", run["stressWindows"]["firstSecondAfterReturnH0"]),
                     ("GameEnded後のVFX2秒", run["stressWindows"]["firstTwoSecondsAfterGameEnded"])]
        for label, data in sections:
            f, c, g = (data["timers"][x] for x in ("frameDeltaMs", "cpuMainMs", "gpuMs"))
            over = data["targetFrameBudget"]["overBudgetFraction"]
            lines.append(f"| {label} | {data['frames']} | {' / '.join(fmt(f[k]) for k in ('median','p95','p99','worst'))} | {fmt(c['median'])} / {fmt(c['p95'])} | {fmt(g['median'])} / {fmt(g['p95'])} | {fmt(over*100 if over is not None else None)}% |")
        memory = run["all"]["unityTotalAllocatedMemory"]; mono = run["all"]["monoUsedMemory"]; gc = run["all"]["garbageCollectionCounters"]
        resolution = run["resolutionEvidence"]
        lines += ["", f"解像度の要求: {resolution['requestedOutput']}、開始時header: {resolution['headerOutput']}、不一致: {resolution['headerRequestMismatch']}。",
                  f"出力寸法のフレーム実測: {resolution['outputFrames']['status']}、内部寸法: {resolution['internalFrames']['status']}。変更履歴はJSONに保存。",
                  f"次元効果の観測最大strength: {run['transitionPeak']['observedPeakStrength']}。終局光: {run['endingGlowPeak']['status']} / 観測最大 {run['endingGlowPeak']['observedPeakLumens']} lm。"]
        for returned in run["returnH0Windows"]:
            lines.append(f"H0帰還 event #{returned['sourceEventIndex']} / ply {returned['event'].get('ply')}: {returned['visibility']}、{returned['summary']['frames']}フレーム。")
        lines += ["", f"Unityメモリー占有の先頭→末尾: {memory.get('firstBytes','未取得')} → {memory.get('lastBytes','未取得')} bytes、最大 {memory.get('maxBytes','未取得')} bytes。",
                  f"Mono占有の先頭→末尾: {mono.get('firstBytes','未取得')} → {mono.get('lastBytes','未取得')} bytes。"]
        if gc["schema"] == "separate-generation-counters/1":
            lines.append(f"GCカウンター末尾: gen0={gc['generation0']['last']} / gen1={gc['generation1']['last']} / gen2={gc['generation2']['last']}。世代を合計していません。")
        else:
            lines.append(f"旧形式GC世代合計の末尾: {gc.get('legacyGenerationSum',{}).get('last','未取得')}。重複の可能性があるため、新形式gen0値と比較しません。")
        wait = run["all"]["timers"]["cpuPresentWaitMs"]
        lines.append(f"CPU present wait median/p95/p99/worst: {' / '.join(fmt(wait[k]) for k in ('median','p95','p99','worst'))} ms。")
        external = run["externalProcessMeasurements"]
        if external["status"] == "MEASURED":
            lines += [f"外部プロセス計測: PID {external['processId']}、{external['samples']} samples、launcher終了状態 {external['launchStatus']} / exitCode {external['exitCode']}。",
                      f"Private bytes先頭→末尾: {external['privateMemory']['trend'].get('firstBytes','未取得')} → {external['privateMemory']['trend'].get('lastBytes','未取得')}、最大 {external['privateMemory']['trend'].get('maxBytes','未取得')}。",
                      f"Working set先頭→末尾: {external['workingSet']['trend'].get('firstBytes','未取得')} → {external['workingSet']['trend'].get('lastBytes','未取得')}、最大 {external['workingSet']['trend'].get('maxBytes','未取得')}。",
                      f"WDDM専用GPUメモリー最大 {external['gpuDedicatedUsage']['trend'].get('maxBytes','未取得')} bytes、共有GPUメモリー最大 {external['gpuSharedUsage']['trend'].get('maxBytes','未取得')} bytes。厳密な物理常駐VRAMとの同一視はしていません。",
                      f"外部サンプリング処理時間 median/p95: {fmt(external['samplingQueryMs']['median'])} / {fmt(external['samplingQueryMs']['p95'])} ms。測定負荷が加わります。"]
            if external["gpuTotalValidation"]["correctedSamples"]:
                lines.append(f"GPU合計値の不一致を {external['gpuTotalValidation']['correctedSamples']} samplesで検出。元JSONLは変更せず、保存済みアダプター別生値から合計を再計算しました。旧報告値・補正値・根拠はanalysis.jsonに保持。")
        else:
            lines.append(f"外部PIDメモリー/GPUメモリー計測: {external['status']}。")
        h90 = run["visibility"]["H90"]
        lines.append("H90の報告可視枚数: " + (", ".join(f"ply {v['ply']}: {v['visibleCount']}枚" for v in h90) if h90 else "未取得") + "。")
        for returned in run["visibility"]["returnH0"]:
            lines.append(f"H0帰還後の主役ID非表示: {returned['heroAbsentFromReportedVisibleIds']}。対象ID集合・state hashはanalysis.jsonに保存。")
        alternating = run["alternatingDimensionViews"]
        lines.append(f"1手ごとのH90着手/H0帰還とBのH0着手: {alternating['status']}。")
        if alternating["status"] != "NOT_APPLICABLE_LEGACY":
            lines.append(f"確認したAのH90手: {alternating['verifiedH90APlies']}。BのH0手: {alternating['verifiedH0BPlies']}。最終王捕獲後の主役はH0で再び表示されます。")
        if run["warnings"]:
            lines += ["", "読込上の注意: " + " / ".join(run["warnings"])]
    lines += ["", "全タイマーのp99/最悪、場面別cold/warm、メモリー回帰傾き、H90/帰還後のID一覧、元ファイルSHA-256は `evidence/runtime/analysis.json` にあります。",
              "30fps目標の最終判断、画質・理解しやすさの人間承認、未取得の計測項目を、この集計で自動合格にはしていません。新runを旧60fps条件で不合格扱いにはしません。", ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", type=Path, default=ROOT / "evidence/runtime")
    parser.add_argument("--output", type=Path, default=ROOT / "evidence/runtime/analysis.json")
    parser.add_argument("--markdown", type=Path, default=ROOT / "docs/PERFORMANCE.md")
    parser.add_argument("--build-manifest", action="append", type=Path, default=[])
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--cold-seconds", type=float, default=5.0)
    group.add_argument("--cold-frames", type=int)
    args = parser.parse_args()
    if args.cold_seconds <= 0 or args.cold_frames is not None and args.cold_frames <= 0:
        parser.error("Cold interval must be positive")
    manifests = collect_manifests(args.build_manifest)
    runs = [analyze_run(p.parent, args, manifests) for p in sorted(args.runtime.rglob("header.json"))]
    grouped = defaultdict(list)
    for run in runs:
        h = run["header"]
        key = "|".join(str(x) for x in (run["category"], h.get("width"), h.get("height"), h.get("profile"), h.get("mode"), h.get("occlusion"), run["runContract"]["targetFrameRate"], run["runContract"]["scenarioVersion"], run["provenance"]["buildGUID"]))
        grouped[key].append(run["path"])
    report = {"schema": "showcase-runtime-analysis/2", "generatedUtc": datetime.now(timezone.utc).isoformat(),
              "analysisToolSha256": digest(Path(__file__)),
              "coldWarmMethodJa": f"先頭{args.cold_frames}記録フレーム / 残り。" if args.cold_frames else f"unscaled frameMsを累積した先頭{args.cold_seconds:g}秒 / 残り。再生速度で拡大されたelapsedは使いません。",
              "acceptance": "MEASUREMENTS_ONLY; HUMAN_PENDING; header-specific target (new 30fps, legacy 60fps); no automatic quality or performance acceptance",
              "categoryCounts": dict(Counter(r["category"] for r in runs)), "comparisonGroups": dict(grouped),
              "availableManifests": [{"path": m["path"], "sha256": m["sha256"]} for m in manifests], "runs": runs}
    args.output.parent.mkdir(parents=True, exist_ok=True);args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps({"runs": len(runs), "categories": report["categoryCounts"], "analysis": str(args.output), "markdown": str(args.markdown)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
