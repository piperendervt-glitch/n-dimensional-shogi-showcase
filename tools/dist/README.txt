N次元将棋（N-Dimensional Shogi）デモ版
=====================================

4次元以上の盤で指す将棋です。盤に見えているのは N 次元空間の 3 次元の断面で、
見えていない方向にも駒が動けます。

■ 起動
  NDShogi.exe をダブルクリックします。
  「Windows によって PC が保護されました」と出たときは「詳細情報」→「実行」を押してください
  （署名のない個人制作のアプリのため表示されます）。

■ 最初に
  初めて起動すると「遊び方」が開きます。あとからはメニューの「遊び方」か F1 キーで開けます。

■ 基本の操作
  左クリック        駒を選ぶ・動かす（青いマスが動ける場所）
  Tab               選んだ駒の、今の断面では見えない候補が見える断面へ回す
  R                 最初の表示に戻す
  ドラッグ／ホイール  視点を回す／拡大・縮小
  Ctrl＋ドラッグ     盤を 4 次元で回す
  F1／Esc           遊び方／戻る（対局中は確認があります）

■ 動作環境
  Windows 10 / 11（64 ビット）、DirectX 12 に対応した GPU、空き容量 約 600 MB。
  ノート PC の内蔵 GPU では重いことがあります。オプション →「表示・操作」→「画質」で「低」を選んでください。

■ 保存先とログ
  %USERPROFILE%\AppData\LocalLow\piperendervt-glitch\四次元将棋
  うまく動かないときは、ここにある Player.log を添えて GitHub の Issues へお知らせください。
  https://github.com/piperendervt-glitch/n-dimensional-shogi-showcase/issues

■ ライセンス
  このゲームのプログラムと自作素材は MIT License です（LICENSE.txt）。
  ほかの素材の条件は THIRD_PARTY_NOTICES.txt を見てください。
