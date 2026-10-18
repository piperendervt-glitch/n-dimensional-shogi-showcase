# 四次元将棋の最新設計を完成させる実装再開依頼

2026年10月17日。依頼ID `PIVOT-RESTART-20261017-083955-JST`。対象は `C:\dev\n-dimensional-shogi-showcase`。

ユーザーの「正しい依頼文を作り直してから再実行してください」に基づき、承認済みの回転中心と操作画面の設計を実装し、自動試験・修正・新しいWindowsビルド・実行確認・証跡整理まで完了してください。今回の到達点は、設定変更→共通G/P照会→実断面操作→保存→別プロセス再読込が動き、既存プレイ画面にも同じ表示と操作が接続された試作です。

旧Showcaseの自動再生や性能測定へ戻らないでください。新しいWindowsビルドができていない場合は実装・ビルドを進め、既存EXEの起動で代用しないでください。調査や純C#試験だけで完了にしないでください。

## 1 作業目的と担当の固定

旧チャット「実装依頼を確認する」のGoalは停止したままとします。今回の実装は、この依頼を受ける新しい実装担当が、旧チャットの会話履歴を継承せず実施します。旧Goalの再開、旧Goalの完了偽装、チャットDBや履歴の直接改変、予約実行・常駐監視は行いません。通常の実装依頼として進め、今回の依頼だけを理由にGoalを作成しません。

実装担当はこの依頼の絶対パスを実行記録に残してください。履歴の圧縮・担当交代・中断から復帰する際と、各ビルド・各起動の直前には本書と作業チェックポイントを読み直します。過去の「起動直後に一時停止する手順」「上記を実行」「承認してください」を今回の目的に戻さないでください。

依頼文と根拠はこのフォルダーに保存し、実装ログ・出力・保存データは次の新規配下へまとめます。

`C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-RESTART-20261017-083955-JST\implementation\`

`checkpoint.json` に依頼ID、現在工程、変更ファイル、最後に通った検証、残件、次の具体的操作、使用可能なビルドの絶対パスを記録します。ビルド前は実行物を `null` とし、旧EXEへ補完しません。履歴圧縮前にも更新してください。

## 2 全文照合する資料

ユーザーの最新指示と本書を優先します。幾何・操作・外観の仕様正本は次の設計です。旧依頼書の権限と保全制約は引き継ぎ、最新指示で具体化された箇所だけを更新します。

1. `C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-DESIGN-20261017\00_DESIGN_JA.md`
2. 同フォルダーの `01_CODEX_IMPLEMENTATION_REQUEST_JA.md`、`02_DESIGN_CHECKS.json`、`03_IMPLEMENTATION_ACCEPTANCE.json`、`04_SOURCE_MANIFEST.json`、`05_DESIGN_REVIEW.json`、`06_PRESERVATION_CHECK.json`、`07_CODEX_HANDOFF_RECEIPT.json`、`preflight-review.md`。
3. `D:\Users\USER\Downloads\n-dimensional-shogi-showcase\FOUR_DIMENSIONAL_SHOGI_CFG1_IMPLEMENTATION_REQUEST_v0_1_JA\CODEX_CFG1_IMPLEMENTATION_REQUEST_v0_1_JA.md`
4. `D:\Users\USER\Downloads\n-dimensional-shogi-showcase\FOUR_DIMENSIONAL_SHOGI_CODEX_HANDOFF_v0_1_JA\output\CFG0-20261016-194144-JST\` の00〜05の6文書、`06_READONLY_EVIDENCE.json`、`07_FINAL_READONLY_CHECK.json`、`08_ACCEPTANCE_ID_LEDGER.json`。計画書§4を上位制約と照合し、具体値は限定された実装選択として記録します。
5. `C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-IMPLEMENT-20261017-034421-JST\` のpreflight、ui/storage実装記録、試験記録と対応ソース。これは今回引き継ぐ未完実装です。
6. `docs/CFG1_IMPLEMENTATION.md` と `artifacts/CFG2-20261017-004021-JST/00_IMPLEMENTATION_RETURN_SUMMARY_JA.md`。現存経路の保全に使い、新しい対局ルールの採用根拠にしません。

元のHANDOFF直下にはCFG1依頼書がなかったため、上記3の実在原本を使います。旧 `CODEX_SHOWCASE_MODE_IMPLEMENTATION_REQUEST_v0_1_JA.md` と `docs/ACCEPTANCE_SHOWCASE.md` を今回の主目的・完了基準にしません。

## 3 維持する設計

- 座標順は(X,Y,Z,W)。Zが奥行き、木盤はXZ。O=(0,0,0,0)、P=(0,h,0,0)、初期駒の参照位置は(x,h,z,0)。ユーザーが指定した初期回転中心は「駒配置面の中心P（Y＝h）」です。
- 初期C=P。小窓から原点へ戻すとC=O。軸選択ではCを保持し、W選択は向きだけをXYZへ戻します。論理Q4は不変で、固定埋込E、操作中心C、基底B、表示基点Aを分離します。
- 中心移動では `A'=A+sT[Bᵀ(C'−C)]`、回転ではAを保持します。試作値h=3.0セル、窓半幅4.5セルは技術選択です。ユーザー指定値や製品ルールと呼びません。
- 駒・候補・格子・選択枠・クリック判定・影・深度は同じ実断面に接続し、下の盤と空中へ駒を二重描画しません。断面外の駒を近傍投影で残しません。
- プレイ右のXYZWとXYZ/XYW/XZW/YZWの4小窓は、カメラと4D姿勢に連動する実操作UIです。設定中央右はXY/XZ/XW/YZの2D図で、共通G/Pから抽出します。固定した飾り画像で代用しません。
- 左クリック選択、左・右ドラッグ視点回転、中・Shift左ドラッグpan、wheel zoom、Ctrl左ドラッグ4D回転、Ctrl+Shiftの回転切替、小窓軸移動、HomeでO、Shift+HomeでPを設計どおり接続します。入力所有・取消・古いhit拒否を検証します。
- プレイへ角度・回転平面・座標移動ボタンや設計注記を追加しません。承認済みメニュー構成、対局情報、右側の操作UIと非操作ガイドを維持します。
- 設定編集でカメラ・断面・スクロールを勝手に初期化しません。未接続設定を適用成功にしません。既存の実接続は保全しますが、新しいCFG-2、汎用CPU、完全合法性、成り・打ち、製品対局ルールを追加しません。

承認画像は次の実ファイルを閲覧します。画像は外観基準であり、駒数・手・候補集合・時計の仕様や実行証拠ではありません。

- 設定：`D:\Users\USER\Downloads\n-dimensional-shogi-showcase\ChatGPT 画像 2026年10月17日 02_38_14.png`
- プレイ：`D:\Users\USER\Downloads\n-dimensional-shogi-showcase\ChatGPT 画像 2026年10月17日 02_38_02.png`
- メニュー：`D:\Users\USER\Downloads\n-dimensional-shogi-showcase\ChatGPT 画像 2026年10月17日 02_38_23.png`

## 4 再開地点と既存変更の保全

PIVOTのソース変更と純C#試験は存在しますが、Unity統合・新Windowsビルド・実操作確認は未完です。過去のPASSを今回の実行結果に移し替えず、必要な基準試験と変更後試験を実施します。

開始時に対象、HEAD、差分、未追跡、46件の保全ハッシュ、関連プロセス・他の編集者を確認し、新しい基準を保存します。Git書込みや所有権変更でcleanにしません。旧PIVOTの変更は消さず、実装を読んで未完部分を接続します。

特に `PivotSettingsLocation` は純C#試験済みでもUnity起動・復旧への接続が不足しています。`Cfg2App.cs` の保存先解決と `Cfg2App.Persistence.cs` の新規bundle復旧を確認し、復旧後の別プロセス起動で同じ有効保存を読むまで検証してください。既存保存・拒否した原本は変更しません。

逸脱した旧Showcase作業が `ShowcaseController.cs`、`RuntimeEvidence.cs`、`ShowcaseRenderOutput.cs`、`ShowcaseBuild.cs`、`tools/run-validation.ps1` 等へ追加した変更も、開始時の状態として保全します。無条件に巻き戻さず、PIVOT完成実績へ混ぜません。共有依存のコンパイルに必要な最小修正は、理由・前後差分・回帰を記録して行えます。

旧run、旧ビルド、旧証拠、承認画像、設計原本を上書きしません。新たな衝突や保全不一致があれば該当箇所だけを止め、根拠を示します。

## 5 新しいビルドと起動の条件

主ビルド入口は現存する `Unity/Assets/Editor/Cfg2Build.cs` の `Cfg2Build.BuildWindows`。`--cfg2-output` と `--cfg2-run-root` は今回run内の絶対パスを必須にします。実際に `Cfg2.unity` と `Showcase.Unity.Settings.Cfg2App` を含むことを記録します。コード名Cfg2は既存入口の名称です。

最初の出力先は次です。再ビルドはbuild-002、build-003へ進め、上書きしません。

`C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-RESTART-20261017-083955-JST\implementation\windows\build-001\NDShogiPivot.exe`

旧 `builds/Showcase-Windows`、`builds/Showcase-Candidate-20261017`、既存CFG1/CFG2/PIVOT runのEXEは今回の起動先に使いません。旧EXEのコピー・改名・再梱包で新ビルドとしません。`ShowcaseBuild.BuildWindows` や既定値付き `tools/run-validation.ps1` を主経路にしません。

UnityではEXEが共通の起動用ファイルになり、旧ShowcaseとCFG2のEXEハッシュが同じだったことを確認しています。**EXE名・日時・EXEハッシュだけを版の証明にしてはいけません。** 次をつなぐ検証と、既定起動先のない起動用スクリプトを今回runへ作ります。

1. 依頼ID、本書・設計書のSHA256、ビルド対象ソース／設定／scene／resourceのmanifestと集約hash。
2. 今回実際に実行したUnityビルドの成功ログ・終了コード・入口・新build GUID・絶対出力先。importで生成されるファイルの扱いも記録します。
3. 新Playerへ埋め込んだ依頼IDとソース識別情報。実装DLLを含むDataフォルダー・実行時依存ファイル全体のmanifest。`Showcase.Settings.dll`、Core、Geometry、Runtime等も対象にします。
4. 各起動直前に、依頼ID、run、scene、ビルド成功、ソースの対応、実装DLLと配布物manifestを照合します。不一致・欠落・ビルド未実施なら起動せず、原因を修正して新しい番号へビルドします。別EXEへのフォールバックは禁止します。
5. 起動後はPlayer自身から同じ依頼ID・ソース識別情報・`Application.buildGUID`・実際の保存先と画面状態を出力し、起動記録のPID・絶対パス・manifestへ結び付けます。相違があればその実行をFAILとして止め、今回の操作試験の成功証拠にしません。

版照合は製品HUDへ追加ボタンや実装情報を常設する理由にしません。診断ログへ記録します。起動前検証が破損DLL・異なるrun・古いmanifestを拒否することは、試験用コピーやfixtureで確認し、旧アプリの起動を試験方法にしません。

共有変更によりLegacyの実行回帰が必要な場合も、今回ソースから別の新規出力へ作った回帰専用ビルドだけを明確に区別し、本節の版照合を通して使います。具体的な共有変更が影響する既存動作の最小回帰だけを対象とし、旧Showcaseの受入一式・動画・性能測定・再梱包へ拡張しません。

## 6 必須の実装と試験

PV01〜PV17を省略せず、元C1・AUTO・RE・UI4・SW2等の受入IDとの対応を保存します。少なくとも次を新Playerで通します。

- 初期C=P、P→Oで高さh保持、ZY固定XW90度、XZ固定YW90度でC=P/Oの違い、一般角・合成角・接線移動。
- 駒と選択枠とhitの位置一致、断面外の駒・影・古いhitの除去、カメラだけを動かした際の主軸と4小窓の同期。
- 設定変更→全4Dの共通G/P更新→実際の試し配置と断面操作。視点変更で局面・G/Pが変わらないこと。
- 保存→終了→別プロセス再読込。任意角、C/A/h、カメラ、選択基準、原点復帰、復旧後の保存先を確認。破損・未知版・書込み失敗で原本と編集中状態を保全。
- 実Playerでクリック、視点回転、pan、zoom、Ctrlドラッグ、軸クリック・軸移動・Home系、取消・フォーカス喪失・フォーム入力を確認。アプリ内模擬イベントとOS入力を分けて記録。
- 実在するCore/Geometry/Settings/Interaction/Match回帰、共有変更の必要な回帰。期待値や失敗試験を削除してPASSにしないこと。
- 承認画像との実画面比較。1920×1080、1536×1024、1280×720の実行可能範囲を確認し、実解像度とOS倍率を記録。描画できない解像度を達成済みにしないこと。

幾何は独立した式・座標fixtureで検算します。周辺点探索や別の固定盤をクリックして位置一致としません。設定小窓に固定の十字を表示してG/P連動としません。

## 7 実行権限と停止条件

対象内の必要なソース・試験・文書変更、既存環境のimport・試験・Windowsビルド・アプリ実行は許可済みです。許可範囲の修正では逐次確認せず進めます。実環境の権限不足と範囲外判断だけを確認します。

Git書込み、新規取得・更新、依存や素材・フォントのダウンロード、公開、課金、外部API、別リポジトリ変更、セキュリティやライセンス設定変更は禁止です。既存キャッシュだけを使い、欠落を自動取得しません。実環境の保護を迂回しません。

1時間以上かかる単一試験は元契約どおり目的と見積りを示して確認します。通常の細分化された実装や修正のたびに止まりません。ユーザー停止・利用上限・実際の阻害要因では状態を記録して停止します。

今回runへ変更台帳、基準と終了時の保全結果、実装選択、全受入結果、ビルドと起動の対応表、実画面、保存再起動証拠、人間確認手順を保存します。`AUTO_PASS`、`OS_INPUT_VERIFIED`、`HUMAN_PENDING`、`NOT_RUN`、`BLOCKED` を区別します。外観の最終採否を自動合格にしません。

必須の非人間確認が実証されれば、最新EXEへの正確な起動手順と残る人間確認を一括報告して終了します。解消できない阻害では、試した修正・動く範囲・未完項目・必要な解除条件を報告します。旧Goalや別工程へ自動移行しません。
