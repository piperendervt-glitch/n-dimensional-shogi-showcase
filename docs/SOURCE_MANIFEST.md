# 原本・入力資料の台帳

記録日: 2026-10-15（ユーザーのAsia/Tokyo日付）。作業場所: `C:\dev\n-dimensional-shogi-showcase`。

以下はユーザーが指定したDドライブの原本を読み取り、バイト列からSHA-256を計算した記録です。原本への書込みはしていません。画像の盤目・漢字・配置はルールの正本には使用しません。

| ファイル | bytes | SHA-256 | 読取 |
|---|---:|---|---|
| 00_SHOWCASE_MODE_CHAT_KICKOFF_v0_1_JA.md | 3568 | `c42deb5dab9be912bad8eb2b73bfc47644ed3a44bda56a90528f399652626dd0` | READABLE |
| 00_SHOWCASE_MODE_HANDOFF_v0_1_JA.md | 10598 | `cfc7af2ed899cb43bdb37f14738e4e7c9cba91345586ed4ddfb9fb5fdaaad059` | READABLE |
| CODEX_SHOWCASE_MODE_IMPLEMENTATION_REQUEST_v0_1_JA.md | 18291 | `86bd2c04d63750809352ab2984bc7bf6a3843033d3634607c436c7a97d7bab56` | READABLE |
| CODEX_SHOWCASE_MODE_START_HERE_v0_1_JA.md | 6593 | `2c7113552c4cbd8aafa736dad801ac45dfc4375817a6c708c71be06c7198bca3` | READABLE |
| README_SHOWCASE_MODE_PACKAGE_v0_1_JA.md | 953 | `7f2afa1cdbf31f8dba2ce782aae509582c3eb47df4d0218c8ffea39ceba8a01c` | READABLE |
| SHOWCASE_MODE_OPEN_QUESTIONS_v0_1_JA.md | 12357 | `7635e69a69894515200ef3920e509bd7c95506b138ef293a48775da9adb3bf3a` | READABLE |
| SHOWCASE_MODE_PLAN_v0_1_JA.md | 48763 | `c9698cbdaefaf1b1c59a55ceae6d5836801af799f9504bfd9a1895670b38ad5d` | READABLE |
| 06_REALTIME_CG_RENDERING_DESIGN_v0_1_1_JA.md | 60730 | `f872494a7095e298ef886e1df31369aa24f67520eebc895be1829a0b17802145` | READABLE |
| ChatGPT 画像 2026年10月15日 17_56_35-1.png | 2447585 | `970496ec93777bbfdbcc6e97999d3af2cb71485ead142d310332f828d6d4d0c3` | READABLE |
| ChatGPT 画像 2026年10月15日 17_56_36-2.png | 2470157 | `e89506592fa348046005127df3afb4ef99a59d0b0ad4c60472dcd141834ce86c` | READABLE |
| ChatGPT 画像 2026年10月15日 17_56_39-4.png | 2282684 | `498a6f18960e1fc4ba7d1b70490c3e27c036dcac6dd13f50addb4629c03be473` | READABLE |
| ChatGPT 画像 2026年10月15日 17_56_40-5.png | 2372857 | `3349b87927f9cb91f6443ff620cecff7c0ae5892b317b9a7d1db4e4a49606474` | READABLE |

原本フォルダー:

- Showcase計画一式: `D:\Users\pipe_render\Downloads\n-dimensional-shogi-showcase`
- 06設計書と参考画像: `D:\Users\pipe_render\Downloads\n-dimensional-shogi`

PLANを今回の設計正本、OPEN_QUESTIONSを仕様判断の台帳、IMPLEMENTATION_REQUESTを実装・受入条件の入口として扱います。START_HERE、HANDOFF、CHAT_KICKOFF、READMEも同一受領パッケージとして記録しました。06設計書と4画像は補助資料です。4画像は導入、3D展開、帰還後の不在、4D攻撃に対応します。

## 実装とbuildの追跡

ローカルcommitは許可されていないため、現時点のcommit IDはありません。`evidence/environment/workspace-audit.json` の最終監査ではbranchは `main`、HEADはunbornです。未commitのローカル実装であり、存在しないcommit IDは記録していません。remote作成・push・外部公開は行っていません。原本のSHA-256は実装のcommit IDとは別です。

`tools/export_source_manifest.py` が実装ファイルの一覧・サイズ・SHA-256と集合のsourceHashを `evidence/environment/source-manifest.json` に出力します。同じJSONを `evidence/environment/source-manifests/<manifest自体のSHA-256>.json` へ不変の履歴として保存します。生成される `Unity/Assets/Resources/showcase-build.json` とそのmetaは自己参照を避けるため集計対象外です。作成後のソース変更は、そのmanifestとは別状態です。最終build時に再生成し、Unity build GUIDと実行物のハッシュをbuild台帳へ対応付ける必要があります。

`tools/analyze_runtime.py` はrunのbuild GUID/sourceHash/sourceManifestSha256とmanifestの完全一致だけを関連付けます。日付や最新ファイルという理由で古いrunに新しいソースを対応させません。対応がないrunは `UNJOINED` のままです。

原本の機械可読台帳: `evidence/environment/original-inputs.json`。この台帳はファイルの来歴と読取り結果を記録するもので、画質の人間承認や実行物の合格を示しません。
