# API Reference

`VoicepeakProxyCore`の公開APIと入出力モデルの詳細です。

## 常駐ランタイム API

### `VoicepeakRuntime.Start(AppConfig config, IAppLogger logger = null)`

- 設定検証後にランタイムを起動します
- `config.Validation.BootValidation`に従って起動時バリデーションを行います
- `BootValidationMode.Required`で失敗した場合は例外になります
- `BootValidationMode.Optional`では、`IsShutdownRequested == true`の状態で起動される場合があります

### `VoicepeakRuntime.Enqueue(SpeakRequest request)`

- 発話要求を非同期で受理します
- 戻り値は`EnqueueResult`
- `request == null`は`ArgumentNullException`
- 停止中は`InvalidOperationException`
- 破棄後は`ObjectDisposedException`

### `VoicepeakRuntime.Stop()`

- 新規受理を停止します
- ランタイムへ停止要求を行います

### `VoicepeakRuntime.Dispose()`

- ランタイムを破棄します
- 内部ワーカーと関連リソースを解放します

### `VoicepeakRuntime.IsShutdownRequested`

- 内部で停止要求が発生しているかを返します
- 例:`voicepeak.exe`喪失、`BootValidationMode.Optional`での起動時検証失敗

## 単発実行 API

### `VoicepeakOneShot.SpeakOnceWait(AppConfig config, SpeakOnceRequest request, IAppLogger logger = null)`

- 1ジョブだけ同期実行します
- ワーカーループは起動しません
- 起動時バリデーションは実行しません
- `config == null`は`ArgumentNullException`
- `request == null`は`SpeakOnceStatus.InvalidRequest`として返します

## 入力モデル

### `SpeakRequest`

- `Text: string`
- `Mode: EnqueueMode`
  - `Queue`
  - `Next`
  - `Flush`
- `Interrupt: bool`

`Interrupt`は`Next`/`Flush`時の割り込み要求に使います。`Queue`では内部で無効化されます。

### `SpeakOnceRequest`

- `Text: string`

単発実行には`Mode`と`Interrupt`はありません。

## 入力文字列の記法

### `[[pause:NNN]]`

- `NNN`はミリ秒整数です
- 負値は`0`扱いです
- `[[pause:NNN]]`自体には文字列置換を適用しません

例:

```text
こんにちは[[pause:3000]]お待たせしました
```

## 戻り値モデル

### `EnqueueResult`

- `Status: EnqueueStatus`
  - `Accepted`
  - `QueueFull`
  - `InvalidRequest`
- `JobId: string`
  - `Accepted`時に設定されます
- `ErrorMessage: string`
  - `QueueFull`/`InvalidRequest`時に設定されます
- `Succeeded: bool`

### `SpeakOnceResult`

- `Status: SpeakOnceStatus`
- `SegmentsExecuted: int`
- `ErrorMessage: string`
- `Succeeded: bool`

主な`SpeakOnceStatus`です。

- `Completed`
- `InvalidRequest`
- `ProcessNotFound`
- `MultipleProcesses`
- `TargetNotFound`
- `PrepareFailed`
- `MoveToStartFailed`
- `PlayFailed`
- `StartConfirmTimeout`
- `MaxSpeakingDurationExceeded`
- `ProcessLost`

## ログ出力インターフェース

```csharp
public interface IAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
```

`logger == null`の場合は`ConsoleAppLogger`を使用します。
