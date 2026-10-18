# PIVOT実装の安全な引継ぎ地点

2026年10月17日。依頼ID: `PIVOT-RESTART-20261017-083955-JST`。

ユーザーの最新指示「いちいち許可を求められるので時間がかかり現実的でない。Codexに実装を依頼してください」により、この担当は製品編集・新規ビルド・新規Player起動を停止し、引継ぎ記録だけを更新した。**製品完成ではない。現在ソースと一致して起動可能と判定したビルドはない。** 旧Goalは再開していない。

作業対象は `C:\dev\n-dimensional-shogi-showcase` のみ。今回runは `C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-RESTART-20261017-083955-JST\implementation`。以下の相対パスはこのrun基準。

## 最初に読むもの

1. `../00_RESTART_REQUEST_JA.md` 全文。SHA256 `47ceb3a8710eccfe37329c9cd1478f60ca6fa2bb562a731c540e05f27e2a1a4b`。各ビルド・起動前、担当交代・圧縮後に本書と `checkpoint.json` を読み直す。
2. 原依頼が列挙するPIVOT設計・原CFG1契約・CFG0 00〜08。親の `../02_ACCEPTANCE_REVIEW_JA.md`、`../03_IMPLEMENTATION_DECISIONS_JA.md`、`../04_INDEPENDENT_REVIEW.json`、`../05_ACCEPTANCE_LINK_INDEX.json` も読む。関連IDは全件合格の意味ではない。
3. `handoff/source-change-ledger.json`、`handoff/source-diff.patch`、`handoff/build004-current-source-check.json`。

設計は O=(0,0,0,0)、P=(0,h,0,0)、初期C=P、固定埋込E、C/BとAを分離、C移動時だけA輸送。h=3と窓半幅4.5は技術値。Core F/U/V/W は表示 Z/X/Y/W に対応。設定小窓はXY/XZ/XW/YZの2D図。プレイは実カメラ連動ギズモと4小窓。新対局ルールを採用する依頼ではない。

## 実装済みの範囲

既存未完PIVOTの幾何・表示・保存を読んで、次を接続／修正した。すべて基準状態との差分を保全している。

- `Cfg2Build.cs` と `Cfg2App.Identity.cs`、run内build/launchスクリプトで、依頼hash・設計hash・全対象source manifest・Cfg2 scene/app・実ビルド実引数・GUID・実装DLLを含む配布全体・Player自身のPID/EXE/保存先を結び付けた。既定EXEも旧版fallbackもない。
- Unityの起動保存先を `PivotSettingsLocation` の選択先へ接続した。新bundle復旧は現在のdraft/options/trial/viewを保存し、成功後だけ選択先へ切替。破損原本と編集中の状態を保持する。別プロセス純C#検証は実行済み、Playerの復旧経路は未実行。
- UI入力所有をButton wrapper/フォーム/画面切替へ接続し、切替前に取消。rollbackが可変の現在sliceでなく開始時のanimatorを戻すよう修正。
- 設定画面でも既存XYZWギズモ軸のドラッグでCを移動し、クリックは基準断面を選ぶ。Home/Shift+Homeは設定プレビュー／ギズモでも利用可能。常設ボタン追加なし。親の技術選択D01参照。
- 通常40駒のXYW断面が正常に空になるfixtureを独立に確認し、空mesh/pick/shadow消去を検証。クリックは未選択状態からsubjectへ変更し、実CellPick処理到達も確認。選択枠と既知三角形hitの検算を追加。
- 画面の「全駒Y=0」を表示座標Y=h／初期中心Pの説明へ更新。入力ヘルプも実操作へ更新。
- 半透明パネルにUnity Outlineが面状に重なり茶色になる問題を、CFG2専用の実周囲線に変更して修正。試験配置ラベルとW軸ラベルの折返しを修正。settings主表示の初期倍率、2D小窓padding/青候補のコントラストを調整。保存カメラは尊重。背景側へ同じ断面が重複する問題をカメラレイヤー分離で修正。

## 最後のビルドから先に進んだ未ビルド変更

`handoff/build004-current-source-check.json` の実hash比較で、以下3ファイルがbuild004と不一致。**この3件を含むUnityコンパイル／Player試験はまだ行っていない。** 次は新番号 `windows/build-005`。build004の再使用、書換え、直接起動は禁止。

| ファイル | 最新の変更 | 残る確認 |
|---|---|---|
| `Unity/Assets/Editor/Cfg2Build.cs` | source除外を全階層同名判定から正確な生成 `Unity/Assets/Resources/pivot-build.json` と `.meta` の2ファイルだけへ限定 | Unityで再ビルド、source集合とhashの確認 |
| `Unity/Assets/Showcase/Settings/Cfg2App.PivotSmoke.cs` | 選択枠の検査対象を全格子線から実際の金色選択線だけへ限定 | 新Playerで全基準・45°・一般姿勢の実選択枠検査 |
| `Unity/Assets/Showcase/Settings/Cfg2ViewGraphics.cs` | XYZ/W軸の線幅2.4、黒縁4.4、矢先8×4、ラベル背景alpha .94へ調整 | 実画面で明るい背景に埋もれないか確認。方向／hit位置は変更していない |

run内 `launch_player.py` も最終レビュー後に補強済み。sourceパス集合の完全比較、上記の正確な除外、実Unity引数6種の一致、phase制約、配布manifestの直下だけ除外、Player識別JSONの短い読込retry、spawn後全体の例外cleanup、interactive引渡し直前の生存pollを含む。Python故障fixtureは19/19 PASS。最新launcherで実Playerを起動した最終確認は未実行。

## ビルドと実行証拠

| 出力 | 実Unity build | Player結果 |
|---|---|---|
| build-001 | 成功、exit0 | 起動なし。後続修正でsource不一致 |
| build-002 | 成功、GUID `e51417fd334e43fd929867f569a817a9` | PID29720、模擬入力smoke FAIL、exit1。試験配置/Wラベルの折返し。失敗証拠保持 |
| build-003 | 成功、GUID `2fbdba74350841feb2fcb443f05ebb40` | PID30964、模擬入力smoke FAIL、exit1。PV05が再計算meshの頂点順を固定して比較していた。双方向の頂点集合距離検査へ修正し、期待値を削除せず後続で通過 |
| build-004 | 成功、GUID `bb4e260369d549949050c95066f7c4d6` | pivot PID29804: PASS、698 assertions、exit0。別プロセスpivot-reload PID31672: PASS、exit0 |

build004 sourceHash: `7fe0353737b8ccd740409854799c67ac85d3c3dd94a84f412c709669a9302be1`。

build004の実行は1536×1024、Player報告DPI96、Unity6000.3.11f1、RTX5060Ti。698は頂点ごとの比較を含むassertion数であり、698独立試験件数ではない。模擬uGUIイベント、明示fixture/API準備を使った実Windows Player試験であり、物理OS入力の成功とは呼ばない。

- `build-logs/build-004-process.json`、`build-logs/build-004.log`
- `windows/build-004/build-report.json`、`source-manifest.json`、`embedded-identity.json`、`distribution-manifest.json`
- `launches/20261017-090552-144977-pivot/launch-record.json`、同 `player-identity.json` と `player.log`
- `launches/20261017-090904-672074-pivot-reload/launch-record.json`、同 `player-identity.json` と `player.log`
- `smoke-pivot-1536x1024-29804.json`、`smoke-pivot-reload-1536x1024-31672.json`
- `screenshots/pivot-1536x1024-29804/approved-menu.png`、`approved-settings.png`、`approved-play.png` と同フォルダーの各状態dump

`approved-*` は承認画像との比較対象という既存smoke命名で、実画面が人間承認済みという意味ではない。親はbuild004の3画面を見て、茶色化・文字クリップ・背景への格子重複の改善を確認した。最終外観はHUMAN_PENDING。承認画像の城庭背景等と既存資産の景観が同一でない点を隠さない。

## 最後に完了した純粋試験

`tests-after-final/results.json` の全5コマンドがexit0。`handoff/test-summary.json` に集約。

| スイート | 結果 |
|---|---|
| Core | 36 PASS |
| Geometry | TECHNICAL_PASS、3,372,857 checks |
| Settings | 43 PASS、別プロセスと保存故障fixtureを含む |
| Interaction | PASS、21,669 assertions |
| Match | 30 PASS、337 assertions |
| launcher追加故障fixture | 19/19 AUTO_PASS |

`guard-failure-test-summary.json` と `launcher-guard-tests/20261017-091139-773102/results.json` は実引数取り違え、source追加、別run、不正phase、Python実子のtimeout/識別例外/KeyboardInterrupt/識別期限切れcleanupを検証。子PID28748/29500/8184/33772は終了・reap確認済み。Unity/Playerは使っていない。長時間timeoutは注入／短縮、OS terminate失敗とkill fallbackは未検証。最初のfixture結果15PASS/4FAILは結果辞書のstatusキー重複による試験側集約不具合で、修正後全再実行、初回結果も保持している。

## 独立レビューの残件

親の `../04_INDEPENDENT_REVIEW.json` 原文は変更しない。次の現状は自己申告の実装状況であり、独立レビューの最終closeではない。

- IR03: 未選択→実subject選択と既知三角形hitはbuild004で通過。しかし選択枠が格子全線を対象にしていた弱点は最新ソースで色限定に補強しただけで未ビルド。**新Player再検証必須。**
- IR05: build前後／launch時のsourceパス集合追加検出は実装済み。正確な生成2ファイルだけの除外は最新builder未ビルド。launcher fixtureは別path同名JSON/metaを含めPASS。**build005実行で確認必須。**
- IR06: launcher spawn後全体cleanupと終了確認、build側cleanup、interactive引渡し生存pollを実装済み。実Python子4条件はPASS。実Playerでの最終guard確認、kill fallback異常条件は未検証。
- IR01/02/04/07〜11は実装に反映済み。IR04のsettingsギズモ移動/Homeはbuild004の模擬入力で確認済み。実OS入力と最終独立レビューは未完。

## 次担当が行う残作業

1. 最新3ファイルをレビューし、既存 `build_player.py 5` で実Windows build005を新規生成。request/checkpointを読み、既存環境の権限経路を守る。ソースに変更があれば次番号へ進む。
2. `test_launcher.py build-005`、必要なguard fixtureの最新照合後、必須引数を明示して `launch_player.py` から実行。pivot と同じdatasetで別プロセス pivot-reload を通す。
3. 既存の `--phase initial` と同じdatasetの `--phase reload` を実行して既存CFG2接続回帰を確認。今回新規Playerのinitialはまだ開始していない。新しい対局ルール／旧Showcase性能試験への拡張は禁止。
4. 新dataset内だけで `pivot-next-match.xml` の破損fixtureを作り、`pivot-recovery` と `pivot-recovery-reload` を別プロセスで実行。`Cfg2App.PivotIntegrationSmoke.cs` に経路あり。原本hash保全を記録。**Player復旧はNOT_RUN**。
5. 1920×1080と1280×720の可能な実解像度で3画面を撮影。最新ギズモ調整を親／人間が実画面で確認。実際に描画できた寸法とDPIだけを記録する。
6. OS入力確認を実施。利用可能skillは `C:\Users\USER\.codex\plugins\cache\openai-bundled\computer-use\26.1013.31256\skills\computer-use\SKILL.md`、`@oai/sky` をnode_replで使用。本文/guidance/api/confirmationsを読む。cua_repl Native disabledだけを理由に実操作不能としない。この担当はskill読了・sky初期化までで、**OS入力操作は一件もしていない**。sky.dragは左dragのみ、修飾キー保持や右／中dragの未対応範囲を模擬と混同しない。
7. 最終確定build絶対パス/run/data/既存Python実パスを固定した利用者用 `.cmd` を今回runに作る。既定EXEなしのguardを呼ぶ構成。EXE直接ダブルクリックは識別引数がなく終了するため、EXEだけを起動入口として渡さない。現在は確定版がないのでこの入口は未作成。
8. PV01〜PV17と元C1/AUTO/RE/UI4/SW2等の対応・現実の結果・未実行を整理。特にPV06/11/13/15/17を部分証拠だけで全PASSにしない。最終保全確認、試験用Unity/Player終了確認、残る人間確認をまとめる。

既存Pythonは `C:\Users\USER\AppData\Local\Programs\Python\Python312\python.exe`、Unityは `C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe`。新規取得・更新・Git書込み・公開・課金・外部API・別repo変更は禁止。

## 実行権限と停止時の事実

対象の実装／試験／build／Playerは業務上許可済み。ただし現担当環境はGUI実行に `require_escalated` を要求した。build004開始で一度 `Rejected("rejected by user")` が返った後、親経由の新しい本人指示「いちいち許可をもとめず進めてください。」により再開しbuild004/pivot/reloadを実施。その次のinitial起動要求にも `Rejected("rejected by user")` が返り、**Player未起動**。既定権限・別ツールへの切替で迂回していない。

その後は通常権限内の最終3ソース補強・純粋回帰・guard fixtureのみを行い、最新の「Codexへ引継ぎ」指示で製品作業を停止した。この記録は自動承認レビューが拒否理由を提示したという意味ではない。ツールが返した文字列は上記のみ。受け側は実環境の許可方式を確認し、必要な同種操作は既存Python＋固定build/launchスクリプトの狭いprefixを使い、無制限Pythonや保護回避は行わない。

## 保全と稼働状態

- `handoff/protected46-at-handoff.json`: 46/46 hash一致。
- `preflight/source-manifest-before.json` と `preflight/source-before/`: 開始時431ファイルのmanifestとテキストbackup。
- `handoff/source-manifest-at-handoff.json`、`source-change-ledger.json`: 開始時から25ファイルの差分（Unity import生成meta/scene/resourceを含む）。既存他者Showcaseソースは巻き戻していない。
- Git通常readは開始時dubious ownership拒否。safe.directory／所有権変更はしていない。raw HEAD `refs/heads/claude/project-thread-5gmozz`、raw ref `14d56f80d2e9758f550de5bb11e152960cb9fc6e` を `preflight/git-read.json` に保持。clean-treeを主張しない。
- `handoff/processes-at-handoff.json`: Get-ProcessでUnity/Shogi/Showcase該当0件。CIMのreadは権限拒否されたため、通常のGet-Processで確認。新たな強制終了は不要だった。
- 旧run/旧EXE/旧保存/設計原本/承認画像は書換対象にしていない。全旧ファイルを包括hash監査したとの主張はしない。今回の失敗build/失敗試験も削除していない。

次担当はこの地点を引き継ぎ、未完作業を完了させる。旧Goalや旧Showcase工程へ戻らない。
