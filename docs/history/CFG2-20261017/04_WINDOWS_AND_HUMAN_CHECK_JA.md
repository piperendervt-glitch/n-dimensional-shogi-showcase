# Windows実行と人間確認

revision-05は3解像度と別processの再読込で成功した。通常UIのOS入力も一部確認した。人間の物理操作・IMEと最終外観承認は未実施であり、以下の手順を残す。

## 起動と保存先

[FourDimensionalShogi.exe](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-05/FourDimensionalShogi.exe) を起動する。同フォルダー一式を保持する。旧CFG-1 EXEではない。

通常起動は引数不要。実行先に書込可能な現在の試作環境を対象とし、埋込runルートは `C:\dev\n-dimensional-shogi-showcase\artifacts\CFG2-20261017-004021-JST`。通常ユーザー保存はその下の `runtime-data/user/`、正常設定は `next-match.xml`、不正rawは `invalid-draft.xml`、試し盤・安定姿勢は `trial-view.xml`。棋譜は `match-records/<matchId>.xml`。試験の `smoke-r05-*` と `os-input-r05` は独立保存先で、通常保存の初期値を変更していない。

正常編集は自動保存し次局へ適用する。「保存」や「適用」を押す必要はない。無効値はそのまま残して開始を止める。正常保存を暗黙の初期値で置換しない。壊れた保存を読んだ場合は開始を止め、明示回復時には元ファイルを別名保全する。対局途中の再開機能は今回の必須範囲外。

## 環境と実行条件

| 項目 | 実測 |
|---|---|
| Windows | Environment.OSVersion：Microsoft Windows NT 10.0.26200.0 |
| Unity / SDK | 6000.3.11f1 / .NET SDK 9.0.312、既存ローカルキャッシュ28パッケージ。取得・更新なし |
| Windows Player | StandaloneWindows64 / Mono、NVIDIA GeForce RTX 5060 Ti |
| ビルド | revision-05、GUID a9d7dc9552b44deaae4a0f79240ce060、exit0、build errors0 / warnings2 |
| 解像度 | 実Playerで1536×1024、1920×1080、1280×720 |
| DPI | 各PlayerのScreen.dpi報告96。OS倍率の設定値・物理resizeの実証とはしない |
| 保存隔離 | 各自動runとOS操作runは専用 `--cfg2-data`。通常ユーザー保存へ試験値を書かない |

2件のbuild警告は旧・新UIの `StandaloneInputModule.forceModuleActive` 非推奨警告。既存ライセンスのtoken更新不可、外部接続を避けるlocalhost proxyへの接続拒否もUnityログに残るが、build reportはSucceeded。アプリ例外とこの環境ログは分けている。初回revision-01は通常sandboxのライセンスIPCで進まず、今回所有プロセスだけ回収して正規の権限経路でビルドした。失敗は生ログに保持した。

## 自動確認の経路

| 経路 | 結果と証拠 |
|---|---|
| pure C# | 5系列成功。制御時刻、CPU取消・世代・予算、全8駒種両陣営、全4D遮蔽・利き、保存失敗境界など。[試験索引](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/core-test-coverage.json) |
| 実Player API＋模擬uGUI＋実mesh pick | 3解像度それぞれ176項目成功。画像・実Text高さ測定1727件、非scroll領域の高さ不足0。通常40駒・短い終局fixtureを別条件で確認 |
| 別process | PID25700保存→PID17700読込、10項目成功。復元設定で40駒開始・実UI着手 |
| OS入力注入 | Computer Use `@oai/sky` のSendInputとWindowsキャプチャ。PID23572、通常UIで設定と実着手・終了を確認 |
| 人間の物理入力・IME | 未実施。H2-01 HUMAN_PENDING |
| 人間の外観・操作感検収 | 未実施。H2-02 HUMAN_PENDING |

すべての自動起動引数・UTC・終了コード・match IDとhashは [runtime-verification-index.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/runtime-verification-index.json)。ビルドと配布ファイルhashは [build-manifest.json](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-manifest.json)。元の失敗結果をPASSに変更していない。

3モードとも通常40駒の開始と進行を確認した。通常終局の短い試験局面ではPvP/PvCの人間側を実hitと実ハンドラ経由で操作し、CPUはその時点の候補から選ぶ。王を直接削除したり勝敗flagを書き換えたりしていない。時間切れ用0.3秒と完了0手用の明示した駒条件は試験専用。試験中断を引分とはしない。

## OS入力で実際に確認した内容

初回の操作承認待ちはタイムアウトしたが、対象を再列挙して一度再試行した後は接続できた。他アプリの操作や回避用ツールの作成はしていない。

1. 通常メニューから「オプション」をクリックし、王の同時最大軸数を選択。
2. `x` のキー入力で不正raw保持・欄近くのエラー・開始保留表示を確認。Ctrl+A→`1` でG8/P8、表示内G6/P6へ更新。
3. Zギズモをクリックし、実イベント `SliceRequest Z` と安定保存 `XYW` を確認。主ビューのホイールで `CameraZoom 0.785`。
4. Escapeで戻りPvPをクリック。通常40駒から歩A-Pawn-0を選択し、青い盤面候補をクリックしてply1へ確定。視点Bへの交代と双方の時計を確認。
5. EscapeでInterruptedとして戻り、「ゲーム終了」をクリック。確認ダイアログなし、QuitログとPlayerのwindow/process不在を確認。

[OS観測JSON](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/os-input-observations.json)、[実イベント](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/runtime-data/os-input-r05/events-23572.tsv)、[実着手棋譜](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/match-records/3cdb075badc64929bcb386af7fa1b640.xml)、[着手後のB視点画像](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/screenshots/os-input-r05/pvp-real-move-b-view.png) を参照。実手後hashは `d6585b12f064d3def6467c160eaa2b848f20753916d20451ef9f17720e397cb1`。

小窓の空間へのドラッグ投入は行ったが、軸handleによる持続変化を証明していないためPASSにしていない。右／中ボタンのドラッグ、OSフォーカス移動／最小化、IME変換はこのOS確認に含まれない。時計のfocus条件はpure制御時刻と実Playerの明示callbackで別途確認している。

## 人間確認手順

| 対象 | 操作と見る点 |
|---|---|
| H2-01：文字入力 | オプション→駒の動きで軸数を4→1→2に変更。G/Pと表示が追随すること、不正文字を入れると開始保留になり修正できること。ルールプリセットの名称欄では日本語IMEの変換・確定・取消を確認 |
| H2-01：主ビュー | 駒の選択→青候補へ移動、右ドラッグで視点回転、中ドラッグで平行移動、ホイールで距離。ドラッグ中Escapeやフォーカス移動で入力所有と位置の取消を確認 |
| H2-01：4D | X/Y/Z/Wギズモを順にクリック。4つの小窓では文字がある軸handleを掴み軸方向へドラッグ。断面中心・交差図だけが変わり、本対局の駒位置・手番が変わらないこと |
| H2-01：試し盤 | XY/XZ/XW/YZの固定軸条件を見て、置く・消す・クリア・Undo/Redoを試す。試し配置が通常40駒対局へ混入しないこと |
| PvP | 先手の駒→青候補へクリック。後手視点へ移動し次の時計だけが進むこと。途中メニューで中断され、次回は新しい40駒になること |
| PvC | 人間Aの1手後にCPU Bが応答し、人間側視点が維持されること |
| CvC | 両CPUの着手を見る。一時停止で時計も止まり、1手進めるでちょうど1手後に止まり、再開で続くこと |
| 保存・終了 | 設定を有効に変更→戻る→終了→再起動。最後の正常設定・安定断面が復元すること。棋譜やエラーは今回runに残る |
| OS環境 | 普段の倍率・ウィンドウサイズで文字、候補、捕獲数を読めるか。フォーカスを外しても実時間時計は進むこと（損失を避けるなら短い試験局で確認） |
| H2-02 | 承認画像との配色・領域・余白、1280×720の小さい補助文字、反対側視点でのタイトルのコントラスト、空中候補と盤の位置関係、視点移動の速さと好み、デモ制約の理解を確認 |

終局・捕獲数・王除外は自動fixtureで証拠済み。人間が短時間で再確認する場合は [02の終局fixture表](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/02_CHANGES_AND_TESTS_JA.md) と同じ条件を検証入口で再現する。通常UIに結果強制ボタンはない。自動安全タイムアウトや上限手数を勝敗に変更しない。

## 終了状態

本runで起動したPlayerは終了した。常駐サーバー・監視・次工程は開始していない。今回の技術実装完了は、人間の検収合格や製品の正式ルール確定を意味しない。
