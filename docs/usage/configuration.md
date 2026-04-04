# Configuration

`VoicepeakProxyCore`は設定ファイル読込機能を持ちません。ホスト側で値を読み込み、`AppConfig`に固めてDLLへ渡す想定です。

## AppConfig 構造

- `Startup: StartupConfig`
- `Hook: HookConfig`
- `Ui: UiConfig`
- `InputTiming: InputTimingConfig`
- `Audio: AudioConfig`
- `Text: TextConfig`
- `Queue: QueueConfig`
- `Validation: ValidationConfig`
- `Debug: DebugConfig`

## StartupConfig

- `BootValidationText` (`default: "初期化完了"`)
  - 起動時バリデーションで入力/再生する文字列です
  - 空文字なら発話監視を省略します
- `BootValidationMaxRetries` (`default: 2`)
  - 起動時の入力検証失敗時の再試行回数です
- `BootValidationRetryIntervalMs` (`default: 1000`)
  - 起動時入力検証の再試行待機時間です
- クリックprime関連設定は`DeprecatedConfig`へ退避されています
- 既定では`Deprecated.EnableLegacyPrimeInputClick=false`のため実行されません

## HookConfig

- `HookCommandTimeoutMs` (`default: 500`)
  - 修飾キー中立化フックへのコマンド送信タイムアウトです
- `HookConnectTimeoutMs` (`default: 300`)
  - 修飾キー中立化フックの1回接続試行タイムアウトです
- `HookConnectTotalWaitMs` (`default: 8000`)
  - 修飾キー中立化フックの接続待機総時間です

## UiConfig

- `MoveToStartModifier` (`default: "ctrl"`)
  - 先頭移動の修飾子キーです
  - 空文字または`ctrl`または`alt`を指定してください(`shift`は指定できません)
- `MoveToStartKey` (`default: "cursor up"`)
  - 先頭移動キーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は`cursor up`, `cursor down`, `cursor left`, `cursor right`, `F1-F12`, `spacebar`, `home`, `end`, `a-z`, `0-9`, 記号キー(`@`, `-`, `[`, `]`など)です
- `PlayShortcutModifier` (`default: ""`)
  - 再生ショートカットの修飾子キーです
  - 空文字または`ctrl`または`alt`または`shift`を指定してください
- `PlayShortcutKey` (`default: "spacebar"`)
  - 再生ショートカットのキーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は`cursor up`, `cursor down`, `cursor left`, `cursor right`, `F1-F12`, `spacebar`, `home`, `end`, `a-z`, `0-9`, 記号キー(`@`, `-`, `[`, `]`など)です
- `DelayBeforePlayShortcutMs` (`default: 60`)
  - 再生ボタンを押す前の待機時間です
- 入力失敗時クリック設定は`Deprecated.LegacyPrimeClickOnInputFailureRetryEnabled`へ退避されています
- 既定では`Deprecated.EnableLegacyPrimeInputClick=false`のため実行されません

## InputTimingConfig

- `CharDelayBaseMs` (`default: 0`)
  - 文字入力時の1文字ごとのディレイです
  - より高速化したい場合は`0`で最速入力になります
- `DeleteKeyDelayBaseMs` (`default: 0`)
  - `Delete`1回ごとの待機です
  - より高速化したい場合は`0`で最速削除になります
- `ActionDelayMs` (`default: 5`)
  - 文字入力欄フォーカスなどのUIアクション時の待機時間です
- `SequentialMoveToStartKeyDelayBaseMs` (`default: 5`)
  - 互換で残している逐次`PageUp`→`Up`経路でのキー間待機です
  - 通常の先頭移動処理では使用されません
- `PostTypeWaitPerCharMs` (`default: 5`)
  - 文字入力後の待機時間算出に使う倍率です
  - 文字入力完了後に再生失敗する場合は値を増やして待機を伸ばしてください
- `PostTypeWaitMinMs` (`default: 300`)
  - 文字入力後待機時間の最小値です
  - 短文で再生失敗する場合は値を増やして待機を伸ばしてください
- `ClearInputMaxPasses` (`default: 10`)
  - 入力クリア処理の最大試行回数です

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
  - `VoicepeakOneShot.SpeakOnce`では再試行せず、超過で即`StartConfirmTimeout`になります
- `StopConfirmMs` (`default: 300`)
  - 発話開始後、この時間だけ閾値未満が続いたら終了と判定します
- `MaxSpeakingDurationSec` (`default: 300`)
  - 発話開始後、この秒数を超えても終了しない場合はエラーとします

## TextConfig

- `SendEnterAfterSentenceBreak` (`default: false`)
  - `true`で、`SentenceBreakTriggers`一致位置の後に`Enter`を挿入し入力ブロックを分割します
- `SentenceBreakTriggers` (`default: ["。", "！", "？", "!", "?"]`)
  - `SendEnterAfterSentenceBreak`での分割対象文字列です
  - 区切り文字は1文字である必要はありません
  - 連続した分割対象はまとめて1つの区切りとして扱います
  - 分割文字列は最長一致を優先します
- `ReplaceRules`
  - 上から順に1回ずつ適用します
  - `[[pause:NNN]]`には適用しません

### ReplaceRule

- `From`
  - 置換対象文字列です
- `To`
  - 置換後文字列です

## QueueConfig

- `MaxQueuedJobs` (`default: 500`)
  - 常駐ランタイムの待機キュー上限です

## ValidationConfig

- `BootValidation` (`default: Required`)
  - 起動時バリデーション失敗をどう扱うかを指定します(`Required`/`Optional`/`Disabled`)

## DebugConfig

- `LogTextCandidates` (`default: false`)
  - 入力欄候補収集の詳細ログを出力します
  - `ReadInputTextDetailed(...)`の候補一覧と推定情報ログを有効化します
- `LogModifierHookStats` (`default: false`)
  - 修飾キー中立化フックの統計ログを出力します
  - `modifier_hook_stats_probe_*`と`modifier_hook_stats`を有効化します

## DeprecatedConfig

- `EnableLegacyPrimeInputClick` (`default: false`)
  - 旧クリックprime経路を有効化します
  - 既定は無効です
- `LegacyPrimeClickAtValidationEnabled` (`default: true`)
  - 旧経路有効時のみ、起動時バリデーションでクリックprimeを許可します
- `LegacyPrimeClickBeforeTextFocusWhenUninitializedEnabled` (`default: false`)
  - 旧経路有効時のみ、入力前未初期化時クリックprimeを許可します
- `LegacyPrimeClickOnInputFailureRetryEnabled` (`default: false`)
  - 旧経路有効時のみ、入力失敗時リトライクリックprimeを許可します

## configバリデーション

起動時に下記の主要な設定不正を検出します。

- `config`と各セクションが`null`でないこと
- 数値設定が許容範囲にあること
- `MoveToStartModifier`が空文字/`ctrl`/`alt`のいずれかであること
- `MoveToStartKey`が有効なキー形式であること
- `PlayShortcutModifier`が空文字/`ctrl`/`alt`/`shift`のいずれかであること
- `PlayShortcutKey`が有効なキー形式であること
- `SentenceBreakTriggers`が`null`でなく、各要素が空文字でないこと
- `ReplaceRules`が`null`でないこと

`PlayShortcutKey`でサポートしている主な形式です。

- `F3`
- `Spacebar`
- `Home`
- `End`
- `A`
- `0`
- `@`

`Delete`や`Enter`は`PlayShortcutKey`設定値としてサポートしていません。
