# CFG2 基準照合・デモ限定仕様・実装判断

最終実測：pure C# の5系列、Windows revision-05の3解像度各176項目、別process再読込10項目が成功した。revision-02で検出した移動先pick不具合は修正後に再検証済み。保護46件・旧証拠・旧EXE等の終了時監査も一致した。これは物理入力、人間検収、正式ゲームルールの承認を意味しない。

対象は `C:\dev\n-dimensional-shogi-showcase`。今回出力は本書のある `artifacts\CFG2-20261017-004021-JST`。過去 run に今回の生成物を書き込まない。

## 1. 採用した依頼と基準

ユーザーが実行依頼として採用した [CFG2 実装依頼書](C:/Users/USER/.codex/.chatgpt-projects/g-p-6ad04e32b33c8191b44c5dfad644d21c/deliverables/CFG2_UI_MATCH_REQUEST_v0_1_JA/CODEX_CFG2_UI_MATCH_IMPLEMENTATION_REQUEST_JA.md)を全文読み、§5を今回のデモに限って実装した。契約 SHA-256 は `D843C859C9DFED22B5494FF3EAB5897C93B8CA71D2E85516B4F76EE9F3A5A7E1`。これは製品の正式ルール、人間検収、強い AI、完全合法性の採用ではない。

CFG1 原本 `D:\Users\USER\Downloads\n-dimensional-shogi-showcase\FOUR_DIMENSIONAL_SHOGI_CODEX_HANDOFF_v0_1_JA\output\CFG1-20261016-201633-JST` の 00～05、最終ソース監査、開始監査、受入台帳を照合した。原本パス・hash・旧196 ID・C1 18件は [cfg1-reference-index.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/cfg1-reference-index.json) に記録する。過去の PASS は今回の実行結果として転記していない。

今回の依頼により、CFG1 の「CFG2へ進まない」「対局未接続」「7カテゴリ」「保存／別名／適用の常設導線」は該当部分だけ更新した。履歴原本・過去 EXE・証拠を上書きしていない。一般 H、正式成り・打ち・自玉安全・反復、N=2互換の新規完成、N>=5、Quest、通信、強い探索は依然として今回の対象外である。

## 2. 開始前の照合

根拠は [preflight-baseline-audit.md](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preflight-baseline-audit.md)、[preservation-before.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preservation-before.json)、[preflight-audit.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preflight-audit.json)。結果は `NO_MAJOR_BASELINE_INCONSISTENCY`。この基準確認と既存4回帰の成功後に実装を開始した。

| 項目 | 開始時実測・扱い |
|---|---|
| 対象 HEAD | `14d56f80d2e9758f550de5bb11e152960cb9fc6e`。開始ブランチ名も保全台帳へ記録。Git書込みなし |
| CFG1 所有ソース | 43/43 SHA一致。tracked変更2と新規41を区別 |
| 歴史的保護46件 | 46/46 SHA一致。画像42、player.log 2、process-memory.jsonl 2。編集・移動・削除禁止 |
| 開始時未追跡 | 87件 = CFG1所有41＋保護46。その他0 |
| staged | 0 |
| tracked差分 | 既知のSectionView.cs／ProjectSettings.assetだけ。他者変更なし |
| AGENTS | 対象と祖先・対象内探索で該当なし |
| Unity | 6000.3.11f1、WindowsStandaloneSupportあり |
| .NET | SDK 9.0.312、既存ローカル環境 |
| Unity依存 | lockの28件を既存package名・版・CFG1 hashと照合、全件一致 |
| 並行処理 | 対象Unity／Player／dotnet／MSBuildの競合なし。プロセスの実測は [環境監査](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preflight-environment-processes.json) |

開始時の pure C# 4 系列は Core 36 PASS、Geometry 3,372,857 checks、Settings 25 PASS、Interaction 13,746 assertions、すべて exit 0。23ソースの実行前後 SHA が一致した。絶対コマンド・UTC・exit は [baseline-tests-summary.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/baseline-tests-summary.json) と各 baseline-*/execution.json。Geometry 原本内の固定 historical command と今回実コマンドを混同しない。

## 3. §5 に対する実装選択

| 契約上の条件 | 今回採用した具体値・経路 | 根拠・制限 |
|---|---|---|
| 独立デモ識別 | RuleId `CFG2_DEMO_4D`、Version `1`。設定・ルール・初期局面・snapshotのhashと一意match ID | 正式ルールの最終採用ではない |
| 次元・盤・前方 | N=4、各軸−4..4、UI X=Core U／Y=V／Z=F／W=W、前方F=0、A+／B− | CFG1対応を継承。BのW=0制限は新対局へ入れない |
| 初期40駒 | RuleCore.InitialState() の ID・種類・所有・Q4 を新Piece群へ明示変換。A先手、全駒V=W=0 | 旧GameState／旧CPU／27手演出を使って対局を進めない |
| 編集可能な4種 | 王・歩Step、飛H11、角H22。軸／同時軸数／前後横／空セル／捕獲／走り距離を共通定義で解釈 | Step距離1。最大軸数はStepのみ。UI独自の移動式を持たない |
| 固定4種 | `cfg2/gold/1`、silver、knight、lance を追加し既存Movementへ委譲 | N4中心で金14・銀13・桂6。旧提案の金34へ置換しない。香は既存前方走り、盤内で切る |
| 旧定義保全 | CreateExperiment()／legacy/* は従来どおり未対応。CreateMatchDemo() が8種に対応する新プリセット | 旧25試験の数学的期待・未対応表示を維持 |
| 占有と遮蔽 | 味方終点不可、敵捕獲。lattice-line/1 と supercover-closed/1 を選択・開始時固定 | 既存2方式。別方式への自動代替なし |
| 終局 | 王捕獲勝ち／完了済み0手引分／時計0負け／中断は別 | Partial、Cancelled、Unsupported、Faulted を手なし・パス・勝敗へ変換しない |
| 合法性の範囲 | 共通Pに基づく王捕獲デモの着手。全4D攻撃範囲は別集合 | 自玉安全、王手回避、詰み、完全Lを評価したと表示しない |
| 捕獲台 | Captorと元駒を別記録。捕獲側ごとの種類と実個数を描画 | 打てる在庫ではない。王を通常台へ入れない |
| 時計 | 初期600秒ずつ、加算なし。StopwatchClock／試験用制御時計を注入 | フレーム数非依存。普通のfocus喪失で止めない |
| 演出 | 0.45秒の視点交代中は入力・双方時計を停止 | CvC pause中の未完了演出も保持し、完了前にCPUを再開しない |
| モード | PvP両Human、PvC A Human/B Cpu、CvC両Cpu | PvP手番視点、PvC人間側視点、CvC観戦。旧演出棋譜再生を代用しない |
| CPU | `capture-stable-seeded/1`。全4D候補、捕獲評価、seed付き安定同点処理 | 初期seed20261017、候補予算65536、着手前待ち500ms。予算・seedをsnapshotと記録へ |
| CPU取消 | immutable CpuRequest とmatch/revision/turn/rule/generation照合。Task処理はUnity側 | 同局面pause/resumeでも世代を更新。時間切れ後の遅延結果は拒否 |
| 保存 | strict XMLの次局正常設定と不正Draftを別ファイルへ原子的保存。試し盤・安定viewは独立CFG1 bundle | 途中失敗は旧bytes保持。現在対局snapshotを後から編集しない |
| 表示経路 | CFG2専用App／scene／build。既存HDRP・広場・木素材・OSフォントを再利用 | 外部取得なし。画像は比較資料で、背景として貼っていない |

保存時の量的制限（設定XML最大1MiB、記録最大16MiB、CPU予算1～1,000,000、待ち時間0～60,000ms等）は、入力検証と処理量を有限にする実装上の境界である。これを製品ゲーム成立の閾値にしていない。ルールhashは規則と持ち時間、設定hashはCPU条件等も含む。表示名の変更と移動規則を区別する。

## 4. 変更可能範囲と責務

事前の [core-change-plan.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/core-change-plan.json) と [visual-decisions.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/visual-decisions.json) に変更範囲と数値を記録した。

| 境界 | 実ファイル・扱い |
|---|---|
| pure C# の既存2ファイル | SettingsModel.csに新版固定4種とMatch用Draft生成を追加。PreviewSession.csに固定4種の共有照会とAttackCellsを追加 |
| 新対局層 | Runtime/Match/MatchModel.cs／MatchSession.cs／MatchStore.cs。旧GameStateから独立した状態・snapshot・時計・CPU・記録 |
| 新試験 | tests/Showcase.Match.Tests/Program.cs／csproj。旧4試験の削除・緩和なし |
| 新Unity入口 | Cfg2Appの4 partial、Cfg2Smoke、Cfg2Visuals／MiniView／PlaneView／Ui、Cfg2Build、Cfg2 scene／run resource と必要meta |
| 共有描画の限定修正 | CfgPreviewRenderer。既存正確な有限4D断面を使い、CFG2表示・pickを追加。純Geometryは不変更 |
| 旧入口の保全 | Rules.cs／Cpu.cs／Scenario.cs、Cfg1App／Cfg1Smoke／Cfg1Build等を全面置換しない。既存SettingsStoreは不変更 |
| Unity import/build | ProjectSettingsなど許可された生成差分を事前backup/hashと照合。最終一覧は終了時保全監査とbuild manifestで確定 |
| 禁止境界 | 保護46、開始時他者変更、旧証跡、sources、参照画像原本、別repo、Git index/config/historyへ書込まない |

今回最終 pure 試験の前後28件SHAは一致。基準23件との差は許可済みSettingsModel.cs／PreviewSession.csだけで、旧4試験本体とcsprojはそのまま。終了時の [preservation-after.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preservation-after.json) では、保護46/46、開始時tracked2/2、CFG1参照11/11、CFG0原本8/8、旧tracked122/122、旧Windows配布169/169、依存28/28、revision-05固定ソース60/60が一致した。CFG1ソース43件は40件不変・許可済み変更3件（上記2件とCfgPreviewRenderer）、新所有ソース33件、想定外差分0。pure試験のhash確認と全体保全監査を別の証拠として残す。

## 5. 旧196受入とC1の扱い

旧196件の内訳はSW2 48、RE 46、AUTO 78、UI4 10、WIN 7、HUM 7。全ID・元資料行・過去FinalStatus・過去ScopeGapを [原本索引](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/cfg1-reference-index.json) に保持している。旧結果の内訳はSCOPED_PASS 51、SCOPED_EVIDENCE_PARTIAL 71、CFG2_PENDING 32、DEFERRED_NOT_IMPLEMENTED 23、HUMAN_PENDING 16、BLOCKED 3。C1は18件とも旧範囲でSCOPED_PASSであり、完全条件・人間確認まで合格した意味ではない。

| 旧範囲 | 今回の接続先と扱い |
|---|---|
| SW2-01～48 | 設定UI・共有定義・保存・断面に対応。C2-02～04、08、13～16。7カテゴリ／常設保存群は新6カテゴリ・自動保存へ変更 |
| RE-A/B/C/D/E/Hの46件 | UI／駒定義／共有照会／保存／開始／人間に対応。C2-02～16へ内容別に接続し、未実装の一般化や人間承認を自動消化しない |
| AUTO-B/I/G/R/F/C/S/Pの78件 | 基準・入力・幾何・ルール・状態・CPU・保存・表示の回帰。旧pure4系列＋新Match30を使う。旧N2・一般N・完全Lの未達を今回の限定4DデモでPASSにしない |
| UI4-01～10 | 主1＋4、断面・軸drag・カメラ・入力所有を維持。C2-08/10/15/17と新Windows模擬イベント証拠へ |
| WIN-01～07 | C2-18/19とH2-01。過去EXEや過去3解像度PASSを今回の結果に使わない |
| HUM-01～07 | H2-02。人間の可読性・空間理解・操作感をCodexで承認しない |

具体的なC1対応は以下。[legacy-acceptance-map.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/legacy-acceptance-map.json) に全196のOriginalRow・元資料行・過去FinalStatus/Proven/ScopeGap/Evidence、全C1とRelatedOriginalIds、今回C2/H2対応・限定差・関連pure試験名を保存した。196唯一・C1の18唯一・元条件全文一致・旧原本hash不変を検証した。対応表は証拠の参照先であり、旧IDの全条件を一括で今回PASSへ変更するものではない。

| C1 | 今回のC2対応 | 保全／変更／残る制限 |
|---|---|---|
| 01 基準保全 | 01/20 | 開始43/43・46/46、最終監査は別証跡 |
| 02 メニューと対局未接続 | 05～07/13 | 5項目順を維持し、3モード開始へ接続する今回の明示変更 |
| 03 7カテゴリ・日本語 | 13/14/19/H2-02 | 新6カテゴリと承認UIへ更新。可読性の人間承認は残る |
| 04 Step編集 | 03 | 旧F1～F3と使用軸・k・前後・捕獲回帰を維持、新対局実手も試験 |
| 05 H11/H22 | 03 | 距離／二遮蔽を維持し実着手へ。一般Hの完成ではない |
| 06 未対応表示 | 03/04/09/13 | CFG2固定金銀桂香だけ新対応。成り打ち／完全L／N>4は未対応 |
| 07 独立試し盤 | 08/15 | PreviewSession履歴を保全。新MatchStateと相互に状態漏れしない |
| 08 G/P/Lと完了状態 | 03/08/09 | 非完了を0手にしない。完全Lは未評価 |
| 09 全4D利き | 08/15 | AttackCellsを表示へ接続。王手回避／完全Lではない |
| 10 主1＋4・実断面 | 08/15/17 | Geometry/Interaction回帰。2D図は別機能として追加 |
| 11 平行移動とカメラ | 05/08/15 | 論理座標不変。PvP手番視点交代は今回追加 |
| 12 古い結果・入力取消 | 10/12 | CPU世代を追加。OS入力確認とは別 |
| 13 保存・不正Draft | 04/16 | 旧Store／25試験を保全。次局設定と記録は別schema |
| 14 未接続の偽適用禁止 | 02/04/16 | 旧Preview型CanApply=falseを維持。MatchConfig→Snapshot生成で実適用 |
| 15 Legacy演出保全 | 17/20 | Rules/Cpu/Scenarioと旧Core36期待を維持 |
| 16 Windows | 18/19/H2-01 | 今回新EXEと3解像度・別processが必要。旧PASSを流用しない |
| 17 入力所有・scroll | 10/13/15/19 | 現在revision/state/draftで入力検証。物理入力・IMEは別 |
| 18 sparse・取消・負荷 | 09/10/17 | sparse辞書と有限予算。製品FPS閾値／長期stress／強いCPUの性能承認ではない |

旧F1～F4は元のBounds4D(0..4)・元期待値のまま試験し、UI試し盤／対局の−4..4 fixtureと分離した。旧CreateExperimentの未対応金銀桂香を支持済みに書き換えず、CFG2専用kindとプリセットを追加したことが、旧結果と新実装を両立させる具体的な方法である。

## 6. 最終実行と証拠の限界

最終配布はWindows/revision-05/FourDimensionalShogi.exe。Unity 6000.3.11f1でSucceeded、0 errors・2 obsolete warnings、process exit0。1536×1024（PID25700）／1920×1080（PID10128）／1280×720（PID16916）は各176項目PASS・exit0、別process1536再読込（PID17700）は10項目PASS・exit0。全て実Windows Player内のAPI・模擬uGUIイベント証拠である。各初回の非scroll Text高さ測定1727件・clipped0、reloadは111件・clipped0。数値は画像・領域重なり・人間可読性の全証明ではない。

revision-02は84観測後FAILであった。候補が内側にある場合の板面pick経路と実候補タイルを追加し、続くrevision-03は141項目、04は173項目を通過した。木駒二重表示、メニュー残像、捕獲個数の可読性、30億秒入力のint overflowも修正・追加検証した。失敗原本を消さず、最終成功で過去結果を書き換えていない。

詳細・コマンド・log・各終局棋譜は [02報告](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/02_CHANGES_AND_TESTS_JA.md) の最終runtime欄。物理マウス／キー／IMEとOS倍率操作は今回の自動入力経路では確認できておらず、人間の外観・可読性・操作感確認も別に残る。全C2/H2の最終判定は親担当のacceptance-cfg2.jsonへ集約する。新工程、公開、外部取得、Git操作へは拡張しない。

