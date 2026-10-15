# Standalone性能実測レポート

集計日時（UTC）: 2026-10-15T11:14:26.493718+00:00

これは実行ログから再計算した測定値です。平均fpsやファイルの存在だけで60fps達成・画質合格とは判定していません。
clean benchmark、smoke、撮影、操作自動試験を分離し、異なるrunのフレームを混ぜていません。

## 集計方法と制限

- 中央値は通常の中央値、p95/p99はnearest-rank、最悪値は最大値です。CPU/GPUの0値・未記録値は欠測とします。present waitの0値はCPUタイミングが同時取得されている場合のみ有効です。
- cold/warm: unscaled frameMsを累積した先頭5秒 / 残り。再生速度で拡大されたelapsedは使いません。
- cold/warmは各run内の区分です。同一プロセスの再生ループでは、先頭区間をプロセスの完全なcold起動と呼べません。
- frameMsは実描画フレーム間隔。CPU main/render、GPU、CPU present waitは取得できたタイマーだけを表示し、加算しません。present waitがない旧runは未取得です。
- sectionMsは最後に行った断面更新の所要時間です。更新がないフレームにも同じ値が記録されるため、毎フレームの実作業時間として加算できません。
- monoBytes/totalAllocatedBytesはメモリー占有値。傾きは占有量の推移で、割当バイト/秒ではありません。新runのGCは世代0/1/2を分離して表示し、合計しません。旧smokeの世代合計は重複を含む可能性があり、新runの世代0カウンターと比較しません。
- ヘッダーのVRAM値はハードウェア容量です。外部WDDMカウンターがある場合はプロセス専用/共有GPUメモリー使用量を別表示し、厳密な物理常駐VRAMと同一視しません。
- build GUID/sourceHash/sourceManifestSha256の完全一致があるmanifestのみ関連付けます。新しいmanifestを古いrunへ推測で割り当てません。

## run一覧

| 分類 | run | 解像度 / profile / mode | フレーム | 全体 median / p95 / p99 / worst ms | 完走記録 | source対応 |
|---|---|---|---:|---|---|---|
| smoke | 20261015-104859-162-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 4596 | 16.67 / 16.67 / 16.69 / 263.64 | INCOMPLETE_OR_DIAGNOSTIC | EXACT_MANIFEST_MATCH |
| smoke | 20261015-105122-053-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 1575 | 16.67 / 17.10 / 75.30 / 1425.00 | RECORDED_COMPLETE | EXACT_MANIFEST_MATCH |
| smoke | 20261015-105817-056-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 269 | 16.67 / 17.03 / 981.59 / 1480.22 | INCOMPLETE_OR_DIAGNOSTIC | EXACT_MANIFEST_MATCH |
| automation | 20261015-103738-773-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 147 | 16.67 / 16.68 / 49.89 / 706.41 | INCOMPLETE_OR_DIAGNOSTIC | UNJOINED |
| automation | 20261015-103741-819-ShowcaseRaster-2 | 1280×720 / ShowcaseRaster / DirectedLive | 2027 | 16.67 / 16.67 / 32.91 / 93.81 | RECORDED_COMPLETE | UNJOINED |
| automation | 20261015-103816-309-ShowcaseRaster-3 | 1280×720 / ShowcaseRaster / DirectedLive | 1846 | 16.67 / 16.68 / 32.20 / 58.80 | INCOMPLETE_OR_DIAGNOSTIC | UNJOINED |
| automation | 20261015-103847-699-ShowcaseRaster-4 | 1280×720 / ShowcaseRaster / DirectedLive | 1 | 16.67 / 16.67 / 16.67 / 16.67 | INCOMPLETE_OR_DIAGNOSTIC | UNJOINED |
| benchmark-no-capture | 20261015-111220-651-ShowcaseRaster-1 | 1920×1080 / ShowcaseRaster / DirectedLive | 5789 | 16.67 / 16.67 / 34.44 / 706.31 | RECORDED_COMPLETE | EXACT_MANIFEST_MATCH |
| smoke | 20261015-102751-006-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 1911 | 16.67 / 16.67 / 32.09 / 993.51 | RECORDED_COMPLETE | UNJOINED |
| smoke | 20261015-103617-833-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 1528 | 16.67 / 16.96 / 36.47 / 1109.72 | RECORDED_COMPLETE | UNJOINED |
| smoke | 20261015-110556-809-ShowcaseRaster-1 | 1280×720 / ShowcaseRaster / DirectedLive | 316 | 16.67 / 16.98 / 1373.08 / 1456.19 | INCOMPLETE_OR_DIAGNOSTIC | EXACT_MANIFEST_MATCH |
| smoke | 20261015-110739-900-GeometryReference-1 | 1280×720 / GeometryReference / DirectedLive | 316 | 16.67 / 16.87 / 1371.56 / 1425.11 | INCOMPLETE_OR_DIAGNOSTIC | EXACT_MANIFEST_MATCH |

## 撮影なしbenchmark


## 20261015-104859-162-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/candidate-pass-smoke/20261015-104859-162-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 4596 | 16.67 / 16.67 / 16.69 / 263.64 | 0.21 / 0.64 | 未取得 / 未取得 | 61.58% |
| cold | 283 | 16.67 / 16.67 / 25.40 / 263.64 | 0.33 / 2.10 | 未取得 / 未取得 | 59.36% |
| warm | 4313 | 16.67 / 16.67 / 16.68 / 33.92 | 0.21 / 0.37 | 未取得 / 未取得 | 61.72% |
| Scene 1 | 356 | 16.67 / 16.67 / 17.13 / 263.64 | 0.35 / 1.94 | 未取得 / 未取得 | 60.11% |
| Scene 2 | 4240 | 16.67 / 16.67 / 16.67 / 33.92 | 0.20 / 0.36 | 未取得 / 未取得 | 61.70% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 136577928 → 136628311 bytes、最大 143194764 bytes。
Mono占有の先頭→末尾: 5791744 → 6152192 bytes。
GCカウンター末尾: gen0=625 / gen1=625 / gen2=625。世代を合計していません。
CPU present wait median/p95/p99/worst: 16.45 / 16.56 / 16.58 / 17.50 ms。
外部プロセス計測: PID 37372、76 samples、launcher終了状態 EXITED_NONZERO / exitCode -1。
Private bytes先頭→末尾: 435372032 → 439713792、最大 443002880。
Working set先頭→末尾: 272076800 → 276226048、最大 276226048。
WDDM専用GPUメモリー最大 76591104 bytes、共有GPUメモリー最大 24801280 bytes。厳密な物理常駐VRAMとの同一視はしていません。
外部サンプリング処理時間 median/p95: 20.49 / 29.20 ms。測定負荷が加わります。
GPU合計値の不一致を 76 samplesで検出。元JSONLは変更せず、保存済みアダプター別生値から合計を再計算しました。旧報告値・補正値・根拠はanalysis.jsonに保持。
H90の報告可視枚数: 未取得。

読込上の注意: evidence/runtime/candidate-pass-smoke/20261015-104859-162-ShowcaseRaster-1/frames.jsonl:4597: Unterminated string starting at: line 1 column 426 (char 425); row excluded

## 20261015-105122-053-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/candidate-visible-smoke/20261015-105122-053-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 1575 | 16.67 / 17.10 / 75.30 / 1425.00 | 5.20 / 16.62 | 5.12 / 6.14 | 55.87% |
| cold | 78 | 16.67 / 120.69 / 1425.00 / 1425.00 | 6.36 / 120.68 | 5.81 / 8.34 | 57.69% |
| warm | 1497 | 16.67 / 16.98 / 43.23 / 1417.30 | 5.12 / 16.55 | 5.10 / 6.10 | 55.78% |
| Scene 1 | 213 | 16.67 / 17.05 / 1387.96 / 1425.00 | 6.67 / 10.27 | 5.90 / 7.55 | 56.34% |
| Scene 2 | 61 | 16.67 / 16.67 / 1417.30 / 1417.30 | 5.63 / 10.54 | 5.47 / 6.04 | 49.18% |
| Scene 3 | 58 | 16.67 / 16.67 / 1384.83 / 1384.83 | 4.94 / 75.45 | 5.32 / 6.17 | 53.45% |
| Scene 4 | 428 | 16.67 / 27.66 / 43.23 / 1378.28 | 9.44 / 27.66 | 5.25 / 6.08 | 58.41% |
| Scene 5 | 815 | 16.67 / 16.67 / 35.80 / 1368.67 | 4.26 / 12.97 | 5.02 / 5.61 | 55.09% |
| 40–50° | 30 | 16.67 / 16.67 / 16.67 / 16.67 | 9.93 / 12.30 | 5.07 / 5.51 | 53.33% |
| H0帰還後1秒 | 59 | 16.67 / 16.67 / 37.01 / 37.01 | 3.95 / 38.95 | 5.22 / 5.76 | 52.54% |
| 終局ply先頭2秒 | 37 | 16.67 / 62.68 / 1368.02 / 1368.02 | 3.88 / 1368.01 | 5.38 / 5.66 | 48.65% |

Unityメモリー占有の先頭→末尾: 136577944 → 183018207 bytes、最大 252398561 bytes。
Mono占有の先頭→末尾: 5816320 → 71503872 bytes。
GCカウンター末尾: gen0=1791 / gen1=1791 / gen2=1791。世代を合計していません。
CPU present wait median/p95/p99/worst: 11.46 / 13.13 / 13.27 / 13.42 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-105817-056-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/final-smoke/20261015-105817-056-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 269 | 16.67 / 17.03 / 981.59 / 1480.22 | 5.11 / 9.10 | 5.10 / 6.09 | 56.51% |
| cold | 74 | 16.67 / 420.10 / 1480.22 / 1480.22 | 4.93 / 420.08 | 5.31 / 6.31 | 62.16% |
| warm | 195 | 16.67 / 16.67 / 110.64 / 1416.13 | 5.17 / 7.51 | 5.09 / 5.98 | 54.36% |
| Scene 1 | 210 | 16.67 / 37.07 / 981.59 / 1480.22 | 5.13 / 9.10 | 5.10 / 6.14 | 57.14% |
| Scene 2 | 59 | 16.67 / 16.67 / 98.52 / 98.52 | 5.01 / 9.21 | 5.22 / 5.73 | 54.24% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 136579896 → 251657209 bytes、最大 251657209 bytes。
Mono占有の先頭→末尾: 5808128 → 11329536 bytes。
GCカウンター末尾: gen0=255 / gen1=255 / gen2=255。世代を合計していません。
CPU present wait median/p95/p99/worst: 11.57 / 12.68 / 13.00 / 38.87 ms。
外部プロセス計測: PID 19020、11 samples、launcher終了状態 EXITED_NONZERO / exitCode 1。
Private bytes先頭→末尾: 1434193920 → 1723404288、最大 1723404288。
Working set先頭→末尾: 720711680 → 987279360、最大 987279360。
WDDM専用GPUメモリー最大 641994752 bytes、共有GPUメモリー最大 183549952 bytes。厳密な物理常駐VRAMとの同一視はしていません。
外部サンプリング処理時間 median/p95: 24.49 / 30.41 ms。測定負荷が加わります。
GPU合計値の不一致を 11 samplesで検出。元JSONLは変更せず、保存済みアダプター別生値から合計を再計算しました。旧報告値・補正値・根拠はanalysis.jsonに保持。
H90の報告可視枚数: 未取得。

## 20261015-103738-773-ShowcaseRaster-1

分類: **automation**。証跡: `evidence/runtime/playback/20261015-103738-773-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: screenshots。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 147 | 16.67 / 16.68 / 49.89 / 706.41 | 1.71 / 5.11 | 1.64 / 2.27 | 51.02% |
| cold | 147 | 16.67 / 16.68 / 49.89 / 706.41 | 1.71 / 5.11 | 1.64 / 2.27 | 51.02% |
| warm | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| Scene 1 | 147 | 16.67 / 16.68 / 49.89 / 706.41 | 1.71 / 5.11 | 1.64 / 2.27 | 51.02% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 136633568 → 176773463 bytes、最大 183625410 bytes。
Mono占有の先頭→末尾: 5767168 → 10149888 bytes。
旧形式GC世代合計の末尾: 153。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: 未取得。

## 20261015-103741-819-ShowcaseRaster-2

分類: **automation**。証跡: `evidence/runtime/playback/20261015-103741-819-ShowcaseRaster-2`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: screenshots。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 2027 | 16.67 / 16.67 / 32.91 / 93.81 | 2.13 / 10.01 | 1.96 / 2.32 | 53.03% |
| cold | 296 | 16.67 / 16.67 / 16.69 / 93.81 | 1.99 / 3.57 | 1.76 / 2.25 | 50.34% |
| warm | 1731 | 16.67 / 16.67 / 33.66 / 40.07 | 2.16 / 12.03 | 1.98 / 2.32 | 53.50% |
| Scene 1 | 367 | 16.67 / 16.67 / 16.72 / 93.81 | 2.05 / 3.57 | 1.75 / 2.21 | 50.41% |
| Scene 2 | 71 | 16.67 / 16.67 / 16.67 / 16.67 | 2.27 / 3.29 | 1.76 / 2.03 | 46.48% |
| Scene 3 | 115 | 16.67 / 16.67 / 16.93 / 18.17 | 1.74 / 3.32 | 1.76 / 1.85 | 53.04% |
| Scene 4 | 523 | 16.67 / 17.87 / 37.13 / 40.07 | 2.69 / 17.86 | 1.99 / 2.21 | 56.21% |
| Scene 5 | 951 | 16.67 / 16.67 / 32.30 / 37.99 | 1.98 / 9.27 | 2.00 / 2.48 | 52.79% |
| 40–50° | 31 | 16.67 / 16.67 / 16.67 / 16.67 | 6.51 / 8.59 | 2.02 / 2.16 | 54.84% |
| H0帰還後1秒 | 60 | 16.67 / 16.67 / 32.90 / 32.90 | 1.49 / 32.80 | 1.78 / 1.98 | 48.33% |
| 終局ply先頭2秒 | 120 | 16.67 / 16.67 / 16.67 / 20.39 | 1.61 / 2.66 | 1.75 / 1.85 | 47.50% |

Unityメモリー占有の先頭→末尾: 177358455 → 176991063 bytes、最大 183874930 bytes。
Mono占有の先頭→末尾: 10145792 → 10919936 bytes。
旧形式GC世代合計の末尾: 6039。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-103816-309-ShowcaseRaster-3

分類: **automation**。証跡: `evidence/runtime/playback/20261015-103816-309-ShowcaseRaster-3`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: screenshots。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 1846 | 16.67 / 16.68 / 32.20 / 58.80 | 1.97 / 11.05 | 1.96 / 2.22 | 53.85% |
| cold | 298 | 16.67 / 16.68 / 17.05 / 58.80 | 1.76 / 3.34 | 1.78 / 2.00 | 52.35% |
| warm | 1548 | 16.67 / 16.67 / 32.75 / 36.62 | 2.02 / 11.74 | 1.97 / 2.27 | 54.13% |
| Scene 1 | 370 | 16.67 / 16.68 / 16.89 / 58.80 | 1.86 / 3.41 | 1.77 / 2.01 | 51.35% |
| Scene 2 | 71 | 16.67 / 16.67 / 16.72 / 16.72 | 2.13 / 4.29 | 1.76 / 1.88 | 53.52% |
| Scene 3 | 116 | 16.67 / 16.67 / 16.93 / 17.08 | 1.85 / 3.33 | 1.80 / 1.89 | 55.17% |
| Scene 4 | 499 | 16.67 / 17.84 / 33.99 / 36.62 | 3.05 / 17.84 | 1.97 / 2.34 | 55.91% |
| Scene 5 | 790 | 16.67 / 16.67 / 31.25 / 34.57 | 1.83 / 10.63 | 2.00 / 2.32 | 53.54% |
| 40–50° | 30 | 16.67 / 16.67 / 16.67 / 16.67 | 6.69 / 8.24 | 2.01 / 2.82 | 53.33% |
| H0帰還後1秒 | 60 | 16.67 / 16.67 / 32.77 / 32.77 | 1.62 / 32.07 | 1.78 / 1.89 | 60.00% |
| 終局ply先頭2秒 | 2 | 16.74 / 16.82 / 16.82 / 16.82 | 1.82 / 1.85 | 1.91 / 1.91 | 100.00% |

Unityメモリー占有の先頭→末尾: 177536711 → 177005999 bytes、最大 183918514 bytes。
Mono占有の先頭→末尾: 10100736 → 10903552 bytes。
旧形式GC世代合計の末尾: 6078。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-103847-699-ShowcaseRaster-4

分類: **automation**。証跡: `evidence/runtime/playback/20261015-103847-699-ShowcaseRaster-4`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: screenshots。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 1 | 16.67 / 16.67 / 16.67 / 16.67 | 3.10 / 3.10 | 1.75 / 1.75 | 100.00% |
| cold | 1 | 16.67 / 16.67 / 16.67 / 16.67 | 3.10 / 3.10 | 1.75 / 1.75 | 100.00% |
| warm | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| Scene 1 | 1 | 16.67 / 16.67 / 16.67 / 16.67 | 3.10 / 3.10 | 1.75 / 1.75 | 100.00% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 177552655 → 177552655 bytes、最大 177552655 bytes。
Mono占有の先頭→末尾: 10121216 → 10121216 bytes。
旧形式GC世代合計の末尾: 39。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: 未取得。

## 20261015-111220-651-ShowcaseRaster-1

分類: **benchmark-no-capture**。証跡: `evidence/runtime/preflight-benchmark-1080/20261015-111220-651-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1920×1080、VRAM容量: 16050 MB。
撮影設定: none / clean benchmark。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 5789 | 16.67 / 16.67 / 34.44 / 706.31 | 4.64 / 12.91 | 6.10 / 6.63 | 54.10% |
| cold | 254 | 16.67 / 16.67 / 43.78 / 706.31 | 4.33 / 5.86 | 6.35 / 7.05 | 49.21% |
| warm | 5535 | 16.67 / 16.67 / 34.33 / 41.71 | 4.66 / 13.07 | 6.09 / 6.60 | 54.33% |
| Scene 1 | 1029 | 16.67 / 16.67 / 16.85 / 706.31 | 4.53 / 6.30 | 6.10 / 6.85 | 52.38% |
| Scene 2 | 211 | 16.67 / 16.67 / 16.67 / 16.80 | 5.02 / 6.55 | 6.05 / 6.69 | 52.13% |
| Scene 3 | 332 | 16.67 / 16.67 / 16.81 / 20.42 | 4.44 / 5.97 | 5.99 / 6.67 | 51.20% |
| Scene 4 | 1467 | 16.67 / 19.40 / 36.94 / 39.22 | 5.89 / 19.48 | 6.19 / 6.77 | 55.62% |
| Scene 5 | 2750 | 16.67 / 16.67 / 32.39 / 41.71 | 4.41 / 10.60 | 6.06 / 6.46 | 54.44% |
| 40–50° | 95 | 16.67 / 16.67 / 16.78 / 16.78 | 9.13 / 10.68 | 6.17 / 6.50 | 53.68% |
| H0帰還後1秒 | 59 | 16.67 / 16.67 / 33.64 / 33.64 | 4.14 / 36.61 | 6.26 / 6.62 | 62.71% |
| 終局ply先頭2秒 | 120 | 16.67 / 16.67 / 16.68 / 16.69 | 4.18 / 5.34 | 6.04 / 6.58 | 55.00% |

Unityメモリー占有の先頭→末尾: 136579696 → 188079647 bytes、最大 188082607 bytes。
Mono占有の先頭→末尾: 5816320 → 10878976 bytes。
GCカウンター末尾: gen0=4495 / gen1=4495 / gen2=4495。世代を合計していません。
CPU present wait median/p95/p99/worst: 12.03 / 12.96 / 13.12 / 14.82 ms。
外部プロセス計測: PID 35412、100 samples、launcher終了状態 EXITED_ZERO / exitCode 0。
Private bytes先頭→末尾: 442851328 → 1206083584、最大 1304027136。
Working set先頭→末尾: 263913472 → 376455168、最大 447045632。
WDDM専用GPUメモリー最大 723693568 bytes、共有GPUメモリー最大 21114880 bytes。厳密な物理常駐VRAMとの同一視はしていません。
外部サンプリング処理時間 median/p95: 21.49 / 29.53 ms。測定負荷が加わります。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-102751-006-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/smoke/20261015-102751-006-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: 未記録。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 1911 | 16.67 / 16.67 / 32.09 / 993.51 | 1.65 / 9.89 | 1.83 / 2.05 | 54.89% |
| cold | 195 | 16.67 / 16.68 / 685.44 / 993.51 | 1.65 / 3.78 | 1.70 / 1.85 | 55.38% |
| warm | 1716 | 16.67 / 16.67 / 31.89 / 62.70 | 1.65 / 10.40 | 1.85 / 2.05 | 54.84% |
| Scene 1 | 287 | 16.67 / 16.67 / 95.04 / 993.51 | 1.75 / 3.28 | 1.69 / 1.81 | 55.75% |
| Scene 2 | 71 | 16.67 / 16.67 / 16.67 / 16.67 | 1.72 / 2.92 | 1.68 / 1.85 | 59.15% |
| Scene 3 | 115 | 16.67 / 16.67 / 16.67 / 16.88 | 1.38 / 2.70 | 1.70 / 1.78 | 53.91% |
| Scene 4 | 491 | 16.67 / 16.73 / 57.42 / 62.70 | 2.59 / 16.54 | 1.89 / 2.08 | 54.99% |
| Scene 5 | 947 | 16.67 / 16.67 / 29.43 / 34.47 | 1.40 / 8.09 | 1.87 / 2.04 | 54.38% |
| 40–50° | 31 | 16.67 / 16.67 / 16.67 / 16.67 | 6.00 / 7.34 | 1.91 / 2.16 | 61.29% |
| H0帰還後1秒 | 58 | 16.67 / 16.67 / 58.22 / 58.22 | 1.71 / 58.00 | 1.74 / 2.07 | 44.83% |
| 終局ply先頭2秒 | 120 | 16.67 / 16.67 / 16.67 / 16.71 | 1.14 / 2.23 | 1.68 / 1.79 | 52.50% |

Unityメモリー占有の先頭→末尾: 136296408 → 178882279 bytes、最大 185894152 bytes。
Mono占有の先頭→末尾: 5844992 → 9891840 bytes。
旧形式GC世代合計の末尾: 6057。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-103617-833-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/textured-smoke/20261015-103617-833-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: UNJOINED。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 1528 | 16.67 / 16.96 / 36.47 / 1109.72 | 2.27 / 13.94 | 1.95 / 2.24 | 56.09% |
| cold | 130 | 16.67 / 16.92 / 1069.49 / 1099.73 | 2.41 / 8.42 | 1.80 / 1.94 | 54.62% |
| warm | 1398 | 16.67 / 16.97 / 35.78 / 1109.72 | 2.25 / 14.68 | 1.95 / 2.25 | 56.22% |
| Scene 1 | 220 | 16.67 / 16.86 / 1055.12 / 1099.73 | 2.38 / 4.78 | 1.79 / 1.93 | 55.91% |
| Scene 2 | 61 | 16.67 / 16.67 / 1092.12 / 1092.12 | 2.47 / 5.37 | 1.83 / 1.93 | 54.10% |
| Scene 3 | 27 | 16.67 / 606.72 / 1065.51 / 1065.51 | 1.97 / 1065.51 | 7.25 / 8.69 | 51.85% |
| Scene 4 | 386 | 16.67 / 30.94 / 242.28 / 1109.72 | 5.13 / 29.68 | 1.90 / 2.17 | 57.51% |
| Scene 5 | 834 | 16.67 / 16.77 / 33.77 / 1087.80 | 2.08 / 11.62 | 1.98 / 2.27 | 55.76% |
| 40–50° | 22 | 16.67 / 16.67 / 16.99 / 16.99 | 6.19 / 7.46 | 1.93 / 2.13 | 59.09% |
| H0帰還後1秒 | 59 | 16.67 / 16.67 / 34.98 / 34.98 | 1.54 / 36.00 | 1.80 / 1.96 | 50.85% |
| 終局ply先頭2秒 | 57 | 16.67 / 16.68 / 1068.91 / 1068.91 | 1.87 / 13.56 | 1.84 / 2.07 | 52.63% |

Unityメモリー占有の先頭→末尾: 136567832 → 177338927 bytes、最大 228525982 bytes。
Mono占有の先頭→末尾: 5828608 → 55087104 bytes。
旧形式GC世代合計の末尾: 5550。重複の可能性があるため、新形式gen0値と比較しません。
CPU present wait median/p95/p99/worst: 未取得 / 未取得 / 未取得 / 未取得 ms。
外部PIDメモリー/GPUメモリー計測: NOT_AVAILABLE。
H90の報告可視枚数: ply 6: 4枚, ply 7: 4枚, ply 8: 4枚, ply 9: 4枚, ply 10: 4枚, ply 11: 4枚, ply 12: 4枚, ply 13: 4枚, ply 14: 4枚, ply 15: 4枚, ply 16: 4枚, ply 17: 4枚, ply 18: 4枚。
H0帰還後の主役ID非表示: True。対象ID集合・state hashはanalysis.jsonに保存。

## 20261015-110556-809-ShowcaseRaster-1

分類: **smoke**。証跡: `evidence/runtime/validated-passes/20261015-110556-809-ShowcaseRaster-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 316 | 16.67 / 16.98 / 1373.08 / 1456.19 | 5.24 / 8.93 | 5.25 / 6.94 | 55.70% |
| cold | 70 | 16.67 / 120.95 / 1456.19 / 1456.19 | 5.33 / 120.94 | 5.37 / 6.59 | 61.43% |
| warm | 246 | 16.67 / 16.68 / 129.93 / 1432.08 | 5.18 / 8.20 | 5.18 / 6.94 | 54.07% |
| Scene 1 | 212 | 16.67 / 16.98 / 1397.80 / 1456.19 | 5.36 / 8.93 | 5.23 / 7.12 | 55.19% |
| Scene 2 | 61 | 16.67 / 16.67 / 1373.08 / 1373.08 | 4.81 / 8.83 | 5.30 / 5.71 | 57.38% |
| Scene 3 | 43 | 16.67 / 17.01 / 102.28 / 102.28 | 5.16 / 8.20 | 5.24 / 6.64 | 55.81% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 136579976 → 252107705 bytes、最大 252107705 bytes。
Mono占有の先頭→末尾: 5726208 → 10964992 bytes。
GCカウンター末尾: gen0=345 / gen1=345 / gen2=345。世代を合計していません。
CPU present wait median/p95/p99/worst: 11.42 / 12.41 / 12.98 / 13.14 ms。
外部プロセス計測: PID 18612、15 samples、launcher終了状態 EXITED_NONZERO / exitCode 1。
Private bytes先頭→末尾: 427012096 → 759881728、最大 1842548736。
Working set先頭→末尾: 257896448 → 504438784、最大 1012838400。
WDDM専用GPUメモリー最大 655437824 bytes、共有GPUメモリー最大 157433856 bytes。厳密な物理常駐VRAMとの同一視はしていません。
外部サンプリング処理時間 median/p95: 22.40 / 71.68 ms。測定負荷が加わります。
H90の報告可視枚数: 未取得。

## 20261015-110739-900-GeometryReference-1

分類: **smoke**。証跡: `evidence/runtime/validated-reference-passes/20261015-110739-900-GeometryReference-1`。
GPU: NVIDIA GeForce RTX 5060 Ti / API: Direct3D11 / 記録driver欄: Direct3D 11.0 [level 11.1]。
AA: SMAA、内部解像度: 100%、出力: 1280×720、VRAM容量: 16050 MB。
撮影設定: GPU pass readback and screenshots。build/source対応: EXACT_MANIFEST_MATCH。

| 区間 | n | frame median/p95/p99/worst ms | CPU main median/p95 ms | GPU median/p95 ms | 16.67ms超の割合 |
|---|---:|---|---|---|---|
| 全体 | 316 | 16.67 / 16.87 / 1371.56 / 1425.11 | 4.79 / 7.77 | 4.96 / 6.15 | 58.86% |
| cold | 82 | 16.67 / 119.12 / 1413.04 / 1413.04 | 4.72 / 132.17 | 5.22 / 7.36 | 60.98% |
| warm | 234 | 16.67 / 16.77 / 102.73 / 1425.11 | 4.81 / 7.12 | 4.94 / 6.12 | 58.12% |
| Scene 1 | 211 | 16.67 / 16.86 / 1401.38 / 1425.11 | 4.72 / 7.77 | 4.91 / 6.28 | 59.72% |
| Scene 2 | 61 | 16.67 / 16.67 / 1371.56 / 1371.56 | 4.87 / 7.15 | 5.01 / 5.52 | 57.38% |
| Scene 3 | 44 | 16.67 / 17.04 / 102.73 / 102.73 | 5.12 / 7.54 | 5.40 / 6.45 | 56.82% |
| 40–50° | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| H0帰還後1秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |
| 終局ply先頭2秒 | 0 | 未取得 / 未取得 / 未取得 / 未取得 | 未取得 / 未取得 | 未取得 / 未取得 | 未取得% |

Unityメモリー占有の先頭→末尾: 136579848 → 251625465 bytes、最大 251625465 bytes。
Mono占有の先頭→末尾: 5758976 → 11005952 bytes。
GCカウンター末尾: gen0=338 / gen1=338 / gen2=338。世代を合計していません。
CPU present wait median/p95/p99/worst: 11.87 / 12.87 / 13.05 / 13.19 ms。
外部プロセス計測: PID 4548、14 samples、launcher終了状態 EXITED_NONZERO / exitCode 1。
Private bytes先頭→末尾: 536702976 → 1768448000、最大 1775988736。
Working set先頭→末尾: 273514496 → 975638528、最大 975638528。
WDDM専用GPUメモリー最大 632041472 bytes、共有GPUメモリー最大 153108480 bytes。厳密な物理常駐VRAMとの同一視はしていません。
外部サンプリング処理時間 median/p95: 21.83 / 31.50 ms。測定負荷が加わります。
H90の報告可視枚数: 未取得。

全タイマーのp99/最悪、場面別cold/warm、メモリー回帰傾き、H90/帰還後のID一覧、元ファイルSHA-256は `evidence/runtime/analysis.json` にあります。
60fpsの厳密合否、画質・理解しやすさの人間承認、未取得の計測項目を、この集計で自動合格にはしていません。
