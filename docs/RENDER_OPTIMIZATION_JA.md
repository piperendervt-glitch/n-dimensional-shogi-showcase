# 描画パフォーマンス調査と最適化（2026-10-15）

対象: `builds/Showcase-Windows`（ShowcaseRaster / DirectedLive / 1920×1080 / RTX 5060 Ti / D3D11）。
数値はすべてこのマシンでの実測。別環境での値は未測定。

## 結論

- 遅さの正体は **GPU 25.6 ms/フレーム**（フレームレート上限なしで 39 fps）。30fps上限の下では毎フレーム約27msを待機していた。CPUは主因ではない（main 6ms中央値）。
- GPU時間の **71%（18.2 ms）が太陽の影**。その内訳の最大要因は、**毎フレーム更新の反射プローブ（128px立方体6面＋512px平面）が、本カメラと同じ4段×4096pxのカスケード影を面ごとに描き直していた**こと（合計約14 ms）。
- 見た目を変えない変更だけで GPU を **25.6 → 約11.1 ms**（非上限 39 → 約88 fps）に短縮。1080p/60fps上限が成立する。
- 描画設定（PCSS 64/64、4096px影、全解像度SSAO、接地影24sample、DOF、Bloom、SMAA、木漏れ日cookie）は **すべて変更していない**。

## 計測方法

`--gpu-ablation`（新設）: 固定カメラ・固定局面・フレームレート上限なしで、効果を1つずつOFF（または設定変更）し、各状態を 30 フレーム暖機後に 120 フレーム `FrameTimingManager` で標本化。`gpu-ablation.json` に中央値/p95 を保存。単独効果の差分は線形に足せない（影の計算は複数の描画で共有される）。

- before: `evidence/runtime/perf-ablation-before/`（build e8e4c9e3、最適化前のソース）
- after: `evidence/runtime/perf-ablation-after/`（最適化後）

## 根本原因（影響の大きい順、before計測）

| # | 原因 | 根拠（beforeでの GPU 差分） |
|---|---|---|
| 1 | **反射プローブの描画が本カメラと同じ影設定**。局所立方体（128px・6面・毎フレーム）と天板平面反射（512px・毎フレーム）は HDRP 既定の RealtimeReflection frame settings で描かれ、面ごとに 4 段×4096px の指向性影カスケードを再ラスタライズしていた。SSAO・接地影も面ごとに実行 | 立方体を影なし: −12.7 ms、平面を影なし: −3.5 ms、両方: −14.1 ms |
| 2 | **太陽影の描画コストそのもの**（4 段×4096px、32bit深度、PCSS 64/64）。上記1を含め、影OFFで −18.2 ms | 影OFF −18.2、2048px −10.9、2段 −10.0、PCSS 32/32 −2.5 |
| 3 | **静的メッシュが材質ごとに1つの巨大メッシュに結合**されていたため、影カスケード・反射面・本カメラのいずれでも視錐台/カスケード外の形状を省けず、庭全体（葉約9,000枚≒29万三角形＋敷石400枚）が全パスで描かれていた | 庭を非表示 −16.5 ms、葉の影投影のみOFF −3.7 ms |
| 4 | DOF後方（4.8m以遠は完全にぼける）にも 2 段のカスケード影を割り当てていた | 2段化 −10 ms の一部 |
| 5 | SSAO（全解像度8step×4dir）・接地影24sample・DOF・Bloom・SMAA・cookie・次元回転エフェクト | 各 ≦0.5 ms。最適化対象外 |
| – | CPU側: 4D回転中の断面再計算は main 中央値8.6ms/p95 16.5ms、`mesh.tangents` のコピー、補助セル線のstringキー、毎フレームの可視ID文字列生成で GC が 1回/フレーム | 60fps運用時の余裕確保のため割当てを削減（下記） |

`cube-probe-off`（`HDAdditionalReflectionData.enabled=false`）は before/after とも GPU 時間を減らさなかった。原因は未確認（HDRPの登録解除とレンダリング要求の関係）。本最適化はこの経路に依存しない。

## 実施した変更（見た目を変えないもの）

1. **反射プローブ専用の描画設定**（`ShowcaseEnvironment.ConfigureReflectionRender`）
   - 立方体プローブ: 影マップ・SSAO・接地影・volumetrics をOFF。128px の面では解像できない。
   - 平面反射: 影マップは維持（天板の鏡面に駒の影が映る）。SSAO・接地影・volumetrics はOFF。
   - プローブ専用 Volume（layer 10、本カメラの volume mask 外）で影を **2 段 / 10 m** に制限。本カメラは従来どおり。
2. **本カメラの影カスケード 4 → 3 段**。分割 0.10/0.28 は同じなので 0–1.8 m・1.8–5 m のテクセル密度は不変。5–18 m を 1 段にまとめた（DOF で完全にぼける範囲）。
3. **静的メッシュを 6 m 格子で分割して結合**（`CourtyardMeshes`）。形状・材質・UV は同一。カスケード/反射面/本カメラが格子単位でカリングできる。描画呼び出しは増えるが SRP Batcher 範囲内（render thread 2.3 → 約3.0 ms）。
4. 本カメラの **motion vector パスをOFF**（TAA・モーションブラーなし。消費者がいない）。
5. CPU: `Mesh.GetTangents/SetTangents` のリスト再利用、補助セル線の数値キー化、可視ID文字列を断面更新時のみ生成。
6. `--target-fps N`（既定30、ユーザー承認済みの目標を維持）。`--gpu-ablation` 診断run。`tools/run-validation.ps1 -RunKind GpuAblation -TargetFps 60`。

## before / after

（計測結果は下記の表を参照。ベンチマークは `--benchmark-no-captures --exit-after-run`、撮影なし。）

### 固定カメラ・上限なし（`--gpu-ablation` の baseline 状態、120フレーム）

| | GPU 中央値 | GPU p95 | 非上限 fps | render thread |
|---|---:|---:|---:|---:|
| before（build e8e4c9e3） | 25.6 ms | 26.3 ms | 39.0 | 2.4 ms |
| after | 11.1 ms | 12.4 ms | 約 88 | 3.0 ms |

after の最初の標本（14.0 ms）はシェーダー暖機・GPUクロック上昇を含むため、同一run内で設定を戻した各状態（bloom-off、smaa-off など、描画負荷に影響しない状態）の 11.1〜11.3 ms を定常値とする。再計測（`perf-ablation-after2`、暖機120フレーム＋末尾 baseline-end）は下の表。

### 27手フルシナリオのベンチマーク（`--benchmark-no-captures`、1 run、1920×1080）

`frameMs` は実フレーム間隔。`cpuMain` は main thread の処理時間（present 待ちを含まない）。フレームレート上限ありの run では `FrameTimingManager` の GPU 値が待機時間を含むため、GPU処理時間は上の非上限計測を正とする。

| run | 上限 | フレーム数 | frame 中央値 / p95 | 16.7ms 超 | 33.4ms 超 | cpuMain 中央値 / p95 | 回転中 cpuMain p95 | present wait 中央値 |
|---|---:|---:|---|---:|---:|---|---:|---:|
| before `quality-v2-benchmark-1080` | 30 | 5,879 | 33.33 / 33.34 ms | 100% | 0.5% | 6.3 / 15.9 ms | 16.5 ms | 27.1 ms |
| after `perf-benchmark-after-30` | 30 | 8,489 | 33.33 / 33.34 ms | 100% | 0.6% | 7.5 / 16.7 ms | 17.6 ms | 25.9 ms |
| after `perf-benchmark-after-60` | **60** | 16,887 | **16.67 / 17.01 ms** | 6.7% | 0.0% | 7.6 / 16.9 ms | 17.8 ms | 9.1 ms |

- 33.4ms 超の 0.5〜0.6% は起動直後の cold frame（1 秒前後）で、before/after 共通。
- 60fps 上限では中央値が 16.67 ms に張り付き、16.7ms 超は 6.7%（ほぼ全て 4D 回転中。回転中の frame p95 は 17.9 ms）。回転中は 41 断面の再計算（`sectionMs` p95 約12 ms）で **CPU main が次のボトルネック**になる。GPU には約 5 ms の余裕がある。
- before の 60fps 運用は GPU 25 ms のため不可能だった。

### before/after の GPU 内訳（`gpu-ablation.json`、各状態の GPU 中央値と baseline との差）

| 状態 | 内容 | before GPU ms（差） | after GPU ms（差） |
|---|---|---:|---:|
| baseline | All showcase effects as shipped | 25.6（+0.0） | 11.1（+0.0） |
| sun-shadows-off | Directional shadow maps disabled (F4+Ctrl) | 7.4（-18.2） | 5.5（-5.6） |
| pcss-32-32 | PCSS blocker/filter 32/32 instead of 64/64 | 23.1（-2.5） | 9.6（-1.5） |
| pcss-16-16 | PCSS blocker/filter 16/16 | 21.8（-3.8） | 8.8（-2.3） |
| shadow-res-2048 | Directional cascade resolution 2048 instead of 4096 | 14.7（-10.9） | 8.7（-2.4） |
| cascades-2 | Two cascades for the main camera | 15.6（-10.0） | 9.9（-1.2） |
| foliage-no-shadow-cast | Tree and planter leaves do not cast shadows | 21.8（-3.7） | 10.7（-0.4） |
| foliage-hidden | Tree and planter leaves not rendered at all | 21.2（-4.3） | 10.4（-0.8） |
| environment-hidden | Whole courtyard hidden; board, pieces, sky, post only | 9.1（-16.5） | 7.7（-3.4） |
| cube-probe-off | Per-frame 128px local cubemap disabled | 25.5（-0.0） | 11.2（+0.1） |
| cube-probe-lean | Cubemap keeps rendering every frame but without shadow maps, SSAO, contact shadows, volumetrics | 12.9（-12.7） | 11.2（+0.1） |
| cube-probe-timesliced | Cubemap time sliced across frames | 13.8（-11.7） | 10.1（-1.0） |
| planar-off | Per-frame 512px planar reflection disabled | 22.8（-2.8） | 9.5（-1.6） |
| planar-lean | Planar keeps rendering every frame but without shadow maps, SSAO, contact shadows, volumetrics | 22.0（-3.5） | 9.8（-1.3） |
| planar-256 | Planar reflection at 256px | 22.2（-3.4） | 11.1（-0.0） |
| both-probes-lean | Cubemap and planar both without shadow maps, SSAO, contact shadows, volumetrics | 11.5（-14.1） | 10.0（-1.1） |
| reflections-off | All reflection probes disabled (F6) | 23.1（-2.5） | 9.5（-1.7） |
| ao-off | SSAO intensity 0 (pass skipped) | 25.1（-0.5） | 10.2（-0.9） |
| ao-half-res | SSAO at half resolution with bilateral upsample | 25.4（-0.2） | 10.4（-0.7） |
| ao-4-steps-2-dirs | SSAO 4 steps / 2 directions, full resolution | 25.5（-0.1） | 10.6（-0.5） |
| contact-off | Contact shadows disabled | 25.7（+0.2） | 11.0（-0.2） |
| contact-12-samples | Contact shadows 12 samples instead of 24 | 25.9（+0.3） | 11.1（+0.0） |
| dof-off | Depth of field off (F8) | 25.8（+0.2） | 11.1（-0.1） |
| bloom-off | Bloom off (F7) | 25.9（+0.4） | 11.2（+0.1） |
| cookie-off | Leaf cookie off (F4) | 26.0（+0.4） | 11.1（-0.0） |
| smaa-off | No anti-aliasing | 25.8（+0.3） | 11.1（+0.0） |
| transition-on | Dimensional time-suspension effect at strength .82 | 26.1（+0.5） | 11.2（+0.1） |
| baseline-end | All showcase effects as shipped, sampled again after every state was restored (warm steady state) | – | 11.2（+0.1） |

after 列は `perf-ablation-after2`（暖機120フレーム後に計測、末尾の baseline-end が定常値）。`cube-probe-off` が before で効かない点は未確認事項。



## 見た目の確認

`--lighting-audit` の同一カメラ・同一局面画像を before/after で比較した。

| 画像 | 平均 |差| /255 | 8/255 超の画素 | 24/255 超の画素 | 最大 |
|---|---:|---:|---:|---:|
| lighting-all-on（通常表示） | 0.062 | 0.02% | 0.001% | 62 |
| lighting-off-Reflections | 0.005 | 0.00% | 0.000% | 19 |
| lighting-off-SunShadows | 0.000 | 0.00% | 0.000% | 1 |
| lighting-transition-on | 0.049 | 0.01% | 0.000% | 58 |
| lighting-only-AmbientOcclusion（AOバッファ） | 0.000 | 0.00% | 0.000% | 0 |

- 差分は立方体プローブの反射（花瓶・箱蓋の映り込みに影がなくなった分）に限られ、8倍に増幅した差分画像でようやく見える程度。反射OFFの画像では差がない＝影カスケード3段化と静的メッシュ分割は本カメラの画像に影響していない。
- 比較画像: `evidence/runtime/perf-visual-compare/*-before-after.png`（左 before、右 after）、`*-diff-x8.png`（差分×8）。
- 元画像: `evidence/runtime/perf-lighting-before/`、`evidence/runtime/perf-lighting-after/`。


人間による画質承認は未実施。`docs/APPROVALS.md` の方針に従い、最終判断はユーザー確認待ち。
