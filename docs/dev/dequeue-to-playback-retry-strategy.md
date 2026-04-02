# Dequeueから再生までのリトライ戦略

この文書は、常駐ランタイムでジョブがdequeueされてから再生が確定するまでの挙動を、先頭移動キーが単体系と複合系の場合に分けて整理したものです。

## 対象範囲

- 対象は`VoicepeakEngine.WorkerLoop`→`ExecuteJob`の区間です。
- 単発API`VoicepeakOneShot`ではなく、キュー処理時の戦略を扱います。
- 参照設定は`AppConfig.Ui.MoveToStartShortcut`と`AppConfig.Audio.StartConfirmMaxRetries`です。

## 共通フロー

1. キュー先頭をdequeueして`ExecuteJob`を開始します。
2. `PrepareSegment`で入力準備します。
   - `PrepareForTextInput`
   - `ClearInput`
   - `TypeText`
3. pre-pause待機後、再生開始処理へ進みます。
4. `startAttempt=0..StartConfirmMaxRetries`で再生開始確認ループを回します。
   - `PrepareForPlayback`
   - `PressPlay`
   - `MonitorSpeaking`
5. `MonitorSpeaking`が`Completed`ならセグメント成功です。

## リトライが発生する箇所

リトライ対象は`StartTimeout`のみです。

- `MonitorSpeaking`結果が`StartTimeout`の場合
  - まだ残り試行があれば`continue`
  - 上限到達なら`start_confirm_failed`でジョブ中断
- 試行回数は`StartConfirmMaxRetries+1`回です。
  - 例: `StartConfirmMaxRetries=0`なら1回のみ
  - 例: `StartConfirmMaxRetries=2`なら最大3回

## リトライしない失敗

次の失敗はその場でジョブ中断します。

- `PrepareForTextInput`失敗
- `ClearInput`失敗
- `TypeText`失敗
- `PrepareForPlayback`失敗
- `PressPlay`失敗
- `MonitorSpeaking`が`MaxDuration`
- `MonitorSpeaking`が`ProcessLost`
- 割り込み要求

## 先頭移動キー別の構造

### 単体系(F1-F12)

- `MoveToStart`は通常ショートカット送信(`SendShortcut`)を使います。
- `PrepareForTextInput`と`PrepareForPlayback`は、追加のprime処理を行いません。
- start-confirmの再試行時は、毎回通常ショートカットで先頭移動をやり直します。

### 複合系(Ctrl+Up)

- `MoveToStart`は複合専用経路(`SendCompositeMoveToStart`)を使います。
- クリック注入は明示設定された契機でのみ行います。
  - `ClickAtValidationEnabled`
  - `ClickBeforeTextFocusWhenUninitializedEnabled`
  - `ClickOnInputFailureRetryEnabled`
- `PrepareForTextInput`では、未primeかつ設定有効時だけ文字入力欄フォーカス直前でprimeを試みます。
- `PrepareForPlayback`では通常primeせず、キーボードフォーカス再付与とCtrl+Up送信だけを行います。
- prime状態はプロセスIDとメインHWNDの組で保持します。
- start-confirmの再試行時も`PrepareForPlayback`は毎回呼ばれます。
- フォーカス再付与とCtrl+Up送信は毎回実施されます。

## 実装上の要点

- 分岐はUI層(`VoicepeakUiController`)に閉じています。
- 実行層(`VoicepeakEngine`)はキー種別を意識せず、`PrepareFor...`と`MoveToStart`を呼ぶだけです。
- そのためリトライ戦略本体は共通で、差分は主に`PrepareFor...`内部の前処理にあります。
