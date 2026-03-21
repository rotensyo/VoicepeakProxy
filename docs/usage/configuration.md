# Configuration

`VoicepeakProxyCore`は設定ファイル読込機能を持ちません。ホスト側で値を読み込み、`AppConfig`に固めてDLLへ渡す想定です。

## AppConfig 構造

- `Server: ServerConfig`
- `Audio: AudioConfig`
- `Prepare: PrepareConfig`
- `Ui: UiConfig`
- `TextTransform: TextTransformConfig`
- `Debug: DebugConfig`
- `Validation: ValidationConfig`

## ServerConfig

- `MaxQueuedJobs` (`default: 500`)
  - 常駐ランタイムの待機キュー上限です

## AudioConfig

- `PeakThreshold` (`default: 1e-9f`)
  - 発話中判定に使う音量の閾値です
- `PollIntervalMs` (`default: 50`)
  - 音声監視を行う間隔です
- `StartConfirmTimeoutMs` (`default: 1000`)
  - 再生押下後、この時間内に発話開始を検知できないと失敗とし、`StartConfirmTimeout`扱いにします
- `StartConfirmMaxRetries` (`default: 0`)
  - `StartConfirmTimeoutMs`超過時の再試行回数です
  - 再試行時は`MoveToStart`→`PressPlay`→開始確認をやり直します
  - `VoicepeakOneShot.SpeakOnce`では再試行せず、`StartConfirmTimeoutMs`超過で即`StartConfirmTimeout`になります
- `StopConfirmMs` (`default: 300`)
  - 発話開始後、この時間だけ閾値未満が続いたら終了と判定します
- `MaxSpeakingDurationSec` (`default: 300`)
  - 発話開始後、この秒数を超えても終了しない場合はエラーとします

## PrepareConfig

- `BootValidationText` (`default: "初期化完了"`)
  - 起動時バリデーションで入力/再生する文字列です
  - 空文字なら発話監視を省略します
- `BootValidationMaxRetries` (`default: 2`)
  - 起動時の入力検証失敗時の再試行回数です
- `BootValidationRetryIntervalMs` (`default: 1000`)
  - 起動時入力検証の再試行待機時間です
- `CharDelayBaseMs` (`default: 1`)
  - 文字入力時の1文字ごとのディレイです
  - より高速化したい場合は0にすると最速で入力されます
- `ActionDelayMs` (`default: 5`)
  - UIアクション時の待機時間です
- `PostTypeWaitPerCharMs` (`default: 4`)
  - 文字入力後の待機時間算出に使う倍率です
  - 文字入力まで完了して再生に失敗する場合は値を増やし、待機時間を伸ばしてみてください
- `PostTypeWaitMinMs` (`default: 100`)
  - 文字入力後待機時間の最小値です
  - 短文で文字入力まで完了して再生に失敗する場合は値を増やし、待機時間を伸ばしてみてください
- `SequentialMoveToStartKeyDelayBaseMs` (`default: 5`)
  - 逐次`PageUp`→`Up`経路でのキー間待機です
- `DeleteKeyDelayBaseMs` (`default: 1`)
  - `Delete`1回ごとの待機です
  - より高速化したい場合は0にすると最速でDeleteされます
- `ClearInputMaxPasses` (`default: 20`)
  - 入力クリア処理の最大試行回数です

## UiConfig

- `MoveToStartShortcut` (`default: "Ctrl+Up"`)
  - 先頭移動のショートカットです
  - VOICEPEAKの設定値と同じものを指定してください
  - F1-F12のいずれかを指定した場合、より高速かつ安定な方式で実行されます
- `PlayShortcut` (`default: "Space"`)
  - 再生ショートカットです
  - VOICEPEAKの設定値と同じものを指定してください
- `DelayBeforePlayShortcutMs` (`default: 60`)
  - 再生ボタンを押す前の待機時間です
- `ClickAtValidationEnabled` (`default: true`)
  - 起動時バリデーションで初期化のためのウィンドウフォーカス奪取と入力欄クリックを許可します
  - `MoveToStartShortcut`が`F1-F12`以外の場合だけ使用されます
- `ClickBeforeTextFocusWhenUninitializedEnabled` (`default: false`)
  - 初期化クリックが未実行の場合に文字入力欄フォーカス直前のウィンドウフォーカス奪取とクリックを許可します
  - `MoveToStartShortcut`が`F1-F12`以外の場合だけ使用されます
  - `VoicepeakOneShot.SpeakOnce`/`VoicepeakOneShot.SpeakOnceWait`では使用されません
- `ClickOnStartTimeoutRetryEnabled` (`default: false`)
  - `StartTimeout`時に一度だけウィンドウフォーカス奪取とクリックを許可します
  - `MoveToStartShortcut`が`F1-F12`以外の場合だけ使用されます
- `SendEnterAfterSentenceBreak` (`default: false`)
  - trueに指定すると、`SentenceBreakTriggers`で指定した文字列の後にEnterを入れ、VOICEPEAKの入力ブロックを分割します。
- `SentenceBreakTriggers` (`default: ["。", "！", "？", "!", "?"]`)
  - `SendEnterAfterSentenceBreak`での分割対象となる文字列です。
  - 区切り文字は1文字である必要はなく、例えば`replaceRules`で"。"を"。　。"に置換する場合は"。　。"を分割対象として指定できます。
  - "!?"のように分割対象文字列が連続した場合、まとめて1つの区切り文字とみなします。
  - 分割文字列は最長のものを優先します。


## TextTransformConfig

- `ReplaceRules`
  - 上から順に1回ずつ適用します
  - `[[pause:NNN]]`には適用しません

### `ReplaceRule`

- `From`
  - 置換対象文字列です
- `To`
  - 置換後文字列です

例:

```csharp
config.TextTransform.ReplaceRules.Add(new ReplaceRule
{
    From = "。",
    To = "。　。"
});
```

## DebugConfig

- `LogTextCandidates` (`default: false`)
  - 入力欄候補収集の詳細ログを出力します
  - `ReadInputTextDetailed(...)`で候補一覧と推定情報のログを有効化します

## ValidationConfig

- `BootValidation` (`default: Required`)
- `RequestValidation` (`default: Strict`)

### BootValidationMode

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

## configバリデーション

起動時に下記のような主要な設定不正を検出します。

- `config`と各セクションが`null`でないこと
- 数値設定が許容範囲にあること
- `MoveToStartShortcut`がnull/空文字/空白でないこと
- `PlayShortcut`が有効形式であること
- `SentenceBreakTriggers`が`null`でなく、各要素が空文字でないこと
- `TextTransform.ReplaceRules`が`null`でないこと

`PlayShortcut`でサポートしている主な形式です。

- `F3`
- `Ctrl+F4`
- `Shift+Space`
- `Home`
- `End`

`Delete`や`Enter`は`PlayShortcut`設定値としてサポートしていません。

## 使用例: C#で直接設定

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
        MoveToStartShortcut = "Ctrl+Up",
        PlayShortcut = "Space",
        DelayBeforePlayShortcutMs = 60,
        ClickAtValidationEnabled = true,
        ClickBeforeTextFocusWhenUninitializedEnabled = false,
        ClickOnStartTimeoutRetryEnabled = false,
        SendEnterAfterSentenceBreak = true,
        SentenceBreakTriggers = new System.Collections.Generic.List<string> { "。", "。、。" }
    }
};
```
