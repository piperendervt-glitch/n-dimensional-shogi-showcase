# CFG2 実装変更と試験結果

最終結果：pure C# の5系列はすべて成功。Windows revision-05で1536×1024・1920×1080・1280×720が各176項目PASS、別process再読込が10項目PASS、全process exit0。3モードの通常40駒開始・実着手と、明示fixtureの通常終局まで確認した。物理OS入力・人間の最終検収は別である。失敗した中間revisionの原記録も残す。

対象：[作業リポジトリ](C:/dev/n-dimensional-shogi-showcase)。今回run：[CFG2-20261017-004021-JST](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST)。基準と仕様判断は [01報告](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/01_PREFLIGHT_AND_DECISIONS_JA.md)。根拠の強さはpure C#／Player API／模擬uGUI／OS入力／人間判断を分ける。

## 1. 実装した接続

`SettingsDraft + MatchOptionsRaw → MatchConfig.TryCreate → MatchSnapshot → MatchSession` を新設した。現在対局は開始時に複製・固定した定義で動き、次局Draftや試し盤の変更を受けない。正常保存と実適用は別であり、保存成功だけを適用成功としない。

PreviewQueryをG/Pと攻撃範囲の正本とし、8駒の両側全4Dを試し盤・人間・CPUで共有する。HumanはTryMove、CPUはimmutable CpuRequestの結果をTryApplyCpuで検証し、最後は同じCommitMoveを通す。状態はsparse駒辞書、CPU結果はmatch ID／revision／手番／rule hash／要求世代を照合する。

時計は注入可能な単調時刻源から差分を取る。着手確定直前にも期限を確認し、終局後の手・CPU・時計更新を拒否する。王捕獲、完了済み0手、時間切れ、中断は別の結果。処理の予算終了・取消・障害はProcessingPausedであり、0手引分ではない。

## 2. 実ファイルと変更理由

| 区分 | ファイル | 内容・検証 |
|---|---|---|
| 既存変更 | [SettingsModel.cs](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Settings/SettingsModel.cs) | CFG2固定4種を新kindで追加。CreateMatchDemoと旧CreateExperimentを分離。元25試験不変 |
| 既存変更 | [PreviewSession.cs](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Settings/PreviewSession.cs) | 固定4種のDeltas/Allows、slider遮蔽、全4DAttackCells。PreviewとMatch同値試験 |
| 新規 | [MatchModel.cs](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Match/MatchModel.cs) | 実行可能設定、初期40変換、独立sparse状態、共通QueryLegal、hash、controller/clock型 |
| 新規 | [MatchSession.cs](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Match/MatchSession.cs) | snapshot、Human/CPU共通commit、捕獲、各終局、単調時計、演出pause、CvC制御、取消世代、イベント |
| 新規 | [MatchStore.cs](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Match/MatchStore.cs) | 新strict XML。正常/不正別保存、atomic置換、full snapshotと棋譜、上限・未知schema拒否 |
| 新規 | [Match.Tests Program.cs](C:/dev/n-dimensional-shogi-showcase/tests/Showcase.Match.Tests/Program.cs)／[csproj](C:/dev/n-dimensional-shogi-showcase/tests/Showcase.Match.Tests/Showcase.Match.Tests.csproj) | 30ケース337assertions。旧4試験を削除・緩和しない |
| 新規Unity | [Cfg2App.cs](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2App.cs)／[Ui](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2App.Ui.cs)／[Match](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2App.Match.cs)／[Input](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2App.Input.cs) | 5メニュー、6カテゴリ、auto-save、3モード、非同期CPU、時計・視点、実pointer入力、試し盤・対局分離 |
| 新規Unity | [Cfg2Visuals](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2Visuals.cs)／[Cfg2MiniView](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2MiniView.cs)／[Cfg2PlaneView](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2PlaneView.cs)／[Cfg2Ui](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2Ui.cs) | 実木盤・木駒・捕獲側台、4超平面、固定2軸図、木駒一覧。見た目の承認は別 |
| 既存共有描画 | [CfgPreviewRenderer.cs](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/CfgPreviewRenderer.cs) | 既存SectionResultを維持しCFG2の半透明面・表示制御・実mesh pickを接続。内側候補は実Q4の板面Pタイルと優先pickを追加し、最終3解像度で再検証 |
| 新規Windows試験 | [Cfg2Smoke.cs](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Settings/Cfg2Smoke.cs) | API setupと模擬uGUI dispatchを明示。実pick→実commit→通常終局・保存・再起動を試験する入口 |
| 新規build | [Cfg2Build.cs](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Editor/Cfg2Build.cs)、Cfg2.unity、Assets/Resources/cfg2-run.jsonと各meta | CFG2専用起動scene、今回run保存先、既存EXE上書き拒否。Mono Windows64 |

Rules.cs／Cpu.cs／Scenario.cs、SettingsStore.cs、純Geometry/Interactionの論理、旧4試験本体とcsprojは変更していない。旧CFG1メニュー・試験・build入口は残る。新meta、Unity import/buildによる許可された設定差分と最終全ファイルhashは最終保全監査・build manifestで確定する。

## 3. 最終 pure C# 回帰

実行は2026-10-17 01:04:15～01:04:48 JST（ログUTC 2026-10-16 16:04:15～16:04:48）。全系統 `dotnet run --no-restore --project tests/Showcase.<系統>.Tests/Showcase.<系統>.Tests.csproj --configuration Release -- <今回run>/final-<系統>`。実コマンド・絶対出力先・時刻・exit・前後hashは [final-pure-tests-summary.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-pure-tests-summary.json)。

| 系統 | 実測 | exit | 実行全体 | 結果／ログ |
|---|---:|---:|---:|---|
| Core | 36 PASS、0 FAIL | 0 | 24.568秒 | [tests](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-core/tests.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/final-core.log) |
| Geometry | 3,372,857 checks、TECHNICAL_PASS | 0 | 2.226秒 | [report](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-geometry/geometry-test-report.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/final-geometry.log) |
| Settings | 25 PASS、0 FAIL | 0 | 3.335秒 | [tests](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-settings/tests.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/final-settings.log) |
| Interaction | 13,746 assertions、PASS | 0 | 1.536秒 | [report](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-interaction/interaction-results.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/final-interaction.log) |
| Match | 30 PASS、0 FAIL、337 assertions | 0 | 1.648秒 | [tests](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-match/tests.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/final-match.log) |

試験前後28/28ソースSHAが一致した。開始時23件との比較ではSettingsModel.csとPreviewSession.csの許可された2変更だけで、既存4試験原本は不変。追加テストプロジェクトのassets初期生成は空の今回local sourceと既存SDK/cacheだけで行い、その後は--no-restore。外部依存の取得・更新はしていない。

新Match初回は28 PASS／324assertionsで、レビュー後に確定直前期限と演出pause/resumeの2境界試験を追加した。最終は30 PASS／337assertions。ケース削除や期待値の緩和で成功させていない。初回テストのnullable警告はGetValueOrDefaultによる明示アクセスに修正し、最終pureログに警告はない。Unity側警告は別記する。

## 4. 新Match 30試験の具体的な対応

下表の全行は [final-match/tests.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-match/tests.json) の同順のtest nameに対応し、入力・期待は [Program.cs](C:/dev/n-dimensional-shogi-showcase/tests/Showcase.Match.Tests/Program.cs) に保持する。すべてpure C#である。fixtureから結果flagを直接設定して合格にした試験はない。王捕獲は通常commit、0手は共通候補の完了結果、時間切れは制御時計から発生させる。

| No. / テスト名 | 目的・入力 | 実測された期待 |
|---|---|---|
| M01 / `C2-02-normal40-explicit-conversion-snapshot-all-modes` | 正常40駒を3モードへ開始 | PASS — 旧配置のID・種類・所有・座標を独立状態へ変換し、controller／初期hashが一致。 |
| M02 / `C2-03-eight-kinds-both-sides-preview-match-same-independent-coordinate-sets` | 8駒×両陣営の独立集合 | PASS — 独立に組み立てた座標集合とPreview/MatchのG/Pが一致。固定金14・銀13・桂6を含む。 |
| M03 / `C2-03-B-W-axis-moves-and-knight-jumps-with-blocker` | BのW移動・桂の跳躍 | PASS — BにもW候補があり、桂の途中遮蔽物で跳躍先を消さない。 |
| M04 / `C2-03-lance-both-sides-blockers-two-occlusions` | 香・2遮蔽・両側 | PASS — 前方走りの途中障害／敵終点／味方終点を両陣営・両遮蔽で照合。 |
| M05 / `C2-03-edit-axis-k-pattern-distance-forward-capture-reflects-real-move` | 設定差→実手 | PASS — 使用軸、k、方向／パターン、H11/H22距離、空セル／捕獲制御を変え、Previewと実commitの可否へ反映。 |
| M06 / `C2-03-H22-corner-occlusion-changes-actual-capture` | H22隅遮蔽→実捕獲 | PASS — 同じ配置と移動先でlatticeとclosed Supercoverの捕獲結果が変わる。 |
| M07 / `C2-04-invalid-draft-unknown-missing-fixed-components-no-apply` | 不正Draft・欠けた固定定義 | PASS — 旧実験、文字入力、未知kind、欠けた駒種、固定部改変、違う盤境界を拒否し生入力不変。 |
| M08 / `C2-04-initial-kings-IDs-cells-side-kind-bounds-validation` | 開始配置妥当性 | PASS — 王欠損／重複、ID・セル重複、未知陣営／種類、範囲外を拒否。 |
| M09 / `C2-02-snapshot-clones-draft-options-and-freezes-current-match` | snapshot不変 | PASS — 生成後のDraft・options・返却コピーを編集しても現在のrule/state/snapshot hashと時間は不変。 |
| M10 / `C2-05-06-07-three-modes-normal-king-capture-handler-and-record` | 3モードの通常王捕獲処理 | PASS — 短い明示fixtureでHumanまたはCPUが通常commitを行いKingCaptureへ。新局は別ID・40駒。UI試験ではない。 |
| M11 / `C2-08-full4D-hidden-attacks-obstruction-independent-preview` | 表示外4D攻撃と独立preview | PASS — W4のB飛がW2を攻撃し、コピーにW3遮蔽物を置くとコピー側のみ消える。元局面の実捕獲が可能。 |
| M12 / `C2-09-no-moves-initial-and-after-commit-complete-only` | 完了済み0手 | PASS — 開始時0手と着手後の次手番0手が別ケースでNoMovesDrawとなる。 |
| M13 / `C2-09-partial-cancel-fault-never-zero-or-draw` | Partial・Cancelled・処理障害 | PASS — 予算1と事前cancelは手を確定せずProcessingPaused。明示障害注入もOngoingであり引分にしない。 |
| M14 / `C2-10-pick-revision-generation-double-commit-rejected` | revision・世代・二重着手 | PASS — 不一致の世代／revision、同じ移動の二重入力を拒否しplyが増えない。 |
| M15 / `C2-10-cpu-stale-pause-resume-new-match-end-duplicate` | 古いCPU結果 | PASS — pause/resume、終了、別match、同じ結果の再配送を拒否し、正しい新要求のみ1手を確定。 |
| M16 / `C2-11-both-capture-trays-owner-and-no-drop` | 両捕獲台・再投入禁止 | PASS — A/B双方の捕獲者・元駒・個数を検証し、消えたvictimを着手駒として再投入できない。 |
| M17 / `C2-12-clock-switch-presentation-focus-no-frame-dependence` | 時計とフレーム非依存 | PASS — 2秒／3秒／7秒、同時刻100tick、演出100秒。現在側のみ減り、演出中双方不変。focusイベントの物理試験ではない。 |
| M18 / `C2-12-zero-boundary-human-and-late-CPU-cannot-overturn` | 0秒境界・後着CPU | PASS — 0.1秒ちょうどで人間手／CPU結果よりTimeLossが優先。終局後時計・状態が凍結。 |
| M19 / `C2-12-CvC-pause-stops-both-step-exactly-one-and-repause` | CvC pauseと1手 | PASS — 明示pause50秒、step思考1秒、演出20秒、その後pause30秒。正確に1手・再停止。 |
| M20 / `C2-12-nonmonotonic-source-rejected-no-clock-rewind` | 異常時刻源 | PASS — 時刻逆行とNaNを拒否し、既に残る597秒を巻き戻さない。 |
| M21 / `C2-12-clock-deadline-rechecked-after-move-validation` | 確定直前の期限 | PASS — 手の検証中に進む時計で期限を再確認し、王捕獲が時間切れを上書きしない。 |
| M22 / `C2-12-pause-resume-during-presentation-never-reopens-input-early` | 演出中pause/resume | PASS — 演出の未完了状態を保持し、resume直後にCPUや時計を早期再開しない。 |
| M23 / `C2-09-interrupt-is-neither-win-nor-draw-and-finished-frozen` | 中断と終局後凍結 | PASS — 中断に勝者を付けず、後のtick／CompletePresentation／再中断がhashを変えない。 |
| M24 / `C2-06-07-cpu-stable-seed-capture-score-and-common-validation` | CPU捕獲評価・安定seed | PASS — 共通候補で高い捕獲価値を選び、同一状態・seedで同じ手。通常検証を通して確定。 |
| M25 / `C2-16-valid-settings-roundtrip-start-all-modes-without-CFG1-mutation` | 正常保存→再読込→3モード | PASS — H11距離2、Supercover、125.5秒、seed−71が再読込後の設定hash・3 snapshotへ一致。同一pure process。 |
| M26 / `C2-04-16-invalid-raw-separate-from-last-valid-and-cross-kind-rejected` | 正常保存と不正rawの分離 | PASS — MaxAxes=12x、時刻=生入力を別Draftへ保持し、最後の正常bytes不変。保存種別の交差上書きを拒否。 |
| M27 / `C2-04-16-atomic-faults-preserve-old-normal-and-raw-dirty` | 原子保存の故障注入 | PASS — 書込前／flush後／置換直前で例外。既存bytesとdirtyを保持し、初回失敗は宛先を作らずtmpを残さない。 |
| M28 / `C2-04-unknown-schema-fields-definitions-DTD-rejected-no-byte-change` | 未知XML拒否 | PASS — schema、属性、要素、kind、DTDの不正入力を拒否し入力ファイルbytes不変。 |
| M29 / `C2-09-record-full-snapshot-move-captor-clock-hashes-and-atomic-failure` | 記録と記録保存の故障 | PASS — full snapshot／元40またはfixture／手／captor／前後hash／2.5秒の残時計をXML確認。王tray=false。置換前失敗で旧記録不変。 |
| M30 / `C2-17-CFG1-legacy-unsupported-fixture-and-original-game-unchanged` | CFG1境界保全 | PASS — 旧CreateExperiment金未対応・CanApply=falseと旧GameStateの独立性を保持。新2手を旧状態へ漏らさない。 |

保存・終局記録の試験実データは [今回Matchデータ](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/final-match/data-20261016-160447-9787)。ここで生成したterminal-PvP/PvC/CvC.xml、journal.xml等はpure試験のfixture記録であり、Windowsの人間UI終局棋譜とは区別する。

## 5. C2受入への対応と限界

機械可読の具体対応は [core-test-coverage.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/core-test-coverage.json)。pure試験は以下の必要部分を実証するが、単独でC2全件PASSにはしない。

| C2 | pure証拠 | 組み合わせるWindows／UI証拠（最終実測は§8） |
|---|---|---|
| 02 | M01/M09/M25：40駒、3snapshot、固定hash、保存との分離 | 設定画面→モード選択→開始 |
| 03 | M02～06：8種×両側、軸/k/距離/捕獲/遮蔽、B W、桂、香 | 実入力field/toggle→共通照会→次局実手 |
| 04 | M07/08/26～28：不正raw・schema・欠けた配置・atomic failure | 短いfieldエラー、開始拒否、UI保存失敗保持 |
| 05～07 | M10/M24：通常commitとController経路、捕獲、CPU評価、次局 | 両Human UI／非同期CPU／3モード終局と再開始 |
| 08 | M11：全4D表示外の攻撃・遮蔽・独立copy | 断面・表示切替前後のhashと候補、実描画 |
| 09 | M10/12/13/18/23/29：各終局と障害の区別、終了後凍結、記録 | 通常終了handlerと結果panel、停止・メニュー |
| 10 | M14/15：世代・revision・新局・重複拒否 | real mesh pickとDown/Up・UI非同期競合 |
| 11 | M10/16：捕獲側・実個数・王除外・打ちなし | 台の描画・位置・視点交代時所有 |
| 12 | M17～22：単調時間・0境界・演出pause・CvC1手 | Stopwatch実時間、focus handler、画面時計 |
| 15 | M02/M11＋Settings25：共有G/P/攻撃、試し盤独立 | 主4D・2D4図・超平面4窓・実試し操作 |
| 16 | M25～28：正常/不正分離、roundtrip、保全 | 別Windows process再起動→復元→実開始 |
| 17 | 既存4系列と新Match成功、ソースhash比較 | 旧証拠・全Unity・保護46の最終監査は別 |

C2-01/20の保全、C2-13/14のUI・画像、C2-18/19のWindowsと解像度、H2-01物理入力、H2-02人間確認をpure試験で代替しない。旧196/C1との対応と旧fixture保全は01報告§5に記載。

## 6. レビューで見つけた不具合と修正

| 問題 | 影響 | 修正・確認の状態 |
|---|---|---|
| 手の検証時間が期限判定後に経過 | 王捕獲が期限後に確定する可能性 | 共通CommitMoveで確定直前Tick。M21 PASS |
| 演出中にCvC pause/resume | 演出終了前に時計・CPUが再開する可能性 | presentationPendingを保持。M22 PASS |
| PvP視点とカメラ操作値の不一致 | B側から次のorbit/zoomで旧A値へ戻る | SetMatchCameraでyaw/pitch/distance/panも同期。ソース修正確認。最終Windowsは手番視点・時計ラベル・台所有を検証。B交代直後の全camera操作組合せの網羅とはしない |
| 試し盤読込の部分反映 | view復元失敗時にpreviewだけ変わる | 仮SliceAnimatorで検証してから一括代入。参照・camera範囲検証。最終別processで正常復元を確認。不正load境界はpure原本回帰とソース確認を区別 |
| 自動保存失敗でdirty消去 | 再試行や退出保留がなく入力を失う | 成功boolとdirty保持、退出保留、wantsToQuit。最終Windowsは正常/不正Draft別保存を確認。書込例外注入のatomic保全はpureで検証 |
| Ctrl+Z/Yの履歴所有切替 | 試し盤Undo後、次のUndoがDraftへ移る | trial経路でAfterTrial。元履歴責務を維持 |
| 対局が試し盤cameraを上書き | 設定へ戻ったとき別cameraを保存 | 開始前trial camera保持、復帰時復元 |
| 2D図のDown/Up間に状態変更 | slice revisionだけでは古いpickを見逃す | state/draft/描画世代を照合。rev02途中試験でstale拒否を観測 |
| 2D/Mini再初期化 | 旧子UIの重複 | 旧子を即非表示にしてDestroyし再構築 |
| 4D内側の移動候補pick | rev02でBの正しいP候補をクリックできない | 板面の実P候補タイル・優先pickを接続。rev03以降の両側UI着手と最終3解像度の王捕獲まで再検証。FAIL原本保持 |

## 7. Windows試行履歴

| 試行 | 実測結果 | 判定・証拠 |
|---|---|---|
| revision-01 build | Licensing IPC接続timeout。import前に停止、exit−1、EXEなし | 環境失敗。[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-exit-revision-01.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/unity-revision-01.log)。今回起動procを回収し正規escalation経路へ |
| revision-02 build | Unity 6000.3.11f1、Succeeded、0 errors、42 warnings、613,330,021 bytes、build本体19.139秒、process43.781秒、exit0 | build成功のみ。[build-report](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-02/build-report.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-exit-revision-02.json)／[log](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/logs/unity-revision-02.log) |
| revision-02 Player 1536×1024 | PID6448、RTX5060Ti、Player報告DPI96。84観測後FAIL、exit1、18.158秒 | [smoke原本](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1536x1024-6448.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/player-revision-02-initial-1536x1024-exit.json)。3モードnormal40開始と着手等の途中成功を最終合格にしない |

revision-02の失敗は `CFG2 SMOKE no visible mesh pick for PvP B real reply destination 1,1,0,0; selected=b-king`。短いPvP fixtureでA捕獲・視点交代・B選択までは観測されたが、移動先のpickが得られなかった。板面の実Q4候補の表示とpickを修正し、新しいrevisionで同じ通常commit経路から終局まで再実行した。

Unityの42 warningsにはnullable annotations contextとobsolete API等があり、build成功を警告ゼロと表現しない。ライセンス接続の初回失敗とソース／操作不具合を分け、失敗logは消さない。既存ローカル依存だけを使用し、外部取得禁止を保った。

| 続く試行 | 実測結果 | 証拠・修正 |
|---|---|---|
| revision-03 | build0 errors/2 warnings、1536×1024は141項目PASS・exit0 | [build](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-03/build-report.json)／[smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1536x1024-19216.json)。内側pick解消後の終局を確認。枚数札の可読性を追加修正 |
| revision-04 | build0 errors/2 warnings、1536×1024は173項目PASS・exit0、layout1702/clipped0 | [build](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-04/build-report.json)／[smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1536x1024-15764.json)。WorldSpace Canvas枚数表示・レイアウト測定を追加 |
| revision-05 | build0 errors/2 warnings、3解像度各176項目PASS、reload10項目PASS | [build](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-05/build-report.json)。時計表示をlong化し30億秒のUI境界3項目を追加。最終証拠は次節 |

revision-03～05の表は上の試行履歴の続き。最終2 warningsはCfg1App/Cfg2Appの既存`StandaloneInputModule.forceModuleActive` obsolete警告である。ログには同警告が複数compile工程で現れるが、BuildReportのtotalWarnings=2と重複行数を混同しない。

追加の画像修正は [revision-02画像レビュー](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/ui-runtime-review-revision02.md)、[revision-03画像レビュー](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/ui-runtime-review-revision03.md) に記録。安定XYZ/中心0対局では物理板と空中に同一木駒を二重描画せず、回転/移動/別姿勢は正確な空中切断を表示する。物理板と空中の候補集合・元Q4は保持。メニュー残像は背景カメラ分離、捕獲個数は実CountのWorldSpace Canvas札、長い固定駒名は一覧短名化で修正した。

## 8. 最終Windows実測

最終配布物は [FourDimensionalShogi.exe](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-05/FourDimensionalShogi.exe) と同階層の依存一式。Unity 6000.3.11f1、StandaloneWindows64/Mono、build GUID `a9d7dc9552b44deaae4a0f79240ce060`、0 errors/2 warnings、613,337,701 bytes、BuildReport 23.302秒、起動process 33.622秒、exit0。build commandとソースhashは [launch](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-launch-revision-05.json)／[source](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-source-revision-05.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-exit-revision-05.json)。

| 実行 | 判定 | Text高さ測定／clipped | process | 結果・exit |
|---|---|---:|---|---|
| 初回1536×1024 | 176 PASS | 1727／0 | PID25700、39.422秒、exit0 | [smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1536x1024-25700.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/player-revision-05-initial-1536x1024-exit.json) |
| 初回1920×1080 | 176 PASS | 1727／0 | PID10128、39.126秒、exit0 | [smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1920x1080-10128.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/player-revision-05-initial-1920x1080-exit.json) |
| 初回1280×720 | 176 PASS | 1727／0 | PID16916、37.953秒、exit0 | [smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-initial-1280x720-16916.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/player-revision-05-initial-1280x720-exit.json) |
| 別process再読込1536×1024 | 10 PASS | 111／0 | PID17700、7.119秒、exit0 | [smoke](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/smoke-reload-1536x1024-17700.json)／[exit](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/player-revision-05-reload-1536x1024-exit.json) |

実行時刻は2026-10-17 01:20:23～01:23:13 JST（原ログUTC）。RTX 5060 Ti、Player報告DPI96。OS表示倍率の変更・物理resizeを実施した証拠とはしない。Text測定はactive・非scroll領域の高さを検査し、scroll内文字を除外している。各画面画像の視認確認と分け、これだけで全領域重なり／操作感／全フォントの可読性を保証しない。

176項目には5メニュー順、6カテゴリと3詳細タブ、攻撃overlay／表示OFF時候補不変、4つの2D固定座標、2D配置/移動/消去/clear/Undo/Redoと古いstate/draft拒否、独立試し捕獲、王k=1によるG=8→3モード40駒開始、全4D切替不変、開始後snapshot固定、両Human／CPU実着手、CvC pause/resume/1手・stale拒否、3モードの通常王捕獲、捕獲側台・×1札・王除外、実時間停止／時間切れ、no-moves、無効raw保持・開始拒否・正常保存保全を含む。各観測のlayer/UTC/matchId/positionHash/settingsHash/plyを結果JSONに保持する。

30億秒をUI欄へ入力すると実行可能値として保持され、long化した時計表示は非負の50,000,000分となる。閾値追加や値丸めで回避したものではない。設定保存の終値は試験で明示した王k=2、実G=32などであり、通常デモの初期600秒を恒久変更して証拠を作っていない。

### 通常終局の具体的な記録

最終1536初回の代表棋譜を以下に示す。通常40駒開始と2手以上の進行は別観測で確認し、終局は明示した短いfixtureを正常な開始constructorへ渡した。人間側は模擬uGUI→実mesh pick→共通commit、CPU側はTaskで共通候補を評価→通常TryApplyCpuを通る。王を直接消す、結果flag直接set、脚本再生、手数上限を引分とする方法は使っていない。

| 条件 | 実終局・勝者 | 記録／fixture | 終局hash |
|---|---|---|---|
| PvP | KingCapture・A | [1ed39038…xml](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/1ed3903854084dae848f89241001a51f.xml)／smoke-pvp-capture-and-king/1 | `f84a177c9ea64c082d90b1f552315b411e5dbfc4aefe1382e233be5ffa2467f8` |
| PvC | KingCapture・B | [39a31b5b…xml](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/39a31b5bfac143d1bced1a7fde8ff613.xml)／smoke-pvc-cpu-capture-and-king/1 | `8105c1d50d7c670c6e783e955820b69eead8adada803853512ef3a8daedf04b4` |
| CvC | KingCapture・A | [ef654342…xml](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/ef654342cceb4f7db64d5e70b1c1d9b6.xml)／smoke-cvc-natural-royal-capture/1 | `1dcf5c6046d781131aa25496ed0a16c6391851e4b36f2fb52bf82c55a9c40d0e` |
| 短時間 | TimeLoss・B | [c16e0e42…xml](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/c16e0e42a3244852b23efe8306072bb7.xml)／smoke-real-time-loss/1 | `2454ae6fff3893ce264a5a7d5c794256dda9309b8c28bdb74ba7d65067db5fce` |
| 完了0手 | NoMovesDraw・勝者なし | [e6fc2132…xml](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/e6fc21324bb44292b0daf091ca40e193.xml)／smoke-complete-zero-moves/1 | `f441f1af6209cbe8dbb88aa329574660d6f48dbd663caf22d5539e63ed4b9844` |

各XMLにrule ID/version/hash、設定hash、固定snapshot、元配置、move/capture/captor、前後hash、単調時刻・UTC・残時間・CPU seedと要求世代を保持する。PvP/PvC/CvC終局後の時計・入力・CPU停止と新しいnormal40開始も同smokeで確認した。メニュー復帰はInterruptedとして記録し、勝ちや引分に変換しない。

### 別processの復元

PID17700はPID25700で保存した最後の正常設定を読み、設定hash `4a34d950d5bc7b05e692e80acb1605bd04d21d04927c12a58bf23b1025f92590` を復元した。編集した王/時間のraw値・試し盤・安定4D中心を確認して新match `1d9911632dd247bf886cbbe0da49ffb3` を生成し、模擬UIの実着手でply1／hash `cfe437934f4409d02649a6ee04d32aa529c2299506514ac43c45d3956289dbce` へ進んだ。これは次局設定の復元であり、対局途中の保存再開機能ではない。

## 9. 保全と残る人間確認

[preservation-after.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preservation-after.json) の実測は保護46/46、CFG1参照11/11、CFG0原本8/8、旧tracked122/122、開始tracked2/2、旧Windows169/169、依存28/28、最終buildソース60/60一致、想定外差分0。CFG1ソース43件中3件だけ許可済み変更、今回新所有33件。旧196条件とC1全18の機械対応は [legacy-acceptance-map.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/legacy-acceptance-map.json)。

物理マウス・キーボード・IME・OS倍率変更はこの証拠では合格にしていない。H2-01の経路制約と具体的な人間手順、H2-02の可読性・空間理解・視点移動・デモ仕様の確認は親担当の04報告に残す。UI比較、最終受入、配布物hashと到達状態は00/03/04・acceptance-cfg2.json・build-manifest.jsonへ集約する。今回の機能合格を旧196条件の全達成や正式ルール採用へ拡張しない。


