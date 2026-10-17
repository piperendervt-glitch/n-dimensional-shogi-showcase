# N-dimensional Shogi — Showcase

## 遊び方（四次元将棋）

1. `Build.cmd` をダブルクリックしてビルドします（Unity 6000.3.11f1 が必要、数分かかります）。
2. `Play.cmd`、または `release\NDShogiPivot\NDShogiPivot.exe` をダブルクリックして起動します。

他のPCへ渡すときは `release\NDShogiPivot-Windows.zip` を展開して `NDShogiPivot.exe` を実行します。設定・対局の保存先は `%USERPROFILE%\AppData\LocalLow\piperendervt-glitch\四次元将棋` です。ビルド済みEXEは約600MBあるためリポジトリには含めません（配布は GitHub Releases への zip 添付を想定）。

試験用の厳密な起動（`--pivot-request-id` などの識別用引数を付けた起動）では、従来どおりビルドとソースの照合を行います。

## フォルダ構成

| 場所 | 内容 |
|---|---|
| `Build.cmd` / `Play.cmd` | ビルド／起動用（ダブルクリック） |
| `Unity/` | Unityプロジェクト（四次元将棋の `Cfg2` シーン、旧Showcaseデモ） |
| `src/` | Unity非依存のルール・幾何ライブラリ（Core / Geometry） |
| `tests/` | 純C#・Pythonの試験 |
| `tools/` | ビルド・計測スクリプト（`build-game.ps1` が四次元将棋、`build-windows.ps1` が旧Showcase） |
| `scenarios/`, `assets/` | 棋譜・シナリオ、素材の原本と出典 |
| `docs/` | 設計・性能・承認の文書。`docs/history/` は各作業回の依頼・設計・報告の記録 |
| `evidence/` | 計測・検証の記録 |
| `release/` | ビルド出力（git管理外） |

## 旧Showcaseデモ

屋外の木製盤で、2Dの対局に見える導入から3Dの高さ方向への逃走と追撃を3組繰り返し、4Dへ移る27手のWindowsデモです。4DではAの1手ごとに初期の3D表示（H0）へ戻り、Bが銀・金・飛車・歩を使って応手します。Bの観測に4Dの情報は入りません。最後はAが王を捕獲します。

既定は1080p・30FPSを目標とする画質設定です。木材はCC0のPBRスキャン、駒名は高解像度の完成語画像を使用し、木漏れ日・接地影・動的反射を加えています。次元回転はゆっくり進み、ShowcaseRasterでは画面全体の時間停止風エフェクトを伴います。目標FPSの達成状況は実測結果を参照してください。描画負荷の原因分析と最適化（2026-10-15）は [docs/RENDER_OPTIMIZATION_JA.md](docs/RENDER_OPTIMIZATION_JA.md) を参照してください。

これは**独自の固定4Dデモルール**です。本将棋完全互換や、自律的な強さの実証を目的としません。歩は前進に加えて非前方軸へ横断・捕獲できます。成り・打ち・自玉安全による着手拒否はなく、王捕獲で終局します。

## 起動

`builds/Showcase-Windows/NDShogiShowcase.exe` を実行します。隣接するデータフォルダー・DLLも必要です。起動メニューはなく、自動で始まり、終局後に停止します。

| キー | 操作 |
|---|---|
| Space | 一時停止・再開 |
| R | 初期配置から再生し直す |
| F1 | 診断表示を切り替える |
| T | 次元回転の画面全体エフェクトを切り替える（ShowcaseRaster） |
| H | 補助セル表示を切り替える |
| F9 | スクリーンショット |
| Esc | 終了確認を開く／閉じる |
| Enter | 終了確認中にアプリを終了 |

終了確認中は再生を停止します。F1は時間・カメラ・局面を変更しません。

| キー | 照明・材質を確認する切り替え |
|---|---|
| F2 | ACESトーンマッピング／なし（固定露出は同じ） |
| F3 | 間接拡散光・暖色補助光 |
| F4 | 木漏れ日cookie |
| Ctrl+F4 | 太陽の実形状シャドウマップ |
| F5 | AO・接地影を一緒にON/OFF |
| Ctrl+F5 | 接地影のみON/OFF |
| F6 | 平面反射・キューブマップ・空の反射光 |
| F7 | Bloom |
| F8 | 背景ぼけ（DOF） |
| Shift+F5 | 実AOバッファ／通常表示 |
| Shift+F4 | 実接地影バッファ／通常表示 |

Shift付き診断は再度押すと通常表示に戻ります。白が非遮蔽、黒が遮蔽です。接地影の診断を太陽の影全体の診断とは扱いません。設定と実装上の範囲は [照明の説明](docs/LIGHTING_V2_JA.md) に記録しています。

## 実行設定

PowerShellからの例:

```powershell
& .\builds\Showcase-Windows\NDShogiShowcase.exe --profile GeometryReference --mode DirectedLive --shape A --evidence C:\dev\n-dimensional-shogi-showcase\evidence\runtime
```

| 引数 | 設定 |
|---|---|
| `--profile ShowcaseRaster` | 既定。PBR木材、自然光、木漏れ日、反射、背景ぼけ、次元回転エフェクト |
| `--profile GeometryReference` | SMAA、DOF/Bloom・次元回転エフェクトなし。断面検査用 |
| `--mode DirectedLive` | 既定。実際のCPUが毎手候補を評価。強い演出上の選好を含む |
| `--mode VerifiedReplay` | 同梱の検証済み棋譜を毎手再検証。CPU探索を行わない |
| `--shape C` / `--shape A` | 固定u/w対称形／数学基準の押し出し形。既定C。人間の最終選択は未実施 |
| `--occlusion lattice-line/1` | 完成シナリオの既定遮蔽 |
| `--occlusion supercover-closed/1` | 閉セルへの接触も遮る別run。比較用 |
| `--helpers-off` | 補助セルなし |
| `--loops 3` | 指定回数だけ独立したrunを再生 (1〜20) |
| `--speed 2` | 検証用の時間倍率。品質・実時間性能評価は1を使用 |
| `--capture-video` | 実描画フレームをJPEGと時刻へ記録。性能測定とは別run |
| `--capture-passes` | 実際のHDRP Color/Depth/Normalと反射のGPU読み戻し。検査画像・EXR・監査JSONを保存 |
| `--benchmark-no-captures` | スクリーンショット・動画・GPU読み戻しを行わない性能測定。記録オプションとの併用を拒否 |
| `--target-fps 60` | フレームレート上限（既定30）。最適化後の1080p計測では60で中央値16.67msが成立（4D回転中に一部超過）。詳細は [描画最適化](docs/RENDER_OPTIMIZATION_JA.md) |
| `--exit-after-run` | 指定run完了後に終了 |
| `--shape-comparison` | A/Cを同じカメラ・光で0/30/45/60/90度撮影する診断run |
| `--gpu-ablation` | 固定カメラ・固定局面で効果を1つずつOFFにし、GPU/フレーム時間を `gpu-ablation.json` へ記録する診断run。フレームレート上限なし |
| `--evidence PATH` | 記録先。省略時はUnity persistentDataPath以下 |

Unityの `-screen-width 1920 -screen-height 1080 -screen-fullscreen 0` も使用できます。

VerifiedReplayは同梱棋譜のseed・遮蔽に一致する設定を要求します。不一致時は停止し、DirectedLiveへ切り替えません。

## 再現と検証

Unity Editorは6000.3.11f1、HDRPは17.3.0です。純粋C#部分は.NET 9 SDKで検証できます。

```powershell
dotnet run --project tests/Showcase.Core.Tests -c Release -- evidence/core
dotnet run --project tests/Showcase.Geometry.Tests -c Release -- evidence/geometry
dotnet run --project tools/Showcase.Console -c Release -- --output evidence/core
```

ビルド操作は `tools/build-windows.ps1` または `Unity/Assets/Editor/ShowcaseBuild.cs` のエントリーポイントを参照してください。実際の手順・ログは `docs/IMPLEMENTATION_REPORT_JA.md` に集約します。

`tools/encode_video.py FRAME_DIRECTORY OUTPUT.avi` は記録されたJPEGを時刻に合わせてMotion-JPEG AVIへまとめます。記録時のサンプリングを動画にする処理で、ゲームの描画fpsやフレーム生成の成果ではありません。

`--automation-playback --loops 3 --automation-exit --automation-evidence PATH` は、一時停止・再開・予約中リスタート・終了確認を実行アプリ内で試す検証用ハーネスです。通常起動では動きません。キーハンドラーと同じ操作メソッドを呼びますが、OSからのキー配送試験は含みません。

`tools/analyze_runtime.py` は実行記録を集計します。撮影・操作・smokeとclean benchmarkを分離し、同一build/sourceの記録だけを対応付けます。結果は `evidence/runtime/analysis.json` と `docs/PERFORMANCE.md` を参照してください。

## 技術的な境界

ルール、観測、CPUはUnity非依存です。Bの探索モデルにはw座標がなく、論理w=0だけを渡します。描画の見え方はBの観測を変えません。盤・駒は固定された4D半空間と観察超平面の交差から生成します。フェードや角度別モデル交換で断面を代用しません。

画質・読みやすさの最終判断は人間の確認待ちです。検証の実施／未実施と結果は `docs/ACCEPTANCE_SHOWCASE.md`、素材は `docs/ASSET_REGISTER.md`、採用仕様は `docs/APPROVALS.md` を参照してください。

木材は[Poly Haven Wood Table 001](https://polyhaven.com/a/wood_table_001)と[American Walnut Veneer](https://polyhaven.com/a/american_walnut_veneer)、庭の白石は[Sandstone Cracks](https://polyhaven.com/a/sandstone_cracks)の[CC0素材](https://polyhaven.com/license)を加工しています。原本12マップ・加工後40画像の出典とハッシュは [素材provenance](assets/source/polyhaven/provenance.json) に保存しています。配布物には `Notices/PolyHaven/NOTICE.txt` と `provenance.json` を同梱し、フォントファイル・参考画像は同梱しません。

本線への変更、ローカルcommit、push、外部公開は実施範囲に含めていません。ユーザー指定の公開リモートは https://github.com/piperendervt-glitch/n-dimensional-shogi-showcase です。

## ライセンス

このリポジトリのソースコード・文書・自作素材（手続き生成の盤・駒・庭、`tools/synth_audio.py` で合成した効果音・BGMを含む）は [MIT License](LICENSE) で提供します。

次のものはMIT Licenseの対象外で、それぞれ元の条件に従います。

- Poly Havenのテクスチャ（原本と加工後の画像）：[CC0-1.0](https://polyhaven.com/license)。出典は [NOTICE.txt](assets/source/polyhaven/NOTICE.txt) と [provenance.json](assets/source/polyhaven/provenance.json) を参照してください。
- 駒名テクスチャ（`Unity/Assets/Showcase/Resources/PieceTextures`）：Windows付属の游明朝で描いた文字画像です。本ゲームの一部としての利用に限り、文字画像を単独の素材として再利用することは許諾しません。
- Unity エンジンと Unity 公式パッケージ：Unity の各ライセンスに従います。
