# 実行環境

- 確認日: 2026-10-15 (Asia/Tokyo)
- 作業領域: C:\\dev\\n-dimensional-shogi-showcase
- 指定原資料: D:\\Users\\pipe_render\\Downloads\\n-dimensional-shogi-showcase
- 本線: C:\\dev\\n-dimensional-shogi (読取り確認のみ)
- 作業領域・その親・原資料フォルダーはreparse pointではない。作業開始時のC側は空。
- Windows 10.0.26200 x64
- .NET SDK 9.0.312
- Unity 6000.3.11f1 (3000ef702840)
- 同梱HDRP / SRP Core / Shader Graph: 17.3.0。実解決依存はUnity/Packages/packages-lock.jsonを参照。
- Windows Standaloneサポート導入済み
- NVIDIA GeForce RTX 5060 Ti / 16,311 MiB / driver 617.42 (nvidia-smi)

描画設定はHDRP Balanced、SMAA、内部解像度100%、ユーザー追加指示により目標30fps、vSyncなし、フレーム生成なし。太陽の動的PCSS影とcontact shadow、非時間蓄積SSAO、物理大気sky ambient、環境専用の静的構成プローブ、128px・6面を毎フレーム更新する局所反射プローブ、天板用512px平面反射を使用する。SSR/SSGI/RTは無効。APVや静的ライトマップを焼いておらず、完全な間接光の再計算とは説明しない。盤・駒は静的ベイクへ含めない。

ShowcaseRasterは控えめなBloomと背景DOF、GeometryReferenceは両方無効。同じ固定形状、材質場、動的影・反射を使用する。補助格子はlayer9、実体はlayer0、静的背景はlayer8。局所反射はlayer0/8を対象とし、格子の影・反射は作らない。

UnityのQualitySettingsの表示名は `High Fidelity` だが、このビルドでは全quality levelが `HDRP Balanced.asset` を共用し、ビルド準備でPCSS High・directional shadow 32-bit・最大4096px/cascade・cookie atlas4096を明示設定する。実cascade atlasは8192×8192。表示名だけから未使用のHigh Fidelity assetの設定を推測しない。

Standaloneの計測ウィンドウは通常表示を必要とする。非表示起動ではUnityの描画とCustomPassが実行されず、黒い画像とタイムアウトとなることを対照試験で確認した。その失敗記録は `evidence/runtime/candidate-pass-smoke` に残し、性能達成の証拠へ含めない。終了した同じバイナリを通常表示で起動すると描画とGPU読み戻しが成功した。

ビルド・起動・実測の結果は [実装報告](IMPLEMENTATION_REPORT_JA.md) と [性能記録](PERFORMANCE.md) を参照。Unityはdriver `32.0.16.1742` と報告する。nvidia-smiの製品表記617.42とは形式が異なる。外部WDDMカウンターの専用／共有GPUメモリは占有ゲージであり、厳密な瞬時物理常駐量と同一視しない。
