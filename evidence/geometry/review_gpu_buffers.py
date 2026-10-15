"""Independent OpenCV review of immutable Unity GPU readback evidence.

Usage: python evidence/geometry/review_gpu_buffers.py RUN_DIRECTORY OUTPUT_DIRECTORY
Requires numpy and OpenCV with EXR support. Does not alter source captures.
"""
import os
os.environ["OPENCV_IO_ENABLE_OPENEXR"] = "1"
import hashlib
import json
import pathlib
import sys
import cv2
import numpy as np

source = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
report = {
    "source": str(source),
    "scope": "Independent EXR decoding, finiteness, ranges, hashes, normal unit lengths, and manifest sampling audit. Shadow/reflection per-piece identity and complete absence are NOT_VALIDATED. Review PNGs use one fixed physical-radiance exposure for every probe face and snapshot; originals are unchanged.",
    "reflectionPreviewExposure": 1 / 8192,
    "captures": [],
    "buffers": [],
    "gpuFaultFixtures": [],
}
manifests = {}

def independent_boundary_review(prefix, samples):
    """Recompute segment distances with NumPy from serialized uploaded geometry."""
    oracle_path = source / (prefix + "-mesh-oracle.json")
    if not oracle_path.exists():
        return {"status": "NOT_AVAILABLE: this build did not save mesh/camera snapshots"}
    geometry = json.loads(oracle_path.read_text())
    depth = cv2.imread(str(source / (prefix + "-depth-metres.exr")), cv2.IMREAD_UNCHANGED)[:, :, 2]
    normals = cv2.imread(str(source / (prefix + "-normal-world.exr")), cv2.IMREAD_UNCHANGED)[:, :, 2::-1]
    direct_error = np.median([abs(float(depth[p["y"], p["x"]]) - p["actualDepthMetres"]) for p in samples])
    flipped_error = np.median([abs(float(depth[depth.shape[0] - 1 - p["y"], p["x"]]) - p["actualDepthMetres"]) for p in samples])
    flip = flipped_error < direct_error
    decoded_depth_errors = []
    decoded_normal_dot_errors = []
    for p in samples:
        row = depth.shape[0] - 1 - p["y"] if flip else p["y"]
        decoded_depth_errors.append(abs(float(depth[row, p["x"]]) - p["actualDepthMetres"]))
        if p.get("hasExpectedSectionSurface"):
            actual = normals[row, p["x"]].astype(np.float64)
            actual /= max(np.linalg.norm(actual), 1e-30)
            expected = np.array([p["expectedNormal"][axis] for axis in ("x", "y", "z")])
            decoded_normal_dot_errors.append(abs(float(actual @ expected) - p["normalDot"]))
    meshes = []
    for mesh in geometry["meshes"]:
        world = np.array([[p["x"], p["y"], p["z"]] for p in mesh["vertices"]], dtype=np.float64)
        projected = np.array([[p["x"], p["y"], p["z"]] for p in mesh["screenVertices"]], dtype=np.float64)
        triangles = np.array(mesh["triangles"], dtype=np.int64).reshape(-1, 3)
        positions = world[triangles]
        normal = np.cross(positions[:, 1] - positions[:, 0], positions[:, 2] - positions[:, 0])
        normal /= np.maximum(np.linalg.norm(normal, axis=1), 1e-30)[:, None]
        screen = projected[triangles]
        meshes.append((mesh["id"], screen, normal))
    checked = []
    for p in samples:
        if not p["result"].startswith(("FAIL", "NOT_VALIDATED")):
            continue
        pixel = np.array([p["x"] + .5, p["y"] + .5])
        row = depth.shape[0] - 1 - p["y"] if flip else p["y"]
        actual_normal = normals[row, p["x"]].astype(np.float64)
        actual_normal /= max(np.linalg.norm(actual_normal), 1e-30)
        candidates = []
        for name, screen, normal in meshes:
            for edge in range(3):
                a = screen[:, edge]
                b = screen[:, (edge + 1) % 3]
                delta = b[:, :2] - a[:, :2]
                t = np.clip(np.sum((pixel - a[:, :2]) * delta, axis=1) / np.maximum(np.sum(delta * delta, axis=1), 1e-30), 0, 1)
                margin = np.linalg.norm(a[:, :2] + t[:, None] * delta - pixel, axis=1)
                z = 1 / ((1 - t) / a[:, 2] + t / b[:, 2])
                dot = normal @ actual_normal
                accepted = (margin <= 1 / 128) & (np.abs(z - p["actualDepthMetres"]) <= .001) & (dot >= .75)
                for i in np.flatnonzero(accepted):
                    candidates.append({"id": name, "triangle": int(i), "edge": edge, "marginPixels": float(margin[i]), "edgeDepthMetres": float(z[i]), "actualNormalDot": float(dot[i])})
        checked.append({"id": p["id"], "x": p["x"], "y": p["y"], "runtimeResult": p["result"], "cpuHitMarginPixels": p.get("projectedTriangleEdgeMarginPixels"), "matchingProjectedEdges": sorted(candidates, key=lambda x: x["marginPixels"])})
    return {"status": "RECOMPUTED_FROM_SAVED_GEOMETRY", "oracleSha256": hashlib.sha256(oracle_path.read_bytes()).hexdigest(), "exrRowFlipRequiredForUnityProbeCoordinates": bool(flip), "medianDecodedDepthMatchErrorMetres": float(min(direct_error, flipped_error)), "decodedSampleCount": len(samples), "maxDecodedDepthVersusManifestErrorMetres": max(decoded_depth_errors, default=None), "maxDecodedNormalDotVersusManifestError": max(decoded_normal_dot_errors, default=None), "disagreements": checked}

for path in sorted(source.glob("*-passes.json")):
    item = json.loads(path.read_text(encoding="utf-8"))
    manifests[path.name.removesuffix("-passes.json")] = item
    pixel = item.get("pixelAudit") or {}
    failures = [p for p in pixel.get("samples", []) if p.get("result", "").startswith("FAIL")]
    zero_normals = sum(sum(v * v for v in p["expectedNormal"].values()) < 1e-20 for p in failures)
    report["captures"].append({
        "file": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "status": item["status"],
        "errors": item["errors"],
        "expectedBuffers": item["expectedBuffers"],
        "savedBuffers": sum(item["saved"]),
        "requestFrame": item["requestFrame"],
        "captureFrame": item["captureFrame"],
        "reflection": item.get("reflection"),
        "planarReflection": item.get("planarReflection"),
        "shadowNativeSize": [item.get("shadowSourceWidth"), item.get("shadowSourceHeight")],
        "pixelAudit": {k: v for k, v in pixel.items() if k != "samples"},
        "failedSamplesWithZeroExpectedNormal": zero_normals,
        "maxMatchedDepthAbsoluteErrorMetres": max((abs(p["depthErrorMetres"]) for p in pixel.get("samples", []) if p.get("result") in ("PASS_SAMPLED_DEPTH_AND_NORMAL", "FAIL_NORMAL_MISMATCH")), default=None),
        "independentBoundaryReview": independent_boundary_review(path.name.removesuffix("-passes.json"), pixel.get("samples", [])),
    })
for path in sorted(source.glob("*.exr")):
    image = cv2.imread(str(path), cv2.IMREAD_UNCHANGED)
    if image is None:
        raise RuntimeError(f"Cannot decode {path}")
    rgb = image[:, :, 2::-1]
    item = {
        "file": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "width": image.shape[1], "height": image.shape[0],
        "allFinite": bool(np.isfinite(image).all()),
        "minRGB": rgb.min(axis=(0, 1)).tolist(),
        "maxRGB": rgb.max(axis=(0, 1)).tolist(),
    }
    if "normal-world" in path.name:
        valid = image[:, :, 3] > 0
        item["validPixels"] = int(valid.sum())
        item["maxUnitLengthError"] = float(np.abs(np.linalg.norm(rgb[valid], axis=1) - 1).max())
    if "shadow-atlas" in path.name:
        manifest = manifests[path.name.removesuffix("-shadow-atlas.exr")]
        native_size = [manifest["shadowSourceWidth"], manifest["shadowSourceHeight"]]
        item["varyingDepthPixels"] = int(np.count_nonzero(rgb[:, :, 0]))
        item["expectedNativeSizeFromManifest"] = native_size
        item["nativeSizeChannelsConstant"] = bool(np.all(rgb[:, :, 1] == native_size[0]) and np.all(rgb[:, :, 2] == native_size[1]))
    if "reflection-" in path.name:
        radiance = np.maximum(0, image[:, :, :3]) / 8192
        linear = radiance / (1 + radiance)
        srgb = np.where(linear <= 0.0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - .055)
        preview = np.rint(np.clip(srgb, 0, 1) * 255).astype(np.uint8)
        preview_path = output / (path.stem + "-exposure-1over8192.png")
        cv2.imwrite(str(preview_path), preview)
        item["fixedExposurePreview"] = preview_path.name
        item["radianceStdDevRGB"] = rgb.std(axis=(0, 1)).tolist()
    report["buffers"].append(item)
for path in sorted(source.glob("*-gpu-stale-depth-fault.json")):
    fixture = json.loads(path.read_text(encoding="utf-8"))
    prefix = path.name.removesuffix("-gpu-stale-depth-fault.json")
    independent_fault = {"status": "NOT_AVAILABLE"}
    fault_samples = fixture.get("injected", {}).get("samples", [])
    if fault_samples:
        fault_depth = cv2.imread(str(source / (prefix + "-gpu-stale-depth-fault.exr")), cv2.IMREAD_UNCHANGED)[:, :, 2]
        baseline_depth = cv2.imread(str(source / (prefix + "-depth-metres.exr")), cv2.IMREAD_UNCHANGED)[:, :, 2]
        direct = sum(abs(float(fault_depth[p["y"], p["x"]]) - p["actualDepthMetres"]) for p in fault_samples)
        flipped = sum(abs(float(fault_depth[fault_depth.shape[0] - 1 - p["y"], p["x"]]) - p["actualDepthMetres"]) for p in fault_samples)
        observations = []
        for p in fault_samples:
            y = fault_depth.shape[0] - 1 - p["y"] if flipped < direct else p["y"]
            old = float(baseline_depth[y, p["x"]]); changed = float(fault_depth[y, p["x"]])
            observations.append({"id": p["id"], "x": p["x"], "y": p["y"], "baselineDepthMetres": old, "faultDepthMetres": changed, "depthChangeMetres": changed - old, "errorFromFormerMeshMetres": changed - p["formerDepthMetres"]})
        independent_fault = {"status": "RECOMPUTED_FROM_ORIGINAL_AND_FAULT_EXR", "samples": observations,
            "changedOverOneMillimetre": sum(abs(p["depthChangeMetres"]) > .001 for p in observations),
            "matchingFormerDepthWithinOneMillimetre": sum(abs(p["errorFromFormerMeshMetres"]) <= .001 for p in observations)}
    report["gpuFaultFixtures"].append({
        "file": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "status": fixture["status"],
        "scope": fixture["scope"],
        "baselinePassingProbes": fixture.get("baselinePassingProbes"),
        "changedSelectedDepthPixels": fixture.get("changedSelectedDepthPixels"),
        "detectedStaleDepthPixels": fixture.get("detectedStaleDepthPixels"),
        "baselineStatus": fixture.get("baseline", {}).get("status"),
        "injectedStatus": fixture.get("injected", {}).get("status"),
        "historicalMeshIds": fixture.get("historicalMeshIds"),
        "independentExrVerification": independent_fault,
    })
report["summary"] = {
    "captures": len(report["captures"]),
    "buffers": len(report["buffers"]),
    "allFinite": all(x["allFinite"] for x in report["buffers"]),
    "allReadbacksComplete": all(x["status"] == "GPU_READBACK_COMPLETE" and not x["errors"] and x["expectedBuffers"] == x["savedBuffers"] for x in report["captures"]),
    "allShadowNativeSizesMatchManifests": all(x["nativeSizeChannelsConstant"] for x in report["buffers"] if "shadow-atlas" in x["file"]),
    "actualPlanarBufferCount": sum("reflection-planar" in x["file"] for x in report["buffers"]),
    "pixelAuditStatuses": [x["pixelAudit"].get("status") for x in report["captures"]],
    "gpuFaultFixtureStatuses": [x["status"] for x in report["gpuFaultFixtures"]],
}
(output / "buffer-review.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report["summary"], indent=2))
