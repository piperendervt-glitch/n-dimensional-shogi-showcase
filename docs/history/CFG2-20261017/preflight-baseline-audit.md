# CFG-2 基準照合・既存回帰監査

対象：`C:\dev\n-dimensional-shogi-showcase`。今回出力：`artifacts/CFG2-20261017-004021-JST`。結論は **NO_MAJOR_BASELINE_INCONSISTENCY**。開始時点に重大な保全不一致・他者差分・実行中の対象Unity/Player競合はなく、CFG-2実装を開始できる。これは新CFG-2の機能合格や人間検収を意味しない。

## 契約と原本

今回契約 `C:\Users\pipe_render\.codex\.chatgpt-projects\g-p-6ad04e32b33c8191b44c5dfad644d21c\deliverables\CFG2_UI_MATCH_REQUEST_v0_1_JA\CODEX_CFG2_UI_MATCH_IMPLEMENTATION_REQUEST_JA.md` を全文読んだ。SHA-256：`D843C859C9DFED22B5494FF3EAB5897C93B8CA71D2E85516B4F76EE9F3A5A7E1`。

CFG-1原本runの00～05報告を全文読み、final-source-audit／preflight-audit／acceptance-finalの全文JSONをパースした。旧196 IDは重複なく保持され、C1は18件。原本のFinalStatus、Evidence、Proven、ScopeGapを最終評価として読む。古いNote／EvidenceThisRunの中間記述を最終証拠と取り違えない。参照ファイル・hash・全ID索引は [cfg1-reference-index.json](cfg1-reference-index.json)。旧run・旧受入・過去試験・EXEを変更していない。

旧「CFG-2へ進まない」「未適用」「7カテゴリ」は今回契約の該当範囲で更新する。§5のN=4、−4..4、共通対称4D、40駒、王捕獲・手なし引分・時間切れ、成り打ちなし、10分時計、小CPUは今回のデモ限定案。正式ルール、人間検収、N=2互換、強いAIの採用を意味しない。

## 基準状態・保全

[preservation-before.json](preservation-before.json) に実名とSHA-256、Git読取状態を保存した。

| 項目 | 実測 |
|---|---|
| HEAD | 14d56f80d2e9758f550de5bb11e152960cb9fc6e |
| branch | claude/project-thread-5gmozz |
| origin | https://github.com/piperendervt-glitch/n-dimensional-shogi-showcase.git |
| CFG-1ソース | 43/43 hash一致。既存tracked変更2＋新規所有41 |
| 歴史的保護対象 | 46/46 hash一致。画像42＋player.log 2＋process-memory.jsonl 2 |
| 未追跡 | 87=CFG-1所有41＋保護46。開始時のその他未追跡0 |
| staged | 0 |
| tracked差分 | CFG-1既知のSectionView.csとProjectSettings.assetのみ |
| AGENTS.md | C:\、C:\dev、対象ルート、および生成物を除く対象内の再帰探索で該当なし |

CFG-1所有43件は今回の変更可能な土台、保護46件は編集不可。HEADだけで開始コードを同定せず、開始時43件hashと差分を併用する。監査完了・4試験完了を親へ通知してから他担当が実装を開始する。以後の所有担当の変更を開始時の他者変更と混同しない。

## 環境と並行プロセス

[preflight-audit.json](preflight-audit.json)、[preflight-environment-processes.json](preflight-environment-processes.json) に実測を保存した。

- Unity 6000.3.11f1（ProjectVersion revision 3000ef702840）、既存Unity.exe、WindowsStandaloneSupportあり。
- .NET SDK 9.0.312。既存runtimeを利用し、新規取得・更新なし。
- lockの28依存すべて、既存ローカルpackage.jsonの名前・版・CFG-1 hashと一致。HDRP/Core/ShaderGraph17.3.0、uGUI2.0.0を含む。
- UnityLockfileなし。対象Unity／Player／dotnet／MSBuildの実行競合なし。Codex自身の補助プロセスは別として記録。
- 通常CIM照会はアクセス不足だったため正規のrequire_escalated経路で読み取り成功。プロセス停止、権限設定や防御設定の変更なし。

依存ファイルの存在はCFG-2のimport/build成功の証明ではない。Unity起動は本監査では行っていない。前回の -noUpm 失敗を再利用方針にせず、必要なら前回のローカルcache／process限定proxy根拠を参照し、外部取得禁止を維持する。

## 今回再実行した基準4試験

すべて `dotnet run --no-restore --project tests/Showcase.<系統>.Tests/Showcase.<系統>.Tests.csproj --configuration Release -- <今回run>/baseline-<系統>`。実際の絶対パス・引数・開始終了時刻・exitは各execution.jsonに保存した。過去PASSの転記ではない。

| 系統 | 結果 | 実行全体秒 | 証拠 |
|---|---|---:|---|
| Core | 36 PASS、0 FAIL、exit0 | 3.5723608 | [execution](baseline-core/execution.json)、[tests](baseline-core/tests.json) |
| Geometry | 3,372,857 assertions、TECHNICAL_PASS、exit0 | 2.1690560 | [execution](baseline-geometry/execution.json)、[report](baseline-geometry/geometry-test-report.json) |
| Settings | 25 PASS、0 FAIL、exit0 | 1.5955948 | [execution](baseline-settings/execution.json)、[tests](baseline-settings/tests.json) |
| Interaction | 13,746 assertions、PASS、exit0 | 1.3369461 | [execution](baseline-interaction/execution.json)、[report](baseline-interaction/interaction-results.json) |

[baseline-source-before.json](baseline-source-before.json) と [baseline-tests-summary.json](baseline-tests-summary.json) でpure C#の23ファイルが試験前後hash一致。試験本体・数理期待値・ソースを変更せず、新しい出力だけを作った。Geometry原本report内のhistorical command文字列は原本の固定記述であり、今回の実コマンドはexecution.jsonを正とする。試験はpure C#であり、新対局・新UI・Windows入力の合格ではない。

## 実symbolとの接続と工程差分

| 実在箇所 | 基準照合 | 今回必要な接続 |
|---|---|---|
| SettingsModel.cs：SettingsDraft／PreviewPreset、CanApply=false | 一致 | 検証済み次局設定と開始snapshot。実接続前に偽の適用成功へしない |
| PreviewSession.cs：PreviewSession／PreviewQuery／Attackers | 一致 | 全4D G/Pと利きを対局・CPU・表示へ共有。利きUIはCFG-1未接続 |
| SettingsStore.cs：SettingsStore／ViewSnapshot | 一致 | 正常設定・不正Draft・安定viewを保全し、今回runで自動保存／別process復元 |
| Interaction：SliceAnimator／InputOwnership／AxisDrag | 一致 | 既存4D実断面・有限窓・所有・古いhit拒否を回帰保護 |
| Cfg1App／Cfg1Smoke／Cfg1Build.BuildWindows | 一致 | 3モード・6カテゴリ・簡素UIへ変更／追加。旧buildはsceneと保存先も生成するため旧runを起動引数に使わない |
| RuleCore.InitialState、旧PawnFirst4D／Tactical3D | 一致 | 40駒の配置だけ新モデルへ明示変換。旧非対称能力・演出CPU・27手ガードを新対局へ持ち込まない |

これらは契約内で修正可能な不足で、重大な基準不一致ではない。新C2-01～20のPASSは、各新機能と今回Windows実行の証拠が得られるまで付けない。物理入力経路の可否は今回のツール状態で別に確認し、過去BLOCKEDをそのまま現在の障害とも、解消済みとも扱わない。

監査担当はこの時点までソース編集・Git書込み・外部取得・別repo変更・旧run変更なし。実装担当の次作業と並行できる基準証跡を保存した。