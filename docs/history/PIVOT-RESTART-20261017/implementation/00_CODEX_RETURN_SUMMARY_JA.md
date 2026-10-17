# PIVOT実装・Windows実行確認の返却記録

2026年10月17日。依頼ID `PIVOT-RESTART-20261017-083955-JST`。対象 `C:\dev\n-dimensional-shogi-showcase`。

設定変更→共通G/P→実断面操作→保存→別プロセス再読込みの接続と、既存対局へのPIVOT表示・入力の接続を実装し、新Windows Player build-009で自動検証した。実OS入力では設定編集、ズーム、Home、軸クリック、駒の選択と1手確定を確認した。**ドラッグ類とShift+Homeの実入力、外観の最終採否は未確認であり、全受入の全面合格とはしない。** 今回の作業はここで停止する。旧Showcase Goal・自動再生・動画・性能測定へ戻らない。

[START_PIVOT.cmd](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/START_PIVOT.cmd) をダブルクリックすると、版照合後に1280×720の通常ウィンドウで開始メニューを開く。開始メニューで操作を待つ。専用保存先は `implementation/runtime-data/user-final-009`。この人間確認用データは試験用データと別で、引渡し時点では未作成。EXE直起動で照合を省略しない。

[Windows操作・人間確認手順](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/02_CODEX_WINDOWS_AND_HUMAN_CHECK_JA.md) と [変更台帳](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/01_CODEX_CHANGES_JA.md) を添付する。

## 最終ビルドと版の対応

最終EXEは `C:\dev\n-dimensional-shogi-showcase\artifacts\PIVOT-RESTART-20261017-083955-JST\implementation\windows\build-009\NDShogiPivot.exe`。

- Build GUID: `4dfababe512f44a8864f107ff8bbb497`
- Source SHA256: `ee25fd80a2cba082dc56ffbb45d0327c81caf2fe01a4b8afa015382133205ba4`
- Unity 6000.3.11f1、`Cfg2Build.BuildWindows`、`Cfg2.unity`、`Showcase.Unity.Settings.Cfg2App`。
- ビルド結果Succeeded、errors 0、warnings 4、sourceDriftなし。既存の入力API非推奨警告と診断用shaderの整数演算警告をログに保持した。今回の製品機能拡張として扱っていない。
- 386ソース、172配布ファイル、4実装DLL、実埋込identity、Unity実行記録、全9 PlayerのPID/保存先/自己identityを照合済み。
- 起動ガードの故障注入24条件、最終実配布物による5条件、固定入口の7条件をそれぞれ検証。異なるrun、破損DLL、古いmanifest、失敗ビルド、引数による起動先変更を拒否する。条件数を単一の独立試験数へ合算しない。

[最終版・起動の独立照合](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-build009-20261017-095411-459144.json)、[固定入口7条件](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-entry-20261017-095410-453377.json)。

## 最終Playerでの結果

| 実行 | PID | 実画面 | 検算結果 |
|---|---:|---|---|
| PIVOT | 32724 | 1920×1080 | 710 PASS、exit 0 |
| 既存対局initial | 33616 | 1536×1024 | 180 PASS、exit 0 |
| 対局別プロセスreload | 6496 | 1536×1024 | 11 PASS、exit 0 |
| PIVOT | 10236 | 1536×1024 | 710 PASS、exit 0 |
| PIVOT別プロセスreload | 3876 | 1536×1024 | 121 PASS、exit 0 |
| 保存復旧 | 20992 | 1536×1024 | 3 PASS、exit 0 |
| 復旧後の別プロセスreload | 33852 | 1536×1024 | 2 PASS、exit 0 |
| PIVOT | 29868 | 1280×720 | 710 PASS、exit 0 |
| OS操作 | 33016 | 1280×720 | 項目別結果、メニューから終了 |

710などの値は頂点ごとの検算を含むassertion数であり、独立したシナリオ数ではない。各起動の記録とログhashは [最終Player集約](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/codex-final-player-runs.json) にある。復旧は新規試験用の破損データだけを使用し、拒否原本のSHA256が前後一致した。回復先を別プロセスで再使用することも確認した。

純粋回帰はCore36、Geometry3,372,857 checks、Settings43、Interaction21,669 assertions、Match30／337 assertionsで全5 runner exit 0。[純粋回帰記録](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/tests-final-codex/summary.json)。最終の純粋ソース・試験・scenario 79ファイルは再照合でも差分0なので、Unity試験コードだけの後続修正で同じ純粋試験を繰り返していない。

## 実OS操作と残る確認

[OS入力の結果](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/os-input-build009/results.json) にスクリーンショット・保存値・Playerログを接続した。computer-use skillの `@oai/sky` で操作した。

確認済みは、設定を開く、最大軸数0の警告と1への訂正、G/P件数80→8・表示内26→6、wheel zoom、HomeでC=Oと表示補償、設定/プレイ軸クリック、駒選択、1手の確定とBへの手番交代、通常終了。

左ドラッグ2回と軸ドラッグ1回は試したが、有効な視点・中心移動を観測できなかった。Shift+Homeも試したが、保存値はOのままでPlayerはHome Oとして記録した。画面だけの途中評価を保存値で訂正し、これらをHUMAN_PENDING／OS入力未実証として残した。利用した手段では修飾キーを保持したdragや右/中ボタンdragを指定できないため、そのOS入力と操作中の取消・フォーカス喪失はNOT_RUN。アプリ内模擬イベントの結果をOS確認済みに昇格しない。

[ドラッグの独立診断](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-os-drag-20261017-095116-514279.json) では接続漏れを確認していないが、ネイティブ入力時系列がないため製品か操作ツールかの原因を断定していない。物理マウスでのdrag、Shift+Home、Ctrl系、右/中drag、取消を人間確認手順へ残す。

Player報告DPI96、OSレジストリAppliedDPI96（公称100%）を確認した。ウィンドウごとの実効DPI独立測定、150%/200%での実機確認は未実施。

## 保全・失敗記録・受入の境界

原本46件、索引に接続した原本13件、設計入力/参照画像15件が一致した。[終了時保全](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/preservation-build009-final-source.json)。引継ぎ変更や旧Showcase作業を巻き戻していない。Gitの所有者不一致を設定変更で回避せず、Git書込み・新規取得・外部公開・他リポジトリ変更は行っていない。

build005/006の既存対局試験では近い候補による遮蔽を検出した。独立ray解析で確認し、元の局面・移動先・捕獲期待を保って固定された公開camera gestureを使う試験へ修正した。製品pickerを試験に合わせて変更していない。build007の1080pではFloat32入力端点による1.57785e-6セルの差を検出した。理想1セルの検査を残し、量子化済端点による独立計算との1e-9セル照合を追加した。build008は診断ログ呼出しの誤記でコンパイル失敗し、修正したbuild009を新規作成した。失敗ログは保持している。

元196＋C1 18＋PV17の231受入IDは索引上の条件数であり、231条件全部PASSではない。[最終受入表](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/acceptance-final-build009.json) で、旧仕様との変更、範囲外、模擬入力、OS入力、HUMAN_PENDINGを区別した。共通rendererの検査を全GPU描画パスの画像比較へ昇格しない。完全合法性、汎用CPU、成り・打ちの新規実装は今回範囲に含めない。[最終ガードレビュー](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/guard-final009-20261017-095535-929744.json) でも、版照合とOS入力の残件を区別している。

メニュー/設定/プレイの実画像を参考画像と比較した。背景素材、質感、字体、パネル比率や格子密度には差が残る。軸ラベルの暗い下地と表示内容は確認したが、完全な画質一致や人間の外観承認は主張しない。`approved-*.png` は比較用の名前である。

試験用Playerは通常メニューから終了し、Unity/NDShogiPivotの残存プロセスはない。[終了確認](C:/dev/n-dimensional-shogi-showcase/artifacts/PIVOT-RESTART-20261017-083955-JST/implementation/final-review/cleanup.json)。今後の自動作業や旧Goalの再開は行わない。
