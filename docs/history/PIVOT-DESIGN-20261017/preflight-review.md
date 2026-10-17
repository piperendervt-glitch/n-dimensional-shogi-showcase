# 設計引継ぎ時の保全確認

2026年10月17日JST。製品実装の着手直前には実装担当で再照合する。

## 原本の読取

監査担当は、実在するCFG1実装依頼書、CFG0の00〜05の6文書、06_READONLY_EVIDENCE.json全文、07_FINAL_READONLY_CHECK.jsonを読了したと報告した。計画§4は親担当も読み、基底・経路・中心・窓・入力・保存の制約を新設計へ照合した。08_ACCEPTANCE_ID_LEDGER.jsonは出典manifestへhashを保存し、実装担当への必読資料に含めた。

## 保全対象

親担当の再計算で元の未追跡46ファイルは46件すべてSHA-256一致。詳細は06_PRESERVATION_CHECK.json。設計で参照した製品ソース15件も、manifest作成後の再照合で変更0件だった。設計担当は製品コード、Unity場面、既存保存、旧証拠を変更していない。

## Gitの照会

親担当の通常のGit照会は所有者相違で拒否された。その結果は06_PRESERVATION_CHECK.jsonへNOT_VERIFIEDとして保存しており、空のStatusはcleanを意味しない。

別の監査担当から、GIT_OPTIONAL_LOCKS=0とコマンド限定の `-c safe.directory=C:/dev/n-dimensional-shogi-showcase --no-optional-locks` による読取結果が報告された。設定の永続保存はしていない。

- HEAD：14d56f80d2e9758f550de5bb11e152960cb9fc6e
- ブランチ：claude/project-thread-5gmozz
- origin：元契約の対象と一致
- staged：なし
- unstaged：Unity/Assets/Showcase/Runtime/SectionView.cs と Unity/ProjectSettings/ProjectSettings.asset
- untracked：報告時126件。既存CFG1/CFG2成果と今回設計ファイルを含むため、件数増加だけで消去・阻害判定をしない

この箇条書きは監査担当の報告であり、親担当の失敗した通常Gitコマンドが成功したという意味ではない。実装担当は現在の差分と所有を再確認し、Git書込みやclean化を行わない。

## 実行プロセス

親担当のGet-ProcessではUnity/Shogi/Showcase/dotnetに一致するプロセスは0件。CIMによるコマンドライン取得は権限不足のため未確認。これを全エージェント・全エディターに競合がない証明にはしない。権限やOS設定は変更していない。

## 引継ぎ状態

数学とUI操作の設計レビューは完了。7件の独立した式の検算は設計限定であり、製品試験ではない。PV01〜PV17はすべて実装検証待ち。

既存Codexチャット「実装依頼を確認する」への送信と担当の着手応答は07_CODEX_HANDOFF_RECEIPT.jsonへ保存した。設計側の読取監査はここで終了し、製品実装・試験・Windows確認は依頼を受けた担当が行う。
