# revision-03 実画像確認と捕獲枚数修正

実閲覧: `screenshots/initial-1536x1024-19216/pvp-captured-A.png`、`piece-settings.png`、`menu.png`。

確認: 対局の同一木駒二重表示が解消し、メニューの木駒は盤上になった。設定背景の旧メニュー残像が消え、一覧の固定定義折り返しも短名化で解消。4小窓は固定座標と実駒数が表示される。

残課題: 捕獲台の木駒自体は見えるが、従来TextMeshの枚数はこの実画像でも読めなかった。HDRPと従来フォントmaterialの組合せが原因候補であり、原因を断定していない。

親担当の指示で `Cfg2Visuals` の枚数をWorldSpace Canvas＋既存CfgUi.Textに置換した。各種木駒近くに幅25mm・高さ12mmの暗い半透明札、白い「×N」、カメラbillboardを置く。枚数は従来と同じ捕獲側・駒種グループの実Count。取得・新フォント・外部素材はない。`CapturedLabel(owner,definition)` で実Text文字列を確認できる。

`logs/cfg2-check-settings-06.log` の型検査はexit0。次revision-04のPlayerで札が実際に見えるか、1枚と2枚、両側と視点交代を確認するまで、枚数画像のPASSは付けない。
