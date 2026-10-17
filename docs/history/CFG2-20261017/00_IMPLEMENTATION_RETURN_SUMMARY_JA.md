# CFG-2 実装返却

到達状態：**CFG2_IMPLEMENTED_VERIFIED_AWAITING_HUMAN_REVIEW**。CFG-1を保全して、承認UIに対応する新しい入口と、同じ4D定義を使うPvP・PvC・CvCを実装した。C2-01〜C2-20は今回の証拠でPASS。人間の最終検収と正式ゲームルールの採用は完了扱いにしていない。

## 起動するもの

[最終Windows EXE：FourDimensionalShogi.exe](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/Windows/revision-05/FourDimensionalShogi.exe)

同じフォルダーのData・DLL等を含む一式が必要。通常起動で新しい5項目メニューが開く。通常保存先は今回runの `runtime-data/user/`。自動試験用保存先とは分離した。操作と再現手順は [04_WINDOWS_AND_HUMAN_CHECK_JA.md](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/04_WINDOWS_AND_HUMAN_CHECK_JA.md)。

## 到達内容

| 項目 | 今回確認した結果 |
|---|---|
| PvP | 設定反映→通常40駒開始→双方の盤面UI実着手。短い検証局面では捕獲→王捕獲→結果→新しい40駒の次局までPASS。手番視点と時計も連動 |
| PvC | 人間Aの盤面UI着手とCPU Bの共通判定による応答。捕獲・王捕獲・次局までPASS |
| CvC | 両側CPUが実候補を評価して着手。一時停止・再開・1手進める・古い結果拒否、王捕獲・次局までPASS |
| 設定と4D操作 | 6カテゴリ、共通G/P・全4D利き、実断面・4超平面、XY/XZ/XW/YZ確認図、独立試し盤とUndo/Redo、正常自動保存・無効raw保持を接続 |
| 再起動 | 別processで設定・試し盤・安定断面を復元し、その設定で実対局開始・着手までPASS |
| Windows | Unity 6000.3.11f1の新規revision-05ビルド成功。1536×1024／1920×1080／1280×720各176項目PASS、別process再読込10項目PASS |
| pure C# | Core36、Geometry3,372,857検査、Settings25、Interaction13,746検査、新Match30ケース・337検査が成功 |
| OS入力 | Computer UseによるOSクリック・キーで軸数の不正入力と修正、G/P更新、ギズモ切替、ホイール、PvPの実着手、視点交代、Escape、終了を確認 |
| 保全 | 歴史的46件、開始時tracked変更2件、旧資料・旧Windows・依存のSHA一致。CFG1ソース43件中、許可変更3件と不変40件を区別。未知差分0 |

終局用の短い局面は試験条件として明記し、通常40駒開始の確認とは分けた。結果flagの直接設定や演出棋譜による代用はしていない。旧196項目の全達成を意味するものでもない。

## 仕様の限界と未実施

採用したのは今回依頼書§5のデモ限定 `CFG2_DEMO_4D/1`。N=4・各軸−4..4・両陣営共通4D、王捕獲／完了0手／時間切れの終了規則、初期持ち時間600秒、簡単なCPU。成り・打ち・自玉安全・完全合法性L・自動反復判定・強いAI・ネット対戦は対象外。捕獲台は捕獲側の表示であり再投入在庫ではない。

H2-01は**HUMAN_PENDING**。OS入力注入は実施したが、人間の物理マウス・キーボード・IME、右／中ドラッグ、OSフォーカス移動・表示倍率変更を検収済みとしていない。小窓ドラッグはOS投入したものの持続回転を証明できず、その操作をPASSにしていない。H2-02も**HUMAN_PENDING**。外観・小文字の読みやすさ・4Dの理解しやすさ・視点移動の好みは人間確認を残す。

承認画像に対する主領域・配色・機能を実装し比較した。既存HDRP素材の照明・構図・装飾には差異があり、画像そのものの完全複製や人間の美観承認とはしていない。

## 証拠への入口

- [01：基準状態・限定仕様の判断](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/01_PREFLIGHT_AND_DECISIONS_JA.md)
- [02：実ファイル変更・試験・失敗と修正](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/02_CHANGES_AND_TESTS_JA.md)
- [03：承認画像と3解像度の比較](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/03_UI_COMPARISON_JA.md)
- [受入台帳 C2/H2](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/acceptance-cfg2.json)・[旧196＋C1全18の非昇格対応](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/legacy-acceptance-map.json)
- [配布物・ソースのビルド台帳](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/build-manifest.json)・[Windows実行と棋譜の索引](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/runtime-verification-index.json)
- [保全の前](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preservation-before.json)・[保全の後](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/preservation-after.json)・[OS入力証跡](C:/dev/n-dimensional-shogi-showcase/artifacts/CFG2-20261017-004021-JST/os-input-observations.json)

Git書込み、外部取得・更新、公開、課金、別リポジトリ変更は行っていない。起動した検証Playerは終了済み。ここで今回の作業を終了し、次工程へは自動移行しない。
