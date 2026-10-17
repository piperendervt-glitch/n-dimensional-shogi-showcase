# PIVOTの引継ぎ変更と今回の修正台帳

2026年10月17日。依頼ID `PIVOT-RESTART-20261017-083955-JST`。対象は `C:\dev\n-dimensional-shogi-showcase`。本書はCodex受領後の変更を、引継ぎ元の実装と分けて記録する。旧Showcase Goalの再開、新しい対局ルールの採用、外観の最終承認を意味しない。

build-009の新規ビルドと全8回の自動Player試験は成功した。別PlayerでOS実入力を確認し、確認できた操作とドラッグ・Shift+Home等の残る人間確認を分けて記録した。試験用Playerは正常終了済み。build-007は1920×1080の軸ドラッグ検査でFloat32入力の丸めを検出した過去の版として保持する。以下の実行結果は個別の証拠に限定し、全受入の完了判定は最終受入表へ記録する。

## 基準と変更の帰属

- 引継ぎ元の基準・変更25ファイルは、既存の [HANDOFF_JA.md](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/HANDOFF_JA.md)、`handoff/source-manifest-at-handoff.json`、`handoff/source-change-ledger.json`、`handoff/source-diff.patch` に保持されている。本書作成で原記録を編集していない。
- Codex受領時の確認は `codex-continuation/receipt.json` と `final-review/preservation-receiving.json`。07の担当変更だけを適用し、固定依頼書00と設計原本の仕様・保全制約を維持した。
- 今回の変更前テキストは `codex-continuation/source-before/Cfg2Smoke.cs`、`Cfg2App.PivotSmoke.cs`。再修正前は `before-build-006/Cfg2Smoke.cs`、`before-build-007/Cfg2Smoke.cs`、`before-build-008/Cfg2App.PivotIntegrationSmoke.cs`、`before-build-009/Cfg2App.PivotIntegrationSmoke.cs` へ別に保存されている。
- [今回の3ソース差分](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/codex-product-smoke-diff-build009.patch) と [引継ぎhashとの差分](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/codex-source-delta-from-handoff-build009.json) を併記した。後者は引継ぎmanifestに列挙されたファイルの比較であり、全workspaceのGit差分を代用するものではない。build-007時点の先行差分も上書きせず残した。

## 引継ぎ時点で既に存在した製品変更

次の内容は今回新たに作った成果として数えない。引継ぎ時に未ビルドだった変更も保全し、新版へ含めた。

| ファイル／機能 | 引継いだ内容 | 確認する境界 |
|---|---|---|
| `Unity/Assets/Editor/Cfg2Build.cs`、`Cfg2App.Identity.cs`、build／launchスクリプト | 固定依頼・設計・ソース集合・Cfg2 scene／app・Unity実引数・GUID・実装DLLを含む全配布物・Player自己identityを結ぶ。除外は生成 `Unity/Assets/Resources/pivot-build.json` と `.meta` の正確な2パスだけ | EXE単体hashや名前だけで版を認定しない。新ソースが残れば旧版を起動先にしない |
| `Cfg2App.cs`、`Cfg2App.Persistence.cs`、`PivotSettingsLocation.cs`、`PivotStorageTests.cs` | 選択した保存bundleへの起動接続、新bundle復旧、成功後だけの選択先切替、破損原本と編集中状態の保全 | 純C#保存試験と実Player復旧を別に記録する |
| `Cfg2App.Input.cs`、`Cfg2App.Ui.cs`、`Cfg2App.Match.cs`、`Cfg2Ui.cs` | 入力所有と画面切替時の取消、操作開始時状態へのrollback、軸ドラッグによる中心移動、Home／Shift+Home | 模擬イベントとOS実入力を分ける |
| `Cfg2App.PivotSmoke.cs`、`Cfg2App.PivotIntegrationSmoke.cs` | 実meshの独立座標検算、未選択からの実subject選択、空断面の描画／pick除去、選択色に属する実線だけの枠検査、保存復旧の試験経路 | 枠の色限定補強はbuild-004後の未ビルド変更だった。新版の検査が必要 |
| `Cfg2ViewGraphics.cs`、`Cfg2PlaneView.cs`、`Cfg2Ui.cs`など | ギズモ線幅・黒縁・矢先・ラベル背景、設定2D小窓、半透明パネルの周囲線、ラベル折返し、プレビュー倍率、背景への断面重複防止 | 画像の `approved-*` 名は人間承認済みを表さない。外観採否は別 |
| `Cfg2.unity`、resource／meta | import・ビルドに伴うscene、識別resource、必要meta | 生成物を製品ロジックの新機能と数えない |

`HANDOFF_JA.md`のbuild-004・698検算PASSは過去の版の記録である。受領時のusableBuild=nullを尊重し、build-005以降を実際に新規ビルドした。

## Codex受領後に行った修正

| 対象 | 問題と変更 | 保持した期待値・検証 |
|---|---|---|
| `Cfg2Smoke.cs` の `SmokeSteps2` | `pivot-recovery`／`pivot-recovery-reload` が `PivotSteps` へ振り分けられず、既存の復旧手順まで到達しなかった。両phaseをdispatcherへ追加 | 復旧の実装自体を新規追加したとの主張はしない。破損原本・復旧先・別Player再読込は専用実行記録で確認 |
| `Cfg2App.PivotSmoke.cs` | XW90でW接線移動後と一般合成姿勢に、既知mesh三角形に対する `SmokePointForCell2` の実hit検査を各1件追加 | 固定セルを対象にし、周辺画素探索や別セルへの期待値置換をしない。既存の選択色付き実線の独立C/B/A検算を保持 |
| `Cfg2Smoke.cs` のPvP fixture | B応手先の既知三角形へのrayが手前の別候補に遮られていた。元の遮蔽を負のfixtureとして検査し、公開pointer操作経路の固定カメラyaw=-27°／pitch=0°を追加 | B応手先 `(1,1,0,0)`、後続A王捕獲先、手数、勝者は不変。カメラ変更前後の局面hash・全P集合不変、実pick、1手だけ確定、重複up拒否を検査 |
| `Cfg2Smoke.cs` のPvC fixture | 同じ問題がAの最初の空セル移動先にもあった。公開pointer操作経路の固定yaw=-90°／pitch=0°で元の2移動先が見える側から操作 | `(-2,0,0,0)`、`(-1,1,0,0)` の元の目的地、CPU捕獲・終局の期待値を保持。局面hash／合法候補不変を検査し、手動camera保持の基準はこの操作後に採取 |
| `Cfg2App.PivotIntegrationSmoke.cs` のPV10入力検査 | 実Float32端点を独立にdouble投影し、実軸移動との差を1e-9セル未満で検査する処理と端点ログを追加。理想1セルの許容差は入力の丸め予算に合わせ1e-5セルへ | 基底不変の厳密比較を保持。動作コードの丸めや軸感度は変更せず、理想量と実際に渡した入力から求まる量を分ける |
| `START_PIVOT.cmd` | 引数でbuild／run／datasetを差し替えられない固定入口を作成。引数なし、または単独 `--verify-only` だけを許す | `launch_player.py` の厳密版照合を必ず経由。009／user-final-009／1280×720へ固定し、verify-onlyと6種類の上書き拒否を再確認。人間用datasetは未起動 |

今回の手書きUnityソース変更は上記3ファイルの試験経路・検査に限定される。build-009時点の引継ぎmanifest比較で差があった他の2ファイルは、ビルド生成の `Unity/Assets/Resources/pivot-build.json` と `Unity/Assets/Scenes/Cfg2.unity`。既存のpicker、Core移動規則、G/P生成、初期配置・移動先を変更して試験を通したものではない。

## initialの失敗から修正まで

| build | Unity結果／GUID | 実Player結果 | 対応 |
|---|---|---|---|
| 005 | Succeeded、errors=0、`6b98d272f21f493a83d6c5edade81600` | PID32844、initial FAIL。107検算を記録した後、PvP Bの目的地 `(1,1,0,0)` に既知mesh点から届かず終了 | 失敗JSONとログを保存。独立ray解析後、固定PvP cameraを追加して006へ |
| 006 | Succeeded、errors=0、`84cba002240b466e8f32bd5e9d8e20c0` | PID10280、initial FAIL。124検算を記録した後、PvC Aの最初の目的地 `(-2,0,0,0)` に届かず終了 | 失敗を上書きせず、元PvCの2移動を維持した固定cameraを追加して007へ |
| 007 | Succeeded、errors=0、`da250cbee5514a5db26c6ea39d467d7c` | PID15060、initial 180検算PASS、exit0。PID2900、同datasetの別Player reload 11検算PASS、exit0 | 最終ソースと各Playerの自己identityを照合。旧版の実行結果を移植しない |
| 007・追加解像度 | 上記と同一ビルド | PID31732、1920×1080のpivot FAIL。284検算を記録した段階でPV10の理想1セル許容差超過 | 実Float32端点を記録する厳密投影検査を追加し、理想量の許容差の根拠を独立確認 |
| 008 | UnityコンパイルFAIL、exit1。配布出力なし | Playerは起動していない | 新診断の呼出先を存在しない `Log` と記述した実装誤り。既存 `Record` に修正し、008のログは保存したまま009へ |
| 009 | Succeeded、errors=0、`4dfababe512f44a8864f107ff8bbb497` | 3解像度のpivot、initial／reload、pivot-reload、recovery／recovery-reloadの計8回がPASS、exit0 | 1920×1080 PID32724の実端点期待値と実移動はともに `1.000001577845558`。全phaseを同版で実行し、007の合格を転記していない |

107／124は失敗前に記録された検算数であり、失敗runを部分的な全体PASSとする数字ではない。180／11も独立した受入IDの件数ではない。これらは実Windows Player内の模擬uGUIイベントと明示fixture／API準備を含み、OS実入力の証拠ではない。

証拠は `smoke-initial-1536x1024-32844.json`、`smoke-initial-1536x1024-10280.json`、[build-007 initial](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/smoke-initial-1536x1024-15060.json)、[別Player reload](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/smoke-reload-1536x1024-2900.json)。各launch-recordとPlayer自己identityへの対応は [build-007独立照合](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-build007-20261017-093946-921816.json) に保存されている。

## 独立したrayの検算

独立担当はPlayerを起動せず、失敗ログの画素・カメラと既存形状から別の式でrayを再計算した。build-005で製品が返した `(3,2,1,0)` への進入距離は約0.844732、期待していた `(1,1,0,0)` は約0.965678であり、手前の実候補に先に当たることが一致した。したがって、要求セルの面がcameraに向いているだけでは、他の3D候補に遮られない証拠にならなかった。

固定PvP cameraでは、既知立方体の12面三角形から幾何だけで1点を選び、全P候補と全3駒の半空間を独立に交差判定した。目的地が最前面となることを確認した。PvCの固定cameraも、元の2手それぞれのsource／destinationについて、全駒と候補の交差を検算した。周辺画素探索、候補Pの削減、手前の別セルへの期待値変更は用いていない。解析はdouble精度であり、Unityのfloat mesh・イベント経路の成功は後続のbuild-007実行で別に確認した。

対応記録：

- [失敗rayの独立診断](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-hit-diagnosis-20261017-092929-394939.json)
- [PvP固定cameraの検算](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-fixture-camera-20261017-093203-173684.json)
- [PvC固定cameraの検算](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-pvc-camera-20261017-093605-957157.json)

## 1920×1080のFloat32端点と許容差

build-007のPID31732では、1セル移動を意図した軸ドラッグの実量が `1.000001577845558` セルとなり、旧検査の1e-6セルを超えた。実誤差は約1.578e-6セル、約0.00005049 canvas px、約0.00005325 screen px。独立担当の解析では、画面座標1024〜2048でのFloat32の1 ULP（約0.00012207 px）より小さく、実解像度・canvas除算を含む誤差予算内だった。ただし旧ログに元端点がなかったため、旧失敗のビット単位再現までは主張していない。

今回の検査は、実際に渡したFloat32の開始／終了点をcanvas座標へ変換した差から、画面投影軸へのdouble投影を独立計算する。製品の実量とは1e-9セル未満で一致する必要がある。理想1セルとの比較は1e-5セル（0.00032 canvas px）とし、元の基底不変検査は変更していない。検査を削除せず、量子化済み入力への厳しい検算を追加した。これは宣言した解像度の入力誤差に対する調整で、未試験の4K／8Kまで一般化しない。

build-008は追加した診断呼出しを `Log` と誤記してCS0103となったため、Player出力を作らず終了した。既存 `Record` への修正だけを加えて009を新規ビルドした。009のPID32724の実ログは、開始 `(1102.52014,807.671753)`、終了 `(1134.82056,817.4569)`、scale `1.0546875` を記録し、独立投影期待値と実量がともに `1.000001577845558` であることを確認した。

証拠：[独立誤差予算](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-axis-quantization-20261017-094414-928646.json)、[008コンパイル失敗](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/build-logs/build-008.log)、[009実端点ログ](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/runtime-data/final-pivot-009-1080/events-32724.tsv)、[009の710検算](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/smoke-pivot-1920x1080-32724.json)。

## ビルド・起動ガード

build-007のsourceHashは `fd553512dc2c50e4aa1fe0668dbea2a0066be8c10ff3ff0d5ba086b7f4ae6967`。同版の独立照合は386ソース、172配布ファイル、Unity成功ログと実引数、packed resource内のidentity、Core／Geometry／Runtime／Settingsの実装DLL、PlayerのPID／EXE／保存先／GUIDを結び付けた。初期実行とreloadの実寸法は1536×1024、Player報告DPIは96である。OS倍率の確認とは分ける。

build-009のsourceHashは `ee25fd80a2cba082dc56ffbb45d0327c81caf2fe01a4b8afa015382133205ba4`。`windows/build-009/build-report.json` は成功・errors=0・sourceDriftなしを記録した。[最終Player記録](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/codex-final-player-runs.json) が全8自動実行の自己identity、寸法、PID、exit、ログhashを009へ対応させる。詳細とOS入力の区分は [起動・人間確認手順](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/02_CODEX_WINDOWS_AND_HUMAN_CHECK_JA.md) を参照。

引継いだlauncherのsource集合完全一致・正確な除外・6実引数比較・spawn後cleanup・interactive引渡し直前のpollを維持し、Python子を使った既存19件と追加境界5件を独立に確認した。固定入口は005時点の確認を保持し、009固定後も [verify-only成功と6種類の上書き引数拒否を再確認](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-entry-20261017-095410-453377.json) した。引数なしの固定入口はまだ起動しておらず、人間用の保存先も未作成。別保存先の実PlayerはOSの「終了」で正常終了した。実OSのterminate／kill権限拒否を検証したとの主張もしない。

## 純粋回帰と最終ソース再照合

今回の新規 `tests-final-codex` 実行は、既存runnerの `dotnet run --no-restore` をそのまま使い、5スイートすべてexit0だった。Core36件、Geometry3,372,857 checks、Settings43件、Interaction21,669 assertions、Match30件／337 assertions。既存環境だけを用い、製品・試験ソース・期待値を変更していない。

2026年10月17日09:41:07 JSTに、実行後manifestにあるsrc／tests／scenariosの79ファイルを再hashし、内容不一致0・追加0・削除0を確認した。よって同じ純粋スイートの再実行は不要と判定した。引継ぎmanifest対象72ファイルも一致し、対象外だった既存7シナリオを「今回新規追加」と扱っていない。前回Geometryの保存ソース10件も一致した。他スイートには前回実行単位の独立ソース保存がないため、その範囲を推定で補っていない。

証拠：[純粋回帰概要](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/tests-final-codex/summary.json)、[最終79ファイル再照合](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/final-pure-source-recheck.json)。Geometry生成JSON内には古いRelease実行例の文字列が残るため、実コマンドは `tests-final-codex/results.json` と `execution-record.json` を正とする。純粋試験はGPU・OS入力・外観を認定しない。

build-009生成後の09:44:58 JSTにも [79ファイルを再照合](/C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/final-pure-source-recheck-build009.json) し、不一致・追加・削除はすべて0だった。今回の追加変更はUnity側の検査であり、純粋5スイートの再実行は引き続き不要と判定した。

## 保全と提出時の境界

元46件・他者変更・原本・承認画像の最終保全は受入担当が別途記録する。受領時の46/46一致を最終確認へ自動転記しない。旧build／旧run／失敗ログを削除・上書きしていない。通常Git照会の所有者制限についてcleanを主張せず、Git書込み・safe.directory変更・新規取得・外部APIは行っていない。

本書作成担当の書込みは、変更台帳、操作手順、純粋回帰の新規結果と補助監査スクリプト・JSON・差分だけである。Unity／Playerの実行と製品修正は親担当、独立guard確認と受入保全は各担当の記録へ帰属させる。OSドラッグとShift+Homeは試行したが期待結果を観測できずHUMAN_PENDING、Ctrl系／右・中ドラッグ／実ドラッグ中の取消はNOT_RUN。ツール側・製品側の原因を根拠なく断定せず、人間の外観・操作採否とともに残す。
