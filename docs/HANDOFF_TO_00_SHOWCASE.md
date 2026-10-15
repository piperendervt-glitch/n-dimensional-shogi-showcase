# 00へのShowcase引継ぎ

実装先: `C:\dev\n-dimensional-shogi-showcase`。ユーザー指定remote: `https://github.com/piperendervt-glitch/n-dimensional-shogi-showcase.git`。独立した未commitのmain。commit・push・公開、本線変更は未実施。

採用仕様は [APPROVALS.md](APPROVALS.md)、固定ルール・CPU・形状は `scenarios/`、全48受入条件は [ACCEPTANCE_SHOWCASE.md](ACCEPTANCE_SHOWCASE.md) にある。

実装は、固定4Dの27手DirectedLive、別モードのVerifiedReplay、Bの専用3D観測境界、A/Cの固定4D形状、局所4D材質座標、HDRPの屋外卓上表示、Windows x64ビルドを含む。追加指示により3Dの逃走・追撃を3組行い、以降はAの4D着手1回ごとにH0へ戻りBが1手応答する。Bは金・銀・飛車・歩を使用する。演出上の選好を持つCPU戦であり、自由対局の強さを実証するものではない。

追加の画質調整では、CC0実写木材、2048pxの文字・法線・粗さ、ACESと物理空、PCSSと木漏れ日、AO・接地影、天板の平面反射を導入した。描画目標はユーザー指示で30fps。4Dの出入りは全画面の時間停止風効果を伴ってゆっくり回転し、効果はT、各光学効果はF2〜F8で切り替えられる。間接光は空のIBLと補助光を使い、完全な焼き込みGI／SSGIとはしていない。

次の判断は、人間による4参考画像との照合、5場面の読みやすさ、木目と文字の連続性、A/Cの外観選択。技術試験の成功を人間承認へ読み替えていない。確認欄は [HUMAN_VISUAL_CHECKLIST_SHOWCASE.md](HUMAN_VISUAL_CHECKLIST_SHOWCASE.md) に用意した。

OSキー入力の配送は操作ツール初期化障害により未確認。Standalone内で同じ操作メソッドを用いた試験とは分離している。Space、R、F1、H、Esc/Enterは手動確認が必要。

最終build、run、性能、画像・動画、残件の対応は [IMPLEMENTATION_REPORT_JA.md](IMPLEMENTATION_REPORT_JA.md) と証跡索引を参照する。人間の承認があるまで `ACCEPTED` としない。
