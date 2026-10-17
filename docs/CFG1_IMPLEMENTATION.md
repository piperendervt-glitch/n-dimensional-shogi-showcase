# CFG-1A＋CFG-1B／P1 Windows試作

契約は2026-10-16にユーザーが送信した `4DS-CODEX-CFG1-IMPLEMENTATION v0.1`。設定変更→共通G/P照会→独立試し盤→実4D切断→保存・再読込を接続した。製品ルールの採用、対局3モード、汎用CPU、完全合法性L、成り・打ち、CFG-2は対象外。保存しても未適用のままで、適用ボタンは未接続理由を返す。

## 起動と保存

Unity 6000.3.11f1、既存HDRP 17.3.0、uGUI 2.0.0を使用する。新しい入口は `Unity/Assets/Scenes/Cfg1.unity`。旧 `Showcase.unity` と `ShowcaseController` は保持し、新入口には自動対局を置かない。

Windowsビルドは `Cfg1Build.BuildWindows`。`--cfg1-output <新しいexeパス> --cfg1-run-root <今回runの実在ディレクトリ>` が必要で、既存EXEへの上書きは拒否する。生成リソース `cfg1-run.json` が通常起動時の保存先を与える。Player側でも `--cfg1-run-root` を指定できる。保存と実行イベントはその下の `runtime-data` に限定する。

日本語はWindowsにある Yu Gothic UI／Yu Gothic／Meiryo／MS Gothic の順で動的フォントを選ぶ。フォントファイルを複製・配布していない。別PCでのフォント存在と配布可否は別途確認が必要。

## 状態と共通照会

- `SettingsDraft` は不正な生入力も保持する。編集履歴、Revision、セッションIDを持ち、標準の試験原本を変更しない。
- `PreviewPreset` は検証済みの読み取り専用定義。step/1、h11/1、h22/1を `PreviewQuery` が解釈する。Unity側は移動集合を再実装しない。
- `PreviewSession` は座標をキーにした疎な独立配置。配置・移動・捕獲・削除・クリア・Undo/Redoは本番局面やLegacyへ反映しない。
- Gは盤内の幾何候補、Pは占有・遮蔽・捕獲を含む候補。Lは常に未評価。理論数、全4D、表示窓内のセル数を分ける。
- 非同期照会は `QueryStamp` と `LatestQueryGate` で古いセッション／Draft／試し盤／選択／要求を拒否する。不正入力になった時は以前の結果を消す。
- 格子直線 `lattice-line/1` と閉Supercover `supercover-closed/1` は既存の Core 実装を再利用する。新規境界方式を追加していない。

試験設定はN=4、各軸−4〜4、前方Core F、Aは+F／Bは−F。UIはX=U、Y=V、Z=F、W=W。旧BのW=0制約は共通試し盤に持ち込まない。元の0〜4 fixtureとUIの中心対称fixtureは区別する。80／32／8は試験条件での候補数で、製品ルールではない。

## 4D操作の限定選択

`SliceAnimator`、`FiniteSliceWindow`、`HyperplaneRelation`、`InputOwnership`、`AxisDrag` はGeometryの新規サブ名前空間にある。既存切断器・Legacyの角度関数は変更しない。

| 主軸 | 基準ビュー | Core順の基底3本；法線 |
|---|---|---|
| W | XYZ | F,U,V；W |
| Z | XYW | W,U,V；−F |
| Y | XZW | F,U,W；−V |
| X | YZW | F,W,V；−U |

基準間はXYZ経由の90度区間、各0.45秒smoothstep。最新の別要求を1件保留し、同一目標再選択は無操作。中間姿勢は基準名を偽表示せず、共通フレームでセル・木駒の実断面を作る。W復帰は方向だけを戻す。

有限窓は各表示軸半幅2.5セル。空中描画中心はUnity座標(0,1.1,0)、木製盤とは独立。6半空間を追加して既存切断器で窓を切る。接線移動は窓と原点、法線移動は切断超平面を動かし、論理駒は動かさない。セルの三角形対角線を除き、共有稜線を重複除去する描画処理は論理幾何を変えない。

4小窓は現在の超平面Hと、それぞれ中心cを通る基準3D空間の実交差を3D投影したもの。一致時は空間の箱、非一致時は交差多角形を示す。駒の2D投影図ではない。

小窓の軸は世界4D軸に固定。32画面px/セル、投影長6px以下の退化軸は拒否する。入力開始面が所有を保持し、フォーカス喪失は未確定ドラッグを戻す。右ドラッグは3Dカメラ回転、中はパン、ホイールはズーム。古いView／Draft／試し盤／カメラRevisionのpickを拒否する。

これらの値・経路は `IMPLEMENTATION_CHOICE` で、人間の操作感採用待ち。任意角度の直接入力は後段。保存は回転が完了した4基準姿勢で行い、中間姿勢の保存・未対応姿勢の読込は理由付きで拒否する。

## 保存境界

`SettingsStore` はXMLのDraftとPreviewPresetを区別し、schema・属性・要素・参照・数値を検査する。DTD、未知機能、コード実行、ネットアクセスは許さない。同一ボリュームの一時ファイル→flush→replaceで旧版を保全し、失敗注入試験を持つ。別名保存は新しいIDを発行し、既存パスは拒否する。

保存には独立の試し配置と表示状態を同梱する。不正入力はDraft専用。UIで扱えない空定義・重複ID・未知参照・カメラ範囲／姿勢を読込する前に検査し、現在の編集と元ファイルを保つ。AppliedSetup／MatchSnapshotの成功を作らない。

## 検証と証跡

pure C#試験は既存Core36件、Geometry3,372,857 assertions、新Settings25件、新Interaction13,746 assertions。`dotnet run --no-restore --project <csproj> -- <専用出力>` を使用し、実コマンドと結果は今回runに保存する。

Windows Playerの `--cfg1-smoke --cfg1-smoke-phase initial` は実UIハンドラへ模擬uGUIイベントを送り、編集・実pick・移動・捕獲・入力所有・回転・保存を確認する。同じrunで `--cfg1-smoke-phase reload` を別プロセス起動すると保存復元を確認する。`images` ディレクトリを起動前に作る必要がある。これらはOSの物理マウス／キー／IME入力を証明しない。

今回run：`D:\Users\pipe_render\Downloads\n-dimensional-shogi-showcase\FOUR_DIMENSIONAL_SHOGI_CODEX_HANDOFF_v0_1_JA\output\CFG1-20261016-201633-JST`。最終成果物・実行結果・全受入ID・未実施・人間確認手順は同フォルダーの00〜05報告と証拠台帳を参照する。識別はBASE_HEAD＋差分／新規ファイルSHA-256であり、HEAD単体ではない。
