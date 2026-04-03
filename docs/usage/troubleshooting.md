# Troubleshooting

## 主なログキー

- `boot_start`
- `boot_validation_ok`
- `boot_validation_fail`
- `boot_validation_failed`
- `boot_validation_retry_failed`
- `boot_start_confirm_retry`
- `boot_validation_skip_speech`
- `job_received`
- `segment_start`
- `play_pressed`
- `speak_start_confirmed`
- `speak_end_confirmed`
- `start_confirm_retry`
- `monitor_timeout`
- `job_dropped`
- `interrupt_applied`
- `runtime_started`
- `runtime_stopping`
- `runtime_disposed`

## `Boot validation failed.`

確認項目です。

- `voicepeak.exe`が起動しているか
- ショートカット設定が`AppConfig.Ui`と一致しているか
- `BootValidationText`が適切か
- 入力欄の読取に失敗していないか

## `Runtime is stopping and cannot accept new requests.`

- `Stop()`後または停止中に`Enqueue(...)`を呼んでいます
- 呼び出し側で再生成前提にするか、例外を処理してください

## `QueueFull`

- 待機キューが`Server.MaxQueuedJobs`を超えています
- `Server.MaxQueuedJobs`を増やすか、呼び出し側で投入レートを制御してください

## `monitor_timeout reason=start_confirm`

- 再生開始がピーク監視で確認できていません
- `Audio.PeakThreshold`を見直してください
- `Audio.StartConfirmTimeoutMs`を見直してください
- 必要に応じて`Audio.StartConfirmMaxRetries`を増やしてください
- 旧クリックprime経路は既定で無効です(`Deprecated.EnableLegacyPrimeInputClick=false`)

## `monitor_timeout reason=max_duration`

- 発話開始後、`Audio.MaxSpeakingDurationSec`を超えても終了が確認できていません
- `Audio.PeakThreshold`や`Audio.StopConfirmMs`を見直してください
- VOICEPEAK側で音声セッションが残り続けていないか確認してください

## ショートカット形式エラー

- `MoveToStartModifier`は空文字/`ctrl`/`alt`のみ指定できます
- `MoveToStartKey`は`cursor up/cursor down/cursor left/cursor right`, `F1-F12`, `space`, `home`, `end`を指定できます
- クリック注入は`Ui.Composite...`設定で明示制御します
- それ以外のショートカットは`Space`, `Home`, `End`と`Ctrl`/`Shift`/`Alt`の組み合わせを使用してください
- `Delete`や`Enter`は設定値としてサポートしていません
