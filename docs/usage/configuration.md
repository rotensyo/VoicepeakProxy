# Configuration

`VoicepeakProxyCore`は設定ファイル読込機能を持ちません。ホスト側で値を読み込み、`AppConfig`へ詰めてDLLへ渡します。

## `AppConfig`構造

- `Server: ServerConfig`
- `Audio: AudioConfig`
- `Prepare: PrepareConfig`
- `Ui: UiConfig`
- `TextTransform: TextTransformConfig`
- `Debug: DebugConfig`
- `Validation: ValidationConfig`

## `ServerConfig`

- `MaxQueuedJobs` (`default: 500`)
  - 常駐ランタイムの待機キュー上限です

## `AudioConfig`

- `PeakThreshold` (`default: 1e-9f`)
  - 発話中判定に使うピーク閾値です
- `PollIntervalMs` (`default: 50`)
  - 音声監視ポーリング間隔です
- `StartConfirmWindowMs` (`default: 1000`)
  - 再生押下後、この時間内に発話開始を検知できないと`StartConfirmTimeout`扱いになります
- `StartConfirmMaxRetries` (`default: 0`)
  - `StartConfirmWindowMs`超過時の再生再試行回数です
  - 再試行時は`MoveToStart`→`PressPlay`→開始確認をやり直します
- `StopConfirmMs` (`default: 300`)
  - 発話開始後、この時間だけ閾値未満が続いたら終了と判定します
- `MaxSpeakingDurationSec` (`default: 300`)
  - 発話開始後、この秒数を超えても終了しない場合はエラーとします

## `PrepareConfig`

- `BootValidationText` (`default: "初期化完了"`)
  - 起動時バリデーションで入力/再生する文字列です
  - 空文字なら発話監視を省略します
- `BootValidationMaxRetries` (`default: 2`)
  - 起動時の入力検証失敗時の再試行回数です
- `BootValidationRetryIntervalMs` (`default: 1000`)
  - 起動時入力検証の再試行待機時間です
- `CharDelayBaseMs` (`default: 1`)
  - 文字入力時の1文字ごとのディレイです
- `ActionDelayMs` (`default: 5`)
  - UIアクション時の待機です
- `PostTypeWaitPerCharMs` (`default: 4`)
  - 文字入力後の待機時間算出に使う倍率です
- `PostTypeWaitMinMs` (`default: 100`)
  - 文字入力後待機の最小値です

## `UiConfig`

- `MoveToStartShortcut` (`default: "F3"`)
- `MoveToEndShortcut` (`default: "F4"`)
- `PlayShortcut` (`default: "Space"`)
- `PlayPreShortcutDelayMs` (`default: 60`)
- `SendEnterAfterSentenceBreak` (`default: false`)
- `SentenceBreakTriggers` (`default: ["。", "！", "？", "!", "?"]`)

重要です。

- `MoveToStartShortcut`/`MoveToEndShortcut`は`F1-F12`のいずれかに設定してください
- `SentenceBreakTriggers`は複数文字指定に対応し、最長一致を優先します

## `TextTransformConfig`

- `ReplaceRules`
  - 上から順に1回ずつ適用します
  - `[[pause:NNN]]`には適用しません

例:

```csharp
config.TextTransform.ReplaceRules.Add(new ReplaceRule
{
    From = "。",
    To = "。、。"
});
```

## `ValidationConfig`

- `BootValidation` (`default: Required`)
- `RequestValidation` (`default: Strict`)

### `BootValidationMode`

- `Required`
  - 起動時バリデーション失敗で`Start(...)`が例外になります
- `Optional`
  - 起動時バリデーション失敗でも起動継続します
- `Disabled`
  - 起動時バリデーションを実行しません

### `RequestValidationMode`

- `Strict`
  - 入力不正をエラー扱いします
- `Lenient`
  - 一部補正しながら受理します
- `Disabled`
  - 最低限の整形のみ行います

常駐ランタイムと単発実行の両方で`config.Validation.RequestValidation`を使用します。

## 設定バリデーション

起動時に`AppConfigValidator`で主要な設定不正を検出します。

主な検証内容です。

- `config`と各セクションが`null`でないこと
- 数値設定が許容範囲にあること
- ショートカット文字列が有効形式であること
- `SentenceBreakTriggers`が`null`でなく、各要素が空文字でないこと
- `TextTransform.ReplaceRules`が`null`でないこと

サポートしている主なショートカット形式です。

- `F3`
- `Ctrl+F4`
- `Shift+Space`
- `Home`
- `End`

`Delete`や`Enter`はショートカット設定値としてサポートしていません。

## 例: C#で直接設定

```csharp
var config = new AppConfig
{
    Prepare = new PrepareConfig
    {
        BootValidationText = "初期化完了",
        CharDelayBaseMs = 1,
        ActionDelayMs = 5,
        PostTypeWaitPerCharMs = 4,
        PostTypeWaitMinMs = 100
    },
    Ui = new UiConfig
    {
        MoveToStartShortcut = "F3",
        MoveToEndShortcut = "F4",
        PlayShortcut = "Space",
        PlayPreShortcutDelayMs = 60,
        SendEnterAfterSentenceBreak = true,
        SentenceBreakTriggers = new System.Collections.Generic.List<string> { "。", "。、。" }
    }
};
```
