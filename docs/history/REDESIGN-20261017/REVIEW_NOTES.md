# 再設計用幾何モデルの独立照合

対象はこのフォルダの設計計算用JavaScriptと既存C#定義の対応。この独立レビュー担当は製品を変更せず、Unityのビルド・起動や製品試験を行っていない。後述する既存pure C# DLLの設計比較は、追加指示を受けた幾何担当が別に実施したもの。設計数値チェックをUnity実装・実マウス操作・人間の外観承認のPASSとして扱わない。

## 読取で確認した対応

- 初期配置の40個のID、駒種、陣営、座標は [Rules.cs:191](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Core/Runtime/Rules.cs:191) の配置式と一致する。Aの後列F=-4・歩F=-2、Bは逆符号、飛角のUも陣営ごとに反転する。
- 設計モデルの `[X,Y,Z,W]=[U,V,F,W]` は [SectionView.cs:119](C:/dev/n-dimensional-shogi-showcase/Unity/Assets/Showcase/Runtime/SectionView.cs:119) と対応する。Bの形状方向がXとZを反転しY/Wを保つことも、[GeometryTypes.cs:75](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Geometry/Runtime/GeometryTypes.cs:75) のF/U反転と一致する。
- 0度と各単回90度の基準姿勢は、[SliceInteraction.cs:15](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Geometry/Runtime/Interaction/SliceInteraction.cs:15) の基底を表示スロットへ変換したものと一致する。XYWはB=[X,Y,W], n=-Z、XZWはB=[X,W,Z], n=-Y、YZWはB=[W,Y,Z], n=-X。ビュー名から無断でスロットを並べ替えていない。
- Y=0の断面で駒が40個の3D体積として残ることは矛盾ではない。原形状はY>=0で、Y=0は4D形状の3次元底部境界と一致する。一方、元のTop境界は残らない。この状態に漢字を表示する場合は「識別注釈」と明示し、元の上面材質が残ったと説明しない。

## 指摘した不一致

初回読取モデルのSHA256は `8268A950B38AEA13C905C48934D20AA42BE2782C57C16A82654AE759E1D9BF20`。その `shapeC()` は元の未正規化法線を足して面取りを作っていた。

C#は [Halfspace4のコンストラクタ](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Geometry/Runtime/GeometryTypes.cs:59) で各法線とoffsetを同時に正規化してから、[ShapeFactory.cs:28](C:/dev/n-dimensional-shogi-showcase/src/Showcase.Geometry/Runtime/ShapeFactory.cs:28) の面取りを合成する。単一半空間では同じでも、正規化前後に法線を足した結果は異なるため、初回モデルの「ソース形状と同じ」という説明は成立しない。担当へ通知済み。製品C#を変更する修正ではない。

V8内の一時的な設計計算でbaseの正規化だけを補った比較は次のとおり。ファイル上のモデルはこのレビュー担当では変更していない。

| 原点相当の駒断面 | 初回JS体積 | base正規化後のJS体積 |
| --- | ---: | ---: |
| XYZ | 0.056449941184943904 | 0.05647236818184844 |
| XZW（Y=0） | 0.14089499629590913 | 0.140081782440034 |
| XW 45度 | 0.07983227241891128 | 0.07986398898209694 |

これは設計JSの数値比較であり、C#ライブラリの実行結果ではない。元の82チェックが全てPASSでも、この移植誤りを検出する独立したソース対応チェックがなければ同値性は証明できない。

### 修正後の再照合

幾何担当は設計モデルを修正し、[geometry-checks.json](C:/dev/n-dimensional-shogi-showcase/artifacts/REDESIGN-20261017-0210-JST/geometry-checks.json) を再生成した。現在のモデルSHA256は `9094D5F25C69D29D72A7996840BEACB0895DD1D41B041A278D738E40BE45F0F5`。旧モデルの不一致は同JSONでもsupersededとして区別されている。

このレビュー担当は、C#の生成式を別の小さいJS計算で再構成し、修正モデルの全48半空間を独立照合した。FaceId・SurfaceKindは48件一致、係数の最大差は `2.220446049250313e-16`、不一致0。XYZ/XYW/XZW/YZWの実体数は40/0/40/4で、XYZとXZWの体積はそれぞれ `0.05647236818184853`、`0.14008178244003444`。最初の面取り移植不一致は解消されている。

幾何担当は追加指示により既存revision-05のpure `Showcase.Geometry.dll`を読込し、48半空間と108断面を比較した。別担当から報告された最大差は係数 `1.11e-16`、体積 `9.71e-17`、頂点Hausdorff距離 `3.24e-15`。156比較と既存82設計不変量を合わせた238件が現在のJSONに記録され、モデルSHAも一致する。この独立レビューはそのC#比較を再実行していない。いずれも製品の入力・描画・外観受入のPASSではない。

## 提示時に残す境界

1. `composeFrame()` は固定世界平面を左から作用させる。旧Playable設計の局所平面右積とは、複数の回転を合成すると異なる。モデル内の規約文字列だけでなく、画面にも「固定世界軸の設計計算」と分かる説明が必要。製品の自由回転操作・保存方式の採用ではない。
2. `center` を固定して回す設計案と、旧計画の `c=P+h*n` を更新する方式を同一視しない。原点以外の回転中心・高さは別の未採用選択として確認する。
3. `miniSection()` は現在の無限超平面と、同じcenterを通る固定参照立方体との交差図。主表示の有限表示窓そのものの境界ではない。共通centerを移した場合の固定座標もラベルへ反映する。
4. `sectionBoard()` の制約集合は元の箱＋面取りに対応するが、汎用`box4()`のFaceIdとSurfaceKindは元C#の盤面属性と同じではない。「元の木目・Top・Bottomの材質属性まで同値」とは主張しない。
5. glyph/texture名は駒の識別情報。画面向きの漢字を描く場合、元の固定4D材質とは別の注釈とする。切断後に残ったTopの有無と、識別ラベルの可視性を分ける。
6. 形状・セル・選択のアンカー一致は設計モデルの幾何条件。既存Unityの二重描画・入力死域・CPUによるカメラ上書きが修正済みであることを意味しない。

## 製品ソースの読取時点一覧

[source-snapshot.json](C:/dev/n-dimensional-shogi-showcase/artifacts/REDESIGN-20261017-0210-JST/source-snapshot.json) は指定3ディレクトリの393ファイルを記録した。Unity/Assets/Showcaseが147、srcが51、testsが195。無視ファイルも含め、bin/objの185ファイルを別フラグにし、残り208ファイルと区別した。

この一覧は **after-only観測** であり、比較元なし・過去との変更比較なし。過去から製品が不変だったことを証明する台帳ではない。このレビューが新規作成したのは設計フォルダ内の一覧と本書のみ。
