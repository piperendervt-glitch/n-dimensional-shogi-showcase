# 環境照明 v2

2026-10-15 の追加指示に対応する照明・反射・診断の実装記録。画質判断は実行ファイルから撮影した画像で行い、この設定一覧だけを効果の実証とはしない。

- 物理ベースの地球大気空、動的 sky ambient/IBL。青みのある天空と暖色の地表反射色を設定。
- 85,000 lux の背面左からの太陽光、EV 12.9 固定露出、ACES、控えめな Bloom と背景 DOF。盤と文字の可読性を維持し、日向の石・木の反射に明暗差をつける。
- 高品質 directional PCSS、太陽角径 1.15°、blocker/filter 各64 sample、4,096 px shadow cascade。影を落とす実形状の樹木を追加。独自生成した 1,024 px の葉 cookie を太陽光に適用し、投影位置をゆっくり揺らす。画面に重ねる影画像ではない。
- 非時間蓄積・全解像度 SSAO と 24 sample の contact shadow。過去の駒の形が残る危険を減らすため、SSAO の temporal accumulation を無効化。
- 天板の高さ 0.821 m に毎フレーム更新の 512 px planar reflection。影響領域の高さは 18 mm とし、盤上面・駒・箱蓋に天板と同じ反射平面が割り当たらないように限定。既存の 128 px / 6 面一括毎フレーム local cubemap も継続。SSR/SSGI は無効。反射画像の生成には現在の断面メッシュを使用し、ガイド線は含めない。
- 暖色の diffuse-only rectangle light を補助光として使用。これは日向の石畳からの回り込みを美術的に近似する光であり、ライトマップやパストレーシングで計算した GI ではない。IBL と区別して説明する。
- 天板の継ぎ目の面取りを 9 mm から 3 mm に縮小。花瓶は専用陶器材質へ変更。天面位置と 4D 断面の幾何は変更しない。

## 個別切り替え

| キー | 効果 |
| --- | --- |
| F2 | ACES / tone mapping なし（固定露出は同一） |
| F3 | 間接拡散光と暖色補助光 |
| F4 | 木漏れ日 cookie |
| Ctrl+F4 | 太陽の実形状 shadow map |
| F5 | AO と contact shadow を一緒に ON/OFF |
| Ctrl+F5 | contact shadow のみ ON/OFF |
| F6 | planar、cubemap、sky の反射光 |
| F7 | Bloom |
| F8 | DOF |
| Shift+F5 | 実 HDRP AO バッファ / 通常表示 |
| Shift+F4 | 実 HDRP contact-shadow バッファ / 通常表示 |

`EnvironmentDebug.SetEffect` は AO と contact shadow を個別にも設定可能。`SetDiagnostic` と各効果の設定はキーボード入力と検証ハーネスで同じ経路を使う。GeometryReference の初期状態では Bloom と DOF は OFF。

診断 shader は HDRP の実 `_AmbientOcclusionTexture` / `_ContactShadowTexture` を AfterOpaqueAndSky で専用 RT に読み出し、AfterPostProcess で表示する。AO は `1 - R`、contact shadow は HDRP 17.3 の 24-bit mask と 8-bit fade を復号する。白が非遮蔽、黒が遮蔽。contact 診断は接地影のみであり、太陽の shadow map 全体の診断と呼ばない。Release ビルドで除去される組み込み debug shader には依存しない。

次元回転・一時停止中は `FreezeAmbientMotion(true)` により cookie の揺れを停止できる。通常表示、診断表示ともに深度や駒のメッシュは変更しない。

API はインストール済み公式 HDRP 17.3 の `PhysicallyBasedSky.cs`、`HDAdditionalLightData.cs`、`ProbeSettings.cs`、`MaterialEvaluation.hlsl`、`LightLoopDef.hlsl` に照合した。新規外部画像はこの照明実装では取得していない。葉 cookie と追加樹木形状はソースから生成する独自素材。

初期の実行比較では、1,024 px cookie が既定 atlas に収まらず太陽光が消える問題を検出し、atlas を4,096へ拡張した。次の比較で SunShadows OFF のときだけ木面全体の網状模様が消えたため、当初は自己遮蔽（shadow acne）を疑った。しかし第4比較の cast 隔離試験では、環境オブジェクトの cast のみ OFF にすると模様が消え、盤・駒の cast のみ OFF や大きい bias にしても残った。主因は盤を覆う密な樹冠の PCSS 半影の空間サンプリングノイズであり、盤の自己遮蔽という初期推定を訂正する。

実 GPU `_HDShadowDatas` 読み戻しにより、4,096 px、blocker/filter 各64 samples、設定した normal bias が使われていることも確認した。修正は樹木の実配置で行い、左の高木を (-3.8, 0, 6.1) m へ移して密な樹冠の影を盤から外した。葉の cast 自体は維持し、盤面には柔らかい少数の cookie の影を残した。normal bias は0.75 texel、slope bias は0.5へ戻した。directional blocker/min-filter cone は各2°、32-bit depth / 4,096 px cascade を維持する。噴水は形状を変えず z=4.8 m へ移し、画面内で盤を圧迫しない位置にした。

## 最終候補の実行画像確認

build `e8e4c9e3ab2e4135b94c136d211da430` の [preview5 通常画像](../evidence/runtime/quality-v2-lighting-preview5/20261015-123653-771-ShowcaseRaster-1/lighting-all-on.png) を実画像として確認した。盤・駒の上面を覆っていた網状模様が消え、木目と文字が見える明るい日向になった。箱蓋と卓にはぼけ足のある木陰が残り、盤・駒自身の影も維持されている。これは今回の固定カメラ画像についての観察であり、ユーザーによる画質承認や参考画像との同等性判定ではない。

[同 run の GPU 読み戻し](../evidence/runtime/quality-v2-lighting-preview5/20261015-123653-771-ShowcaseRaster-1/lighting-shadow-restored-gpu.json) は cascade 0 の world texel size `0.000708562613 m`、normal bias `0.75`、積算 bias `0.000531421974 m`（約0.53 mm）、shadow map `4096 × 4096`、blocker/filter `64 / 64` を示した。slope bias `0.5` は CPU 設定の記録であり、この GPU 読み戻しで直接検証した値とは区別する。

[画像差分の技術検証](../evidence/runtime/quality-v2-lighting-preview5/20261015-123653-771-ShowcaseRaster-1/lighting-audit-review.json) では9個の個別効果を OFF にした各画像に差が生じ、実 AO/contact バッファは非一様、復元画像は通常画像と同じ SHA-256 だった。これらは効果が実際に動作し状態が復元された証拠であり、フォトリアルさの自動合格判定や OS キー入力配送の試験ではない。正式な `quality-v2-lighting` と全体 suite の結果は別記録を参照する。
