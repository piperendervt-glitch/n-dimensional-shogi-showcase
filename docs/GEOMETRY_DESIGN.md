# 固定4D形状と断面生成

この文書は `geometry/0.1` の実装とconsole検証を記録する。対象は本Showcase専用の独立ライブラリであり、本線のコードや試験実績を流用していない。Unityの実画像・影・反射・性能・人間の画質承認は、この幾何試験だけでは確定しない。

## 座標と形状

座標順は `(f,u,v,w)`、垂直方向は `v`、全軸の1セルは0.05 m。局面の整数座標、表示移動の4D補間、観察断面、3Dカメラを別データとする。ShapeFactoryの形状は作成時に固定され、観察角に応じて縮尺や形を変更しない。

| 対象 | 固定値・定義 |
|---|---|
| 盤 | `f,u,w ∈ [-4.5,4.5]`、`v ∈ [-0.8,0]`、4D稜の面取り0.04セル |
| 歩の平面輪郭 | 後端 `f=-0.36`、先端支持 `f=0.36`、左右幅 `rho(f)=min(0.2623076923076923-f/13, 0.45-1.25f)` |
| 歩の高さ | `0 ≤ v ≤ 0.17-f/18`。後端は厚く、先端は薄い |
| 歩の面取り | 元3D制約の法線対から作る固定切り落とし平面。距離0.012セル |
| A | 面取り済み3D形状K3を `w∈[-0.2,0.2]` へ押し出す |
| C | 面取り済みK3のu非ゼロ制約をu→wに複製。uとwの幅関数を同一にする |
| 他駒の大きさ | `CreatePiece(variant,size)` の固定一様倍率。角度とは無関係 |
| 卓の計画寸法 | 0.9×0.9 m以上。正方形u/w盤の最大幅上界は `9√2` セル、約0.6364 m |

これらはプロジェクトの造形値であり、実在する将棋盤・駒の標準寸法を主張しない。面取り後の実際の輪郭は上記の支持範囲より僅かに小さくなる。

`Instance4D` は固定形状と4D平行移動を持つ。陣営の向きは `diag(forward,forward,1,1)`、forwardは±1。f/uを同時反転し、v/wは維持する。Shapeの制約リストは外部から変更できない。

## 観察断面

`Bθ=[ef,cosθ eu+sinθ ew,ev]`、`nθ=cosθ ew−sinθ eu` とし、`X=P+Bθξ` を満たす点だけを断面へ含める。`BᵀB=I`、`Bᵀn=0`、`||n||=1` を確認する。H0はw=0、H90はu=0。u=w=0の主役中心と盤中心は回転中も断面に残り、H90のままwへ移動できる。

固定された物体の半空間 `a·X≤b` を代入すると `(Bᵀa)·ξ≤b−a·P` になる。元の4D点を単純投影した図形ではなく、この不等式集合の3D境界を描く。基底は必ず有限かつ直交として検証する。

Unityへの座標変換は `Vector3(ξ.Y,ξ.Z,ξ.X)*0.05+anchor`。循環置換なので向きは維持される。面の頂点は外向きに並べ、法線は正規化した `Bᵀa`。ピッキング等の逆変換でも同じ規約が必要。

## 主方式と独立検算

主方式 `SectionBuilder` は倍精度の凸多面体クリッピング。元形状の4D AABBの16頂点を断面座標へ投影して保守的な初期箱を作り、余裕を付けてから各制約で切る。新しい蓋には、その制約のFaceIdとSurfaceKindを引き継ぐ。初期箱の仮面が最終境界へ残った場合は成功として返さない。縮退した点・辺・面も途中で保持し、低次元接触と空集合を区別する。

独立方式 `TriplePlaneOracle` は、物体局所座標で断面を別経路から代入し、3制約平面ずつの連立解を列挙する。すべての制約を満たす候補だけを残す。主方式の初期箱、クリップ、多角形整列、蓋生成を使わない。次元判定も独立したGram–Schmidtによる。基本ベクトル演算は共通ライブラリを使用する。

| 数値項目 | 値・扱い |
|---|---|
| 主方式の距離許容 | 1e-9セル。正規化した3D制約の距離比較に使用 |
| 法線ゼロの分類 | 投影法線長が1e-12未満なら正規化せず、右辺により排除／冗長／共面を判定 |
| Oracleの候補許容 | 1e-8セル、三平面行列式の閾値1e-11 |
| 主／独立頂点比較 | 2e-6セル以下、0.05m/セルで1e-7 m以下 |
| 默认容量 | 256制約、1024面、8192作業／出力頂点。超過はCapacityExceeded |

許容値は本寸法での倍精度誤差を扱うためのものであり、厚みを持つ観察スラブを作る値ではない。数値の閾値内にある接触は離散的分類の許容帯を持つ。巨大な座標や極端な縮尺への一般化は未保証。

戻り値は `Empty`、`ContactLowerDimensional`、`Volume3D`、`NumericallyUncertain`、`CapacityExceeded`。後二者をEmptyへ丸めない。Aのw端面と完全一致するとK3全体の3D体積が残る。端面直後のEmptyへの不連続は元形状の性質である。非凸の複数成分は各成分ごとに切り、離れた成分のmin/max距離だけで空白を埋めない。

## 材質と由来

各SectionVertexは断面位置、法線、元の物体局所4D位置、FaceId、SurfaceKindを持つ。Topは元の上面制約だけを指し、Side/Bottom/End/Bevelと区別する。三角形の全頂点は同じ由来面に属する。局所位置は断面から逆写像した値で、生成面へ毎回貼り直したUVではない。

| 材質プロファイル | 固定された参照式 | 意味 |
|---|---|---|
| A reference | `M4(f,u,v,w)=M3(f,u,v)` | w押し出し。90°で文字が帯状になる場合を保つ |
| C visual | `M4(f,u,v,w)=M3(f,u+w,v)` | 固定されたアフィンな場。中心の0°と90°で同じ横座標を使える |

Cの式は観察角やカメラに依存しない。中間角の伸び、w移動時に切られる材質、陣営姿勢による見え方は元の固定場に従う。木目・文字の見栄えは人間確認が必要。Top以外へ新しい文字を貼らない。shaderのnormal/roughness等を追加するときも、局所座標と接線の退化を検査する。

## 検証結果と証跡

[geometry-test-report.json](../evidence/geometry/geometry-test-report.json) が本実装のconsole実行結果。[source-provenance.json](../evidence/geometry/source-provenance.json) に実行時のライブラリ・試験・設定のSHA-256、試験アセンブリと幾何アセンブリのSHA-256を保存する。実際のソースは [source-snapshot](../evidence/geometry/source-snapshot) に複写した。Git commitによる版固定は行っていない。

検証は6群: 既知形状・接触、A/C比較、全経路、初期配置の幾何可視集合、Unityへのfloat変換、直列／並列の完全一致。0〜180°の181角度で盤/A/Cを独立方式と比較し、固定H90のw移動、90→0°の帰還全経路、翻転した陣営・平行移動、90°近傍を追加した。辺の2回共有、反対向き、Euler数2、三角形面積、重複なし、有限値、正の体積、元制約への所属、FaceIdと法線、局所材質位置の逆変換を検査した。並列試験はA/C/盤、両陣営、u=-4〜4、0/1/2/45/90°の270ジョブを、最大8 workerで直列結果と比較し、状態、体積、頂点と三角形の順序、位置、法線、局所4D、由来が完全一致した。

最新結果は `TECHNICAL_PASS`。レポートの3,372,857はループ内assertionの総数であり、独立したテスト件数ではない。記録された実行所要はconsole検証全体の所要で、フレーム時間や描画性能ではない。再実行ごとの値はJSONを正本とする。

クリッピングの最適化について、[旧新比較31,752ケース](../evidence/geometry/optimization-review/comparison.json) は `PASS_EXACT_OLD_NEW`。状態、diagnostic、体積、bounds、順序付き頂点の位置・法線・局所4D座標・由来、順序付きindexまで完全一致した。対象はA/Cの40駒と盤、0〜2°を0.02°刻み・3〜180°を1°刻み、w端面付近、容量とepsilonの変更、rank 0/1/2の接触を含む。比較対象のSectionBuilderの旧SHA-256は `64292231858ee7299274153c2889111ed10480ba97ad14c0baf03dc949c3b559`、新SHA-256は `e1898e837f886b7b02b0a14984168105866adbb10a7d9b23742c9d9e884381bd`。

同じ比較に含まれる、1476断面を単一threadで処理するwarm console試行5回の中央値は103.4385msから53.9417msとなった。現threadの管理メモリー割当は各試行452,319,400bytesから167,934,312bytesへ約62.9%減少した。これは数学処理の比較であり、Unityの描画fps、CPU/GPUフレーム時間、プロセスメモリー占有の証明ではない。`gen0Collections` は累積カウンターなので、その値だけからGC回数の増減は主張しない。30fps目標の実描画評価は撮影なしのStandalone計測を別途使用する。

初期40位置を同じ寸法の駒で切った幾何fixtureではH0で40枚、H90で中央列の4枚が残る。これは正式な対局経路の可視集合の証明ではない。「大部分」の人間受入閾値も定義していない。w=1へ動いたA/Cの歩は帰還H0でEmptyになる。

再実行:

```powershell
dotnet run --project tests/Showcase.Geometry.Tests/Showcase.Geometry.Tests.csproj --configuration Release -- evidence/geometry
```

UnityのローカルパッケージがDLLを二重importしないよう、.NETのbin/objはパッケージ外の `artifacts/bin/Showcase.Geometry` と `artifacts/obj/Showcase.Geometry` へ出力する。

## Unityアダプターの独立点検と残件

SectionView.csの初回読取りでは座標・単位、局所4D保存、元Topの材質分離、固定A/CのUV式、単一Meshの各パス共用、Emptyでの三角形消去を確認した。初回点検対象SHA-256は `48345b544e76cef401ea45339b331abb318760d4bbdad545e2ab69d6e3c29f09`。これはその時点の読取り証拠であり、後続変更の承認ではない。

初回指摘は、キャッシュ再利用時にもSectionVersionを更新すること、およびAの90°等で縮退するUVからの接線を有限・直交なfallbackへ処理すること。修正・実画像の確認は統合担当が別証拠で記録する。ForceNoMotionと非時間型SMAAは初期の整合した方針であり、正しい動的トポロジーのmotion vectorsを実装済みとはしない。

現在のクリッピングは管理配列・Listを確保する。0 GC、Jobs/Burst、GPU生成、60fps達成は未主張。変更しない断面の再利用をadapterで行い、実機で計測してから最適化する。実際のカラー・深度・法線・影・反射の同一形状、反射履歴、静的GI焼込み、卓との接地、連続動画、画質は [受入台帳](ACCEPTANCE_SHOWCASE.md) の別条件として残す。

## GPUバッファの検証範囲

`PassEvidenceCapture` はHDRPの `BeforePostProcess` において、実際のカメラHDR色、深度、法線を別のfloat render targetへ読み出す。カメラの入力ターゲットを書き換えない。深度EXRはmetres単位のLinearEyeDepth、法線EXRはHDRPの法線バッファを復号したworld normal。PNGは診断表示であり、最終カラーの承認には別の通常スクリーンショットを使用する。取得処理・PNG/EXR保存の所要を含むrunを、取得なしの性能runと混同しない。

現在GPUへ渡したMeshの三角形へCPU rayを交差させ、内部および辺の近くの選定画素を比較する。深度許容1 mm、法線の内積下限0.75。三角形の微小な外積には、Unityの通常のVector3.normalizedがゼロを返す大きさの面取り面があるため、外積の二乗長を用いて明示的に正規化する。投影した三角形の各辺までの画素距離を記録し、1/128画素以内で生じた不一致だけを `NOT_VALIDATED_RASTER_BOUNDARY` とする。これは成功数へ振り替えず、結果はPARTIALとなる。その他の不一致はFAIL。画素選定元の「interior」「near-edge」はサンプリング法を示し、最も手前で交差した別三角形の辺距離も独立に記録する。

CPU rayが手前の駒を僅かに外して盤へ到達する逆の場合もあるため、全section三角形の投影辺から1/128画素以内の候補を記録する。その候補の透視補正した深度が実深度の1 mm以内、候補法線と実法線の内積が0.75以上のときだけ、同じ未検証の境界分類へ入れる。候補の駒ID、辺距離、深度を保存する。実際のworld頂点、投影頂点、三角形、カメラ行列も `*-mesh-oracle.json` に保存し、別のNumPy処理から辺距離を再計算できる。境界を除く許容値を緩めていない。検査失敗は全snapshotを取得するまで蓄積し、run終端で失敗終了コードへ反映する。

前回Volumeだった駒がEmptyになった場合は、以前のシルエット内の選定画素を現行Meshと比較する。以前の面の深度が残るとFAIL、現在の物体より手前に別の環境物があれば遮蔽として区別する。これは全画素の完全な残像不在証明ではない。CPU側の故障検出検査は二種類を区別する。CPU宣言versionを古くする試験、および実際に取得した深度配列のコピーの1画素を古い面深度等へ書き換える試験である。後者もGPUの描画パスへ故障を注入したものではない。元のバッファと通常検証のサンプルは保存したままにする。

追加の `GpuStaleDepthFault` は、現在のGPU深度をprivate RTへコピーし、保存した過去のworld-space駒三角形を、そのコピーだけへGPUで再ラスタライズする。元のカメラ色・深度・法線と実際のMeshは変更しない。現在の法線を保ったまま一つの深度パスへ古い形状を加え、同じauditorがbaseline正常画素を `FAIL_POSSIBLE_STALE_DEPTH` として検出することを要求する。これはCPU配列書換えとは別の実GPU故障fixtureである。成功／失敗、変更された選定画素数、故障EXR、行列、古い三角形は `*-gpu-stale-depth-fault.json` 等に記録し、実行前に成功を主張しない。

追加のEXRは実際の `_ShadowmapCascadeAtlas`（最終buildのnative 8192×8192を1024×1024へpoint sample）と、実際の `HDProbe.realtimeTexture` の6面（128×128、mip0）、`PlanarReflectionProbe.realtimeTexture`（512×512）を取得する。8192はatlas全体の寸法であり、各cascadeの4096とは区別する。独立レビューは影EXRのG/Bに保存されたnative寸法を当該passes.jsonと照合し、旧4096固定判定を使用しない。影atlasの取得だけで駒ごとの影IDや不在を証明しない。cubemapは毎フレーム全6面を更新する局所近似であり、正確な視点依存反射ではない。planarは天板の鏡像カメラを毎フレーム描画する。GPU上のsection version tokenを捏造せず、設定とtexture更新情報はmetadataとして別記する。反射の物理HDR放射輝度はpre-exposure済みのカメラHDRとは桁が異なるため、独立レビュー画像は全snapshotへ同じ1/8192露光を適用している。

独立したEXRデコード・有限値・normal単位長・SHA-256と反射診断画像の生成は [review_gpu_buffers.py](../evidence/geometry/review_gpu_buffers.py) で再実行できる。先行visible-controlの [buffer-review.json](../evidence/geometry/candidate-visible-buffer-review/buffer-review.json) は90個すべてのEXRが有限で、9回すべてのreadbackが完了したことを記録する。ただし、その旧runのpixel auditは微小法線と辺境界の問題を検出してFAILを保持しており、修正版の成功証跡へ書き換えていない。最新のrunと受入判定は受入台帳を正本とする。

2026-10-15の最終build `e8e4c9e3ab2e4135b94c136d211da430` について、[GeometryReference run](../evidence/runtime/quality-v2-geometry-passes/20261015-123835-231-GeometryReference-1) と [ShowcaseRaster run](../evidence/runtime/quality-v2-showcase-passes/20261015-124021-341-ShowcaseRaster-1) はそれぞれ9回の取得を完了した。[GeometryReference独立レビュー](../evidence/geometry/quality-v2-geometry-buffer-review/buffer-review.json) と [ShowcaseRaster独立レビュー](../evidence/geometry/quality-v2-showcase-buffer-review/buffer-review.json) は各9 snapshot × 11 buffers = 99個と故障fixture1個、計100 EXRがすべて有限と記録する。各profileとも4,583 probes中、深度4,554・法線4,551が一致し、29は以前の面の不在だけを確認したsample、3は境界法線未確定、失敗0。snapshot単位では6 PASS、3 PARTIAL。選定深度と期待面の最大誤差は0.08244 mm、保存EXRを独立デコードした深度と全sample記録の差は0、法線内積の再計算差は最大9.82e-8、法線バッファの単位長最大誤差は2.39e-7。各9枚のactual planarも512×512で有限・非一様だった。これは両profileの選定画素検証であり、全影・反射の同一断面版、全画素不在、時間履歴、最終画質の承認ではない。

3件の境界未確定はScene02-height・Scene03-pressure・Scene04-absenceに共通するBoard画素(763,388)。保存済み頂点からのNumPy独立再計算により、手前のB-King-0の三角形36・辺2から0.001116956983画素の位置だったと確認した。その辺の透視深度は1.112653582 m、実法線との内積は0.999992109。説明可能な境界として記録するが成功数へ振り替えない。各profile全体で783個の以前のシルエット選定画素が古い深度を除外した。Scene04の主役不在の実GPU故障fixtureは、baseline正常13画素（以前の面の不在のみを確認）すべてをGPU上の過去三角形描画で変更し、13画素すべてを同じauditorが古い深度として検出した。元の深度EXRと故障EXRの独立デコードでも、13画素すべての1 mm超の変化と過去Mesh深度への1 mm以内の一致を確認した。背景法線の一致を確認したとは扱わない。結果と参照先のSHAは [集約JSON](../evidence/geometry/quality-v2-gpu-review-summary.json) に保存した。

影atlasと6面cubemapの実画素も保存され、固定露光の下向き反射面では、Scene03で見える中央の主役がScene04で消えていることを目視確認した。これは選定した比較の証拠であり、影・反射の全画素について駒IDと不在を完全に証明したという主張ではない。
