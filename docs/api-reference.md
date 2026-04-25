# VoicepeakProxyCore API仕様
`VoicepeakProxyCore.dll`の公開APIと入出力モデルの詳細です。

## 常駐ランタイム API
起動し続ける間、内部のキューに蓄積されていく発話要求を順に実行していく機能です。

### `VoicepeakRuntime.Start(AppConfig config, IAppLogger logger = null)`
- 起動検証後に常駐ランタイムを起動します
- `config.Runtime.BootValidation` に従って起動時バリデーションを行います
- `BootValidationMode.Required` で失敗した場合は例外になります
- `BootValidationMode.Optional` では、`IsShutdownRequested == true` の状態で起動される場合があります

### `VoicepeakRuntime.Enqueue(SpeakRequest request)`
- 発話要求を非同期で受理します
- 戻り値は `EnqueueResult`
- `request == null` は `ArgumentNullException`
- 停止中は `InvalidOperationException`
- 破棄後は `ObjectDisposedException`

### `VoicepeakRuntime.Stop()`
- 新規受理を停止します
- ランタイムへ停止要求を行います

### `VoicepeakRuntime.Dispose()`
- ランタイムを破棄します
- 内部ワーカーと関連リソースを解放します

### `VoicepeakRuntime.IsShutdownRequested`
- 内部で停止要求が発生しているかを返します
- 例: `voicepeak.exe` 喪失、 `BootValidationMode.Optional` での起動時検証失敗

## 単発実行 API
キューを持たず、セッション内で単発操作を実行する機能です。

### `VoicepeakOneShot.Start(AppConfig config, IAppLogger logger = null)`
- 単発実行セッション(`VoicepeakOneShotSession`)を開始します
- `config == null` は `ArgumentNullException`
- セッション生成時にUIAサブプロセス管理を初期化します

### `VoicepeakOneShotSession.Dispose()`
- 単発実行セッションを終了します
- UIAサブプロセスと関連リソースを解放します

### `VoicepeakOneShotSession.SpeakOnce(SpeakOnceRequest request)`
- 発話を1ジョブだけ同期実行します
- 再生開始を確認できた時点で完了となります
- `request == null` は `SpeakOnceStatus.InvalidRequest` として返します

### `VoicepeakOneShotSession.SpeakOnceWait(SpeakOnceRequest request)`
- 発話を1ジョブだけ同期実行します
- 再生終了まで待機してから完了となります
- `request == null` は `SpeakOnceStatus.InvalidRequest` として返します

### `VoicepeakOneShotSession.ValidateInputOnce()`
- 入力検証と発話確認を1回だけ同期実行します
- 検証文字列は`config.Validation.ValidationText`を使用します

### `VoicepeakOneShotSession.ClearInputOnce()`
- 入力欄のクリアだけを1回同期実行します

## 入力モデル

### `SpeakRequest`
```
public sealed class SpeakRequest
{
    public string Text { get; set; } = string.Empty;
    public EnqueueMode Mode { get; set; } = EnqueueMode.Queue;
    public bool Interrupt { get; set; }
}

public enum EnqueueMode
{
    Queue,
    Next,
    Flush
}
```
- `EnqueueMode` は キューの挿入方法を指定します
  - `Queue`: 最後尾に追加
  - `Next`: 先頭に追加
  - `Flush`: 先頭に追加し後続の既存キューを全て削除
- `Interrupt` は `Next` / `Flush` 時の割り込み要求に使い、`Queue` では内部で無効化されます
  - `true`: 現在発話中の内容を中断して実行
  - `false`: 現在発話中の内容が完了してから実行

### `SpeakOnceRequest`
```
public sealed class SpeakOnceRequest
{
    public string Text { get; set; }
}
```
単発実行には`Mode`と`Interrupt`はありません。

## 入力文字列の記法

### `[[pause:NNN]]`
`[[pause:NNN]]`を文字列内に組み込むことで、発話間の待機時間を指定することができます。

- `NNN`はミリ秒整数です
- 負値は`0`扱いになります
- 指定した時間が文字削除・入力等の時間より短い場合、完了次第即発話を開始します
  - 読み上げ完了から次の開始まで数秒程度はかかるので、500ms程度を指定しても意味がない場合が多いです
- `[[pause:NNN]]`自体には文字列置換は適用されません
- `VoicepeakOneShot.SpeakOnce`では`[[pause:NNN]]`は無視され、1回の再生として扱います
- `VoicepeakOneShot.SpeakOnceWait`と常駐実行では従来通りpauseとして解釈します

例:
```text
こんにちは[[pause:3000]]お待たせしました
```

## 戻り値モデル

### `EnqueueResult`

```csharp
public enum EnqueueStatus
{
    Accepted,
    QueueFull,
    InvalidRequest
}

public sealed class EnqueueResult
{
    public EnqueueStatus Status { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == EnqueueStatus.Accepted;
}
```

- `JobId`は`Status == Accepted`時に設定されます
- `ErrorMessage`は`Status == QueueFull`または`Status == InvalidRequest`時に設定されます

### `SpeakOnceResult`

```csharp
public enum SpeakOnceStatus
{
    Completed,
    InvalidRequest,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    PrepareFailed,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxSpeakingDurationExceeded,
    ProcessLost
}

public sealed class SpeakOnceResult
{
    public SpeakOnceStatus Status { get; set; }
    public int SegmentsExecuted { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == SpeakOnceStatus.Completed;
}
```

### `ValidateInputOnceResult`

```csharp
public enum ValidateInputOnceStatus
{
    Completed,
    InvalidRequest,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    PrepareFailed,
    MoveToStartFailed,
    PlayFailed,
    StartConfirmTimeout,
    MaxSpeakingDurationExceeded,
    ClearInputFailed,
    TypeTextFailed,
    ReadInputFailed,
    TextMismatch,
    ProcessLost
}

public sealed class ValidateInputOnceResult
{
    public ValidateInputOnceStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string ActualText { get; set; } = string.Empty;
    public bool Succeeded => Status == ValidateInputOnceStatus.Completed;
}
```

### `ClearInputOnceResult`

```csharp
public enum ClearInputOnceStatus
{
    Completed,
    ProcessNotFound,
    MultipleProcesses,
    TargetNotFound,
    ClearInputFailed,
    ProcessLost
}

public sealed class ClearInputOnceResult
{
    public ClearInputOnceStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == ClearInputOnceStatus.Completed;
}
```

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
