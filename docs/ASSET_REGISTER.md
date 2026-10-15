# 素材台帳

取得・生成日: 2026-10-15。取得許可はAPPROVALS.md参照。

| ID | 素材・出所 | 権利・利用 | 改変・用途 |
|---|---|---|---|
| ART-01 | ユーザー指定の参照画像4点 (Dドライブ) | ユーザー提供資料。公開配布権の確認はしていない | 目視による構図・材質の参考のみ。アプリへ画像を貼らない |
| GEO-01 | 本プロジェクトの手続き的な盤・駒 | 新規制作 | 固定4D半空間、A/C形状。通常時も同じ形状の断面 |
| TEX-01 | tools/assets/prepare_pbr_assets.py（旧generate_piece_textures.pyから呼出） | CC0木材・石材スキャンの改変と、新規制作の完成した駒名画像 | pbr-courtyard/3.0。2048px Color/Normal/Mask、Kaiser mip・異方性16。駒名は墨色、浅い彫りの法線と艶差を持つ。固定4D局所UVを使用し、輪郭・深度の変位は行わない |
| TYPE-01 | Windowsに導入済みのYu Mincho Demibold（yumindb.ttf） | [Microsoft Font redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq) のゲーム内の完成した語句画像に関する説明を確認 | 歩兵・香車・桂馬・銀将・金将・角行・飛車・玉将・王将という完成した駒名のテクスチャのみ。フォントファイルや汎用文字アトラスは配布しない |
| ENV-01 | Unity/Assets/Showcase/Environment | 新規手続き制作、pbr-courtyard/3.0 | 面取り石畳、花器、土、葉脈を持つ立体葉、花、噴水。陶器・土・葉の表面は独自生成、木部はTEX-02/03、庭の石材はTEX-04を使用。外部モデルは使用しない |
| TEX-02 | [Poly Haven: Wood Table 001](https://polyhaven.com/a/wood_table_001) | [CC0-1.0](https://polyhaven.com/license)。Photography: Dimitrios Savva、Processing: Rico Cilliers | 2k diffuse / normal GL / roughness / AOの原本を取得。卓・駒箱・木製家具用。色調をわずかに調整し、法線強度と粗さを磨いた木材用に調整、HDRP Maskへ変換。クリアコート0.18・平滑度0.34〜0.70（平均約0.55） |
| TEX-03 | [Poly Haven: American Walnut Veneer](https://polyhaven.com/a/american_walnut_veneer) | [CC0-1.0](https://polyhaven.com/license)。Jenelle van Heerden | 2k diffuse / normal GL / roughness / AO。色を蜂蜜色へ再着色し盤・駒へ使用。原本はウォールナットであり、実物の榧のスキャンと表示しない。面ごとの木目場は固定 |
| TEX-04 | [Poly Haven: Sandstone Cracks](https://polyhaven.com/a/sandstone_cracks) | [CC0-1.0](https://polyhaven.com/license)。Rob Tuytel | 2k diffuse / normal GL / roughness / AO。目地を含まない自然な砂岩表面を暖かい白石色へ再着色。噴水・プランター・柱・石畳へ固定UVで使用。法線強度0.22、AO 0.85以上、非金属・coatなし、平滑度0.18〜0.36。浅い亀裂・粒を保持し、メッシュ変位なし |
| ENGINE-01 | Unity 6000.3.11f1同梱HDRPテンプレートと公式パッケージ | Unityの導入済み公式依存。各packageのLICENSEに従う | 新規HDRPプロジェクト設定。既存本線をコピーしない |

有料素材、有料API、生成画像サービス、素材購入は使用していない。参照画像とフォント原本の再配布は成果物に含めない。公開リモートへのpushは未承認。

2026-10-15の質感改善でPoly Havenの公式[ライセンス](https://polyhaven.com/license)と[API規約](https://polyhaven.com/our-api)を確認。制作時のAPIアクセスは固有User-Agent `NDShogiShowcase-AssetPreparation/2.0`（木材）と `/3.0`（石材）を使用し、配布アプリはAPIへ接続しない。取得した12マップのMD5を公式配布メタデータと照合した。[素材provenance](../assets/source/polyhaven/provenance.json)に各取得URL・作者・SHA-256・加工後40画像のSHA-256を保存した。CC0素材は本プロジェクト独自の制作物と扱わず、出典とライセンスを維持する。

v1.1で使用した周期的な手続き木目は、v2.0の盤・駒・卓では使用しない。材質差を確認する際は、照明・AO・反射・トーンマッピングのデバッグ切替と併用する。彫り文字は0.04mm相当の微細法線で表現し、断面の厳密な深度と一致しないピクセル変位や視点依存パララックスは実装していない。

v3.0はv2.0の木材・駒の37画像をそのまま保ち、石材3画像を追加した。庭の石に使っていた大きな雲状の手続き模様を置換する。元の石材スキャンは砂岩であり、石灰岩・トラバーチンを撮影した素材とは表示しない。実行時材質名は `Warm sandstone` / `Pale sandstone`。既存のUV・メッシュ形状・4D断面・ルールはこの材質更新で変更しない。

## 同梱した公式依存の通知

Windows配布フォルダーの `Notices/` に、使用した9個のUnity公式パッケージからライセンス・第三者通知11ファイルを原文のままコピーした。通知の内容は改変していない。この添付は、本プロジェクトへ新しいライセンスを付与するものではない。

| パッケージ | 版 | 同梱原文 |
|---|---|---|
| `com.unity.burst` | 1.8.28 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.burst/LICENSE.md) |
| `com.unity.burst` | 1.8.28 | [Third Party Notices.md](../builds/Showcase-Windows/Notices/com.unity.burst/Third%20Party%20Notices.md) |
| `com.unity.collections` | 2.6.2 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.collections/LICENSE.md) |
| `com.unity.mathematics` | 1.3.3 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.mathematics/LICENSE.md) |
| `com.unity.render-pipelines.core` | 17.3.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.render-pipelines.core/LICENSE.md) |
| `com.unity.render-pipelines.core` | 17.3.0 | [THIRD PARTY NOTICES.md](../builds/Showcase-Windows/Notices/com.unity.render-pipelines.core/THIRD%20PARTY%20NOTICES.md) |
| `com.unity.render-pipelines.high-definition-config` | 17.3.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.render-pipelines.high-definition-config/LICENSE.md) |
| `com.unity.render-pipelines.high-definition` | 17.3.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.render-pipelines.high-definition/LICENSE.md) |
| `com.unity.shadergraph` | 17.3.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.shadergraph/LICENSE.md) |
| `com.unity.ugui` | 2.0.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.ugui/LICENSE.md) |
| `com.unity.visualeffectgraph` | 17.3.0 | [LICENSE.md](../builds/Showcase-Windows/Notices/com.unity.visualeffectgraph/LICENSE.md) |

[通知の出所・版・SHA-256](../builds/Showcase-Windows/Notices/notice-provenance.json) は、導入済みパッケージ内の元ファイルと同梱先を対応付ける。[配布ファイル一覧](../builds/Showcase-Windows/SHA256SUMS.json) と [配布物マニフェスト](../evidence/artifact-manifest.json) に、実行物一式・ZIPのハッシュ、build GUID、対応するソースマニフェストのSHA-256を記録した。

Unityが生成したWindows実行物フォルダー、および導入済みWindowsStandaloneSupport・MonoBleedingEdgeのファイル名を確認した範囲では、別のlicense／noticeファイルは見つからなかった。存在しない通知や独自の利用許諾は追加していない。
