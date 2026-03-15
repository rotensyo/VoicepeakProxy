# UIA文字入力候補契約

このドキュメントは`VoicepeakUiController`の`CollectTextCandidates(...)`で採用している候補抽出契約を記録します。

## 背景

- 可視入力欄探索について実機のヒューリスティクス調査を行い通常ケースでは`CollectTextCandidates(...)`で画面内入力欄を過不足なく取得できることを確認済みです
- その結果を契約として固定するため候補判定は単純な条件に統一しています

## 現行契約

`TryBuildTextCandidate(...)`は次の条件を満たす要素のみ候補化します。

- `element != null`
- `controlType in (Edit, Document, Text)`
- `name == ""`

実装箇所:

- `VoicepeakUiController.IsCollectTextCandidateTarget(...)`
- `VoicepeakUiController.TryBuildTextCandidate(...)`

## 運用メモ

- 完全削除判定や削除ステップ計算はこの候補集合を前提にしています
- 将来VOICEPEAK側のUIAメタデータが変化した場合はこの契約を最初に見直してください
