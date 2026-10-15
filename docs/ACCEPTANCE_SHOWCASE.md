# Showcase 受入台帳

対象正本: [SHOWCASE_MODE_PLAN_v0_1_JA.md](references/SHOWCASE_MODE_PLAN_v0_1_JA.md) §12、2026-10-15。最初の4列は正本48項目の原文を保持する。以下はcore/幾何と中間Standaloneの点検結果。最終buildの証拠へは未照合の項目を明示し、別buildの成功を最終runへ転用しない。

`PASS` は記載した技術条件を本runの証拠で確認したもの。`HUMAN_PENDING` は人間／映像確認を含む条件が未完了で、技術部分の合格を条件全体の合格へ拡張しない。`NOT_RUN` は該当証拠の点検または実行が未完了。未完理由は最終列に記載する。

幾何証拠 G: [試験結果](../evidence/geometry/geometry-test-report.json)、[ソースSHA-256台帳](../evidence/geometry/source-provenance.json)、[幾何設計](GEOMETRY_DESIGN.md)。記録のassertions数はループ内検査数であり、別々のテスト件数ではない。コンソール所要時間を描画性能に使用しない。

Core証拠 C: [v2の36件の試験](../evidence/core-v2/tests.json)、[DirectedLive](../evidence/core-v2/directed-live.json)、[VerifiedReplay](../evidence/core-v2/verified-replay.json)、[27手の集計](../evidence/core-v2/summary.json)、[設計と情報境界](CORE_DESIGN.md)。旧中間Standalone S: `evidence/runtime/smoke/20261015-102751-006-ShowcaseRaster-1` のtranscript/events。旧中間操作試験 P: [playback-automation.json](../evidence/playback/playback-automation.json)。S/Pは19手版の履歴であり、v2の27手・3D三往復・毎手のH0帰還や最終buildの代わりにしない。

2026-10-15の追加指示は、3DでA/B三往復の後に4Dへ進み、Aの4D着手ごとにH0へ戻してBが1手応答する構成。Bは銀・金・飛車・歩を含む通常の観測内展開を行う。Cでは27手の合法性、観測境界、三往復、混合駒種の応手、H90/H0用metadataを確認した。実描画の順序・画質・伝わり方は新Standaloneの証拠と人間確認を待つ。追加指示による30fps目標は旧正本の60fps行を改変せず、性能証拠列で新版の条件として扱う。

| ID | 受入条件・検証方法（正本原文） | 種別 | 主段階 | 現状 | 証拠・未実施理由 |
|---|---|---|---|---|---|
| SM-AC-01 | 本線と異なる作業実体で、本線への書込みがないことを範囲・差分記録で確認 | 自動＋点検 | M0/M5 | PASS | [workspace-audit.json](../evidence/environment/workspace-audit.json)で別作業実体・親を含む非reparse point、本線HEAD ae71161e5bc3d8726eaa1ea3fde9f2598c2fc7daとclean状態が開始時baselineに一致することを確認。Showcaseはmain/unborn、commitなし。 |
| SM-AC-02 | core/geometryのUnity依存がなく、consoleから試験できる | 自動 | M1/M2 | PASS | CとG。netstandard2.1のcore/geometryはUnityEngine参照なし。net9.0のconsole試験を実行。改訂core v2の36件PASS、幾何はG参照。 |
| SM-AC-03 | 駒リスト＋座標hashを使用し、9^4の局面配列／全マスGameObjectを作らない | 自動＋点検 | M2 | PASS | Cのsource-state-immutable-and-sparseとRules.csを点検。駒リスト＋Dictionary<Q4,Piece>で占有管理。Unityは40駒のSectionViewと盤を生成し、9^4マスの配列／GameObject群を生成しない。 |
| SM-AC-04 | 同一runの次元・陣営能力・遮蔽方式・版がSceneを跨いでも不変 | 自動 | M2 | PASS | Cのall-run-snapshots-preserve-rules-and-capabilities、固定次元拒否試験、全27手ログ。Scenarioはcentral-pawn-directed/2、Ruleset/能力/Observation版は従前の固定仕様でrun中不変、B全駒w=0。別遮蔽は別runとして記録。 |
| SM-AC-05 | 全着手を正本ルールと独立検証で確認。A/B交互、同じ主役ID、途中局面置換なし | 自動 | M2 | PASS | CのDirectedLive全27手とIndependentRuleVerifier。40駒正式初期配置から手番・駒種・移動・捕獲・全駒座標・終局を別経路で再構成。同じA-Pawn-0が14回着手、Bが13回応答し、王捕獲後39駒。3D三往復とBの金銀飛歩の応手を追加試験。H90/H0の実描画順序は新Standalone確認待ちで、旧Sの棋譜を代用しない。 |
| SM-AC-06 | シナリオ変更・不正手で確実に失敗し、Directorが勝敗を上書きしない | 自動・破壊試験 | M2 | PASS | Cのillegal-move-mutation/state-warp/replay-tamper試験が改ざんを拒否。early-four-dimensional-escape-is-rejected-by-scenario-guardでは3D三往復を省く合法な早期w着手も拒否。Scene guardは実攻撃と合法な逃走を検証し、勝者はRuleCoreの王捕獲イベントからのみ生成。 |
| SM-AC-07 | 格子直線と閉セルSupercoverの既知差分を確認し、設定を保存 | 自動 | M2 | PASS | Cのexact-supercover-known-corner-difference。移動(0,0,0,0)→(2,2,0,0)、障害(1,0,0,0)でlattice許可/Supercover拒否。設定と別27手runは[v2 supercoverログ](../evidence/core-v2/supercover/summary.json)にも保存し、Live/Replay・独立検算PASS。 |
| SM-AC-08 | 面方式など未知の方式を拒否。起動引数変更は別runになる | 自動 | M2 | PASS | Cのfixed-dimension-and-unknown-occlusion-rejected/alternate-occlusion-is-explicit-separate-run。plane/1等を拒否し、遮蔽変更で初期hashも変わる。別方式への暗黙fallbackなし。 |
| SM-AC-09 | q_w≠0の主役がGameStateに残り、全情報の攻撃・捕獲計算へ参加する | 自動 | M2 | PASS | Cのhidden-pieces-retain-occupancy-and-attacks/capture-king-terminal。主役はply11〜26でw=1のGameState内に残り、ply27の-w着手で王を捕獲。各中間局面でB観測からの論理不在をguardし、不可視駒を占有や全情報の攻撃計算から除外しない。 |
| SM-AC-10 | Bの観測が論理w=0で決まり、カメラ／断面／ソリッド厚みで変化しない | 自動 | M2 | PASS | CのObservationFactory.ForBは論理Position.W==0だけを使用。B入力と内部探索はPosition3型。Unity/カメラ/断面/形状厚みを入力に持たず、display-independent試験でも選択不変。 |
| SM-AC-11 | 同一観測・履歴・seedの反事実局面でBの候補評価と選択が一致 | 自動 | M2 | PASS | CのB-counterfactual-hidden-location-kind-count-invariance。同じ公開ply/観測/seedで隠れた駒の位置・種類・枚数だけを変更し、全候補評価・ノード数・選択fingerprintが一致。ordinary-B-development-does-not-use-escape-plyでは同じ観測位置の公開手番号だけを変更しても駒組みの候補スコア・ノード数・選択が一致。 |
| SM-AC-12 | Bへ全state・全攻撃表・隠れた移動端点・全hashを渡す変異を検出 | 自動・破壊試験 | M2 | PASS | CのB-boundary-full-state-and-hash-injection-rejectedで全state・全攻撃表・全hash・隠れた端点を含む入力を拒否。B-leaky-policy-mutation-detectedは隠れた情報に依存させた候補スコア変異を検出。 |
| SM-AC-13 | Bがw≠0へ動かないが、vを含む追撃を行う | 自動＋映像 | M2 | HUMAN_PENDING | CでBのw禁止、ply6/8/10のv=1/2/3追撃、各回の実攻撃guardがPASS。w脱出後は観測内の金銀飛歩で応答し、隠れた主役への情報漏洩なしを検査。3D三往復・各4D着手後のH0帰還が見て分かるかは新Standaloneの映像・人間確認待ち。旧19手Sの画像を新版の根拠にしない。 |
| SM-AC-14 | Aの歩着手／非歩救済を記録。正式シナリオで期待した偏重を確認 | 自動＋人間 | M2 | HUMAN_PENDING | Cの全A手14回が同じ歩。非歩救済0をsummary/候補ログへ記録。全着手の候補評価は5000ノード以内、全runで65246ノード。強い演出選好を明記し、自律的に発見した勝利とは説明しない。人間の偏重・役割理解の確認は未実施。 |
| SM-AC-15 | G固定の断面・カメラ変更で局面hash、合法手、各CPU入力と選択が不変 | 自動 | M3 | PASS | Cのdisplay-independent-observation-and-selectionで固定Gのhash、合法手集合、A/B入力・候補fingerprint不変。core APIに表示依存なし。四次元表示metadataは11〜27奇数手に対してtrueで、Bへの入力には入らない。改訂Standaloneの回転イベント・実描画hashの照合は別途待ち。 |
| SM-AC-16 | B^TB=I、B^Tn=0、回転と逆回転、基底・単位変換の既知値が一致 | 自動 | M1 | PASS | G。181角度の直交基底、B^Tn、回転の逆再構成、既知箱の単位変換、Unity循環置換とfloat転送を確認。ソース／試験assemblyのSHA-256と実行時snapshotを保存。 |
| SM-AC-17 | 押し出し形のH0が元K3と一致。箱と90°断面の既知解も一致 | 自動 | M1 | PASS | G。箱H0体積8・H90体積1.6、押し出しAのH0と同じK3から作るCの中心断面の頂点一致を確認。Cの0°／90°同一頂点と45°の実際の幅増加も検査。 |
| SM-AC-18 | 中央列の主役と盤中心が回転中に残り、H90でw移動を固定断面内に表示 | 自動＋映像 | M1/M3 | HUMAN_PENDING | Gで中央列A/Cの全回転角と固定H90の41点w移動がVolume3D。最終build e8e4c9e3ab2e4135b94c136d211da430の[27手events](../evidence/runtime/quality-v2-geometry-passes/20261015-123835-231-GeometryReference-1/events.jsonl)にH90停止・着手・各手のH0帰還を記録し、[H90取得](../evidence/runtime/quality-v2-geometry-passes/20261015-123835-231-GeometryReference-1/scene04-h90-passes.json)に主役・盤の実Meshを保存。一連の動きの伝わり方は最終連続映像・人間確認待ち。 |
| SM-AC-19 | 同一形状の帰還後不交差を確認し、fade/hideによる偽装を検出 | 自動・点検 | M1/M3 | PASS | Gで同じ固定A/Cのw=1帰還H0がEmpty。SectionViewは断面結果からMesh.Clearし、fade/hideで代用しない。最終buildの[実機不在取得](../evidence/runtime/quality-v2-geometry-passes/20261015-123835-231-GeometryReference-1/scene04-absence-passes.json)ではA-Pawn-0がGameStateに残り、実Meshは頂点0・index0。[両profile独立レビュー](../evidence/geometry/quality-v2-gpu-review-summary.json)で主役の以前のシルエット13選定画素が過去面より後方の深度を示す。同じ実GPU故障検査が各13/13画素を拒否し、故障EXRも独立再計算。背景法線の一致や全影・反射の不在保証には拡張しない。 |
| SM-AC-20 | 接触・共面・w端面一致・完全Empty・容量超過を区別 | 自動 | M1 | PASS | G。点／線／面の低次元接触、共面、w端面一致時に残る完全3D体積、端面外Empty、CapacityExceeded、無効基底・非有限入力のNumericallyUncertainを区別。後二者をEmptyへ丸めない。 |
| SM-AC-21 | Volume3Dの断面が閉じ、法線向き・重複面・退化三角形・有限値が妥当 | 自動 | M1 | PASS | Gで閉辺の2回共有、Euler数2、正の体積、外向き法線、重複面・退化三角形なし、有限値と由来面を確認。最終buildの[GeometryReference](../evidence/geometry/quality-v2-geometry-buffer-review/buffer-review.json)／[ShowcaseRaster](../evidence/geometry/quality-v2-showcase-buffer-review/buffer-review.json)は各99通常EXR＋1故障EXRがすべて有限。復号法線の単位長最大誤差2.39e-7。各profileの選定境界3画素はAC26のPARTIALとして別記。 |
| SM-AC-22 | 主断面方式と独立した制約交差検算が許容誤差内で一致 | 自動 | M1 | PASS | G。主クリッピングと独立3平面交差・独立rank判定の状態／頂点集合が2e-6セル以内で一致。全181角度、w移動、帰還、接触と姿勢変更を検査。追加270ジョブの直列／並列結果は位置・法線・局所4D・由来・index順序まで完全一致。 |
| SM-AC-23 | 可視集合の駒ID・枚数を出力し、「大部分」をhideや任意閾値で達成扱いしない | 自動＋人間 | M3 | HUMAN_PENDING | 最終buildの[27手frames](../evidence/runtime/quality-v2-showcase-passes/20261015-124021-341-ShowcaseRaster-1/frames.jsonl)全2662行で可視ID数とvisiblePiecesが一致。初期H0は40枚、最初のH90は4枚（A/Bの王・中央歩）、ply12〜26のH90はB-Gold-1を加え5枚。ply11帰還H0は主役を除く39枚、終局H0はB王を除く39枚で主役が復帰。Mesh断面状態から算出し、hideや任意閾値を使わない。「大部分」として十分伝わるかは人間未確認。 |
| SM-AC-24 | A/Cの0°・中間角・90°比較。固定形状版であり角度別モデル交換なし | 自動＋人間 | M1 | HUMAN_PENDING | Gで固定A/Cの0°／45°／90°と連続角度を数学比較し、角度別モデル交換なし。最終buildの[A/C実画像とバッファ](../evidence/runtime/quality-v2-shape-comparison/20261015-124703-597-GeometryReference-1)は0°・30°・45°・60°・90°を取得し、専用runはEXITED_ZERO。形状と文字の見た目の人間選択は未実施。 |
| SM-AC-25 | 局所4D材質座標・面IDが追跡でき、木目の泳ぎや漢字貼り直しがない | 自動＋人間 | M3 | HUMAN_PENDING | Gで局所4D位置、FaceId、SurfaceKindと法線の由来を自動確認。SectionViewの固定場はA=M3(f,u,v)、C=M3(f,u+w,v)、文字は元Top由来面のみ。GPUへ局所4D／由来をUV channelで保持。最終buildの[A/C角度比較](../evidence/runtime/quality-v2-shape-comparison/20261015-124703-597-GeometryReference-1)を保存したが、木目の泳ぎ・漢字貼り直しがないことの連続動画・人間点検は未完。 |
| SM-AC-26 | Color/Depth/Normal/Shadow/Reflectionの同一断面版を確認 | 自動＋映像 | M3 | PARTIAL | 最終build e8e4c9e3ab2e4135b94c136d211da430の[GeometryReference](../evidence/geometry/quality-v2-geometry-buffer-review/buffer-review.json)／[ShowcaseRaster](../evidence/geometry/quality-v2-showcase-buffer-review/buffer-review.json)で各9回、実Color/Depth/Normal、native8192影atlas（cascade4096）、実128px cubemap6面、実512px planarを取得。各4583 probes中、深度4554・法線4551一致、absence-only29、境界未確定3、失敗0。実GPU古い深度13/13検出、保存EXRとsample深度差0。CPU宣言をGPU版tokenとせず、両profileともShadow/Reflectionの全駒・全画素同一性は未保証。 |
| SM-AC-27 | Empty駒の実体の影・反射が消え、静的ベイクに盤・駒の古い影が残らない | 映像＋点検 | M3 | HUMAN_PENDING | 最終buildの[Scene04取得](../evidence/runtime/quality-v2-showcase-passes/20261015-124021-341-ShowcaseRaster-1/scene04-absence-passes.json)はEmpty主役の三角形0、以前の主役シルエット13選定画素に古い深度なし。[両profile独立レビュー](../evidence/geometry/quality-v2-gpu-review-summary.json)で実影atlas・cubemap・planarの有限性と非一様な反射画像を確認したが、全影／反射の駒別不在と静的ベイク残像の完全照合ではない。最終映像・人間確認待ち。 |
| SM-AC-28 | カメラ外でも影・反射に必要な非Empty形状を保持 | 自動＋映像 | M3 | HUMAN_PENDING | SectionViewの形状保持は4D断面状態のみで、カメラfrustumをMesh消去条件にしないことをソース点検。最終buildの[両profile取得](../evidence/geometry/quality-v2-gpu-review-summary.json)は実盤・駒を含む動的cubemap全6面とplanarを保存し、EveryFrame設定をmetadataへ記録。カメラ外の指定駒を追跡した専用の影／反射映像試験は未実施。 |
| SM-AC-29 | 非時間型AA基準との比較で、不正な履歴・消失後残像を確認 | 映像 | M3/M4 | HUMAN_PENDING | 最終buildの両profileは非時間型SMAA、TAA／SSR／SSGIなし。cubemapはEveryFrame・timeSlicingなし、planarも毎frame更新。[実深度検査](../evidence/geometry/quality-v2-gpu-review-summary.json)は各profileで以前のシルエット783選定画素の古い深度を除外。これは全時間履歴・最終カラー残像の証明ではなく、最終両profile連続動画の比較と人間確認は未完。 |
| SM-AC-30 | 全断面経路で盤の卓へのめり込み・急な縮小・不意のクリッピングを確認 | 自動＋人間 | M3 | HUMAN_PENDING | Gで固定盤の全0〜180°断面boundsと垂直範囲[-0.8,0]セル、最大横幅9√2セル≒0.6364mを確認。角度に応じた形状縮小を実装しない。最終buildの[両profile実Mesh取得](../evidence/geometry/quality-v2-gpu-review-summary.json)は選定snapshotの証拠であり、実卓との全経路接地、画角内の不意のクリッピングは連続映像・人間点検待ち。 |
| SM-AC-31 | Scene 01が木製盤・駒・卓、広場、自然光、人物なし、メニューなしを満たす | 人間 | M4 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-32 | Scene 02で本当にv座標を変え、Bが高さへ応答することが見て分かる | 自動＋人間 | M3 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-33 | Scene 03の危険が全情報の攻撃計算から確認でき、単なる字幕に依存しない | 自動＋人間 | M3 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-34 | Scene 04の断面変形・停止・w移動・帰還・不在が一連で伝わる | 人間 | M3/M4 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-35 | Scene 05の経路が実着手に一致し、同じ歩による捕獲と正式終局を確認 | 自動＋人間 | M3/M4 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-36 | 補助演出OFFでも断面が成立。ghostを占有・実体影へ含めない | 自動＋人間 | M3 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-37 | Scene 01〜05が入力なしで完走。guard違反時は不一致を明示 | 自動＋実機 | M5 | NOT_RUN | 中間Standalone Sはメニュー操作なしで全19手・Scene01〜05を完走しIndependentVerificationPassed/GameEndedを記録。guard失敗のcore破壊試験はCでPASS。最終buildでの実行確認はまだ記載しない。 |
| SM-AC-38 | DirectedLiveとVerifiedReplayをログ上区別し、同じ棋譜hashを照合 | 自動 | M2/M5 | PASS | Cのdirected-live.json/verified-replay.jsonはmodeを区別し、27手の同一棋譜hash 8aeb2a56571719a6b1ea74424da8279a44864edd25c3d766a6680800d991f466を確認。Replayは全手再検証・検索ノード0。同梱fixtureもv2へ更新。改訂Standaloneでの両モード照合は撮影・実行証拠待ちで、旧19手のSを代用しない。 |
| SM-AC-39 | 一時停止・再開・先頭再生・終了・複数ループで二重commitや駒残りなし | 自動＋実機 | M5 | NOT_RUN | Cの予約取消・再開・二重commit拒否はPASS。中間Pは共通操作メソッドで停止中hash/角度/カメラ固定、再開、予約中R、2完走と自動リセット、終了を確認。OSキー注入試験ではない。旧terminal glow確認は光が既に無かったため証拠に採用せず、最終buildで再試験待ち。 |
| SM-AC-40 | デバッグ表示切替で勝手なカメラ・時間・スクロール移動なし | 自動＋人間 | M5 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-41 | 1080p/1440p、AA、内部解像度、CPU/GPU、p95/p99、最悪値、GC/VRAMを記録 | 計測 | M4 | NOT_RUN | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-42 | 最悪断面角・消失直後・VFX最大・cold/warm・連続ループを計測 | 計測 | M4 | NOT_RUN | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-43 | 60fps目標の達成／未達を実描画で判定。フレーム生成・静止収束画像を混同しない | 計測＋人間 | M4 | HUMAN_PENDING | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-44 | 素材の出所・ライセンス・取得権限・改変・配布可否の台帳がある | 点検 | M0/M5 | PASS | [ASSET_REGISTER.md](ASSET_REGISTER.md)に自作形状・木目・環境、完成した駒名画像、導入済み公式依存、参照画像の配布対象外を記録。[APPROVALS.md](APPROVALS.md)に取得権限、[SOURCE_MANIFEST.md](SOURCE_MANIFEST.md)に原本12点のSHA-256。購入素材なし。 |
| SM-AC-45 | Windows実行物をEditorなしで起動し、README通りに再生・終了できる | 実機 | M5 | NOT_RUN | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-46 | 受入表全IDに結果・証跡・未実施理由が対応し、本線や別runの証拠で穴埋めしない | 点検 | M5 | NOT_RUN | 全48項目の要求本文を正本から複写。完成提出時に最終runの証拠へ再照合が必要。 |
| SM-AC-47 | GeometryReferenceとShowcaseRaster双方の連続動画、全景・近接・帰還・終局画像を提出 | 点検 | M5 | NOT_RUN | この初期点検では該当するシナリオ・実機・人間／素材証拠を未照合。統合担当が実施結果とrunを追記する。 |
| SM-AC-48 | 人間未確認の画質・理解しやすさをPASSにせず、残件と次の判断を00へ返す | 点検 | M5 | NOT_RUN | 画質・理解の人間承認なし。最終返却文の作成・確認が必要。 |

