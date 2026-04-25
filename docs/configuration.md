# VoicepeakProxyCore 設定値

`VoicepeakProxyCore` は設定ファイル読込機能を持ちません。ホスト側で値を読み込み、 `AppConfig` に固めてDLLへ渡します。

## AppConfig 構造
- `Validation: ValidationConfig`
- `Ui: UiConfig`
- `InputTiming: InputTimingConfig`
- `Text: TextConfig`
- `Audio: AudioConfig`
- `Runtime: RuntimeConfig`
- `Hook: HookConfig`
- `Debug: DebugConfig`

## ValidationConfig
- `ValidationText` (default: `初期化完了`)
  - 入力検証で入力/再生する文字列です
  - 空文字なら発話監視を省略します
- `ValidationMaxRetries` (default: `2`)
  - 入力検証失敗時の再試行回数です
- `ValidationRetryIntervalMs` (default: `1000`)
  - 入力検証の再試行待機時間(ミリ秒)です

## UiConfig
- `MoveToStartModifier` (default: `ctrl`)
  - 「先頭に移動」ショートカットの修飾子キーです
  - 空文字または `ctrl` または `alt` を指定してください
  - `shift` は入力欄内で文字入力と誤認されるため指定できません
- `MoveToStartKey` (default: `cursor up`)
  - 「先頭に移動」ショートカットのキーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は `cursor up`, `cursor down`, `cursor left`, `cursor right`, `F1-F12`, `spacebar`, `home`, `end`, `a-z`, `0-9`, 記号キー(`@`, `-`, `[`, `]`など)です
- `ClearInputSelectAllModifier` (default: `ctrl`)
  - 「すべてを選択」ショートカットの修飾子キーです
  - 空文字または `ctrl` または `alt` を指定してください
  - `shift` は入力欄内で文字入力と誤認されるため指定できません
- `ClearInputSelectAllKey` (default: `a`)
  - 「すべてを選択」ショートカットのキーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は`a-z`, `0-9`, 記号キーです
- `PasteShortcutModifier` (default: `ctrl`)
  - 「ペースト」ショートカットの修飾子キーです
  - 空文字または `ctrl` または `alt` を指定してください
  - `shift` は入力欄内で文字入力と誤認されるため指定できません
- `PasteShortcutKey` (default: `v`)
  - 「ペースト」ショートカットのキーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は`a-z`, `0-9`, 記号キーです
- `PlayShortcutModifier` (default: `""`)
  - 「再生/停止」ショートカットの修飾子キーです
  - 空文字または `ctrl` または `alt` または `shift` を指定してください
- `PlayShortcutKey` (default: `spacebar`)
  - 「再生/停止」ショートカットのキーです
  - VOICEPEAKの設定値と同じものを指定してください
  - 主な指定値は `cursor up`, `cursor down`, `cursor left`, `cursor right`, `F1-F12`, `spacebar`, `home`, `end`, `a-z`, `0-9`, 記号キー(`@`, `-`, `[`, `]`など)です
  - `Delete` や `Enter` は文字入力操作と誤認されるため指定できません。
- `DelayBeforePlayShortcutMs` (default: `20`)
  - 再生ショートカットを実行する前の待機時間(ミリ秒)です

## InputTimingConfig
- `ActionDelayMs` (default: `5`)
  - 文字入力欄フォーカスなどのUIアクション時の待機時間(ミリ秒)です
- `PostTypeWaitPerCharMs` (default: `5`)
  - 文字入力後の待機時間算出に使う倍率(ミリ秒)です
  - 入力文字列の中で最も長い1文の長さに対してこの倍率を掛け、その時間だけ待機することで発話準備完了前に再生が実行されるのを防ぎます。
  - 文字入力完了後に再生失敗する場合は値を増やして待機を伸ばしてください
- `PostTypeWaitMinMs` (default: `300`)
  - 文字入力後待機時間の最小値(ミリ秒)です
  - 短文であっても、再生前にこの時間は最低限待機します。
  - 短文の場合にのみ再生失敗する場合は値を増やして待機を伸ばしてください
- `TypeTextRetryWaitMs` (default: `1000`)
  - 文字入力失敗時の再試行前待機時間(ミリ秒)です
- `TypeTextRetryMaxRetries` (default: `2`)
  - 文字入力失敗時の再試行回数です
- `ClearInputRetryWaitMs` (default: `1000`)
  - 入力クリア失敗時の再試行前待機時間(ミリ秒)です
- `ClearInputRetryMaxRetries` (default: `2`)
  - 入力クリア失敗時の再試行回数です
- `ClearInputMaxPasses` (default: `10`)
  - 入力クリア処理の最大実行回数です
  - 1パスでは可視入力ブロック数分だけ`全選択`->`Delete`->`Delete`を実行します
  - 文字列が消しきれない場合はこの数を増やしてみてください。

## TextConfig
- `ReplaceRules`
  - 上から順に1回ずつ適用します
  - `[[pause:NNN]]` には適用しません
  - 設定形式は`ReplaceRule` の配列です

```yaml
text:
  replaceRules:
    - from: "（"
      to: "("
    - from: "）"
      to: ")"
```

```csharp
AppConfig config = new AppConfig
{
    Text =
    {
        ReplaceRules =
        {
            new ReplaceRule { From = "（", To = "(" },
            new ReplaceRule { From = "）", To = ")" }
        }
    }
};
```

## AudioConfig
- `PeakThreshold` (default: `1e-9f`)
  - 発話中判定に使う音量の閾値です
- `PollIntervalMs` (default: `50`)
  - 音声監視を行う間隔(ミリ秒)です
- `StartConfirmTimeoutMs` (default: `1000`)
  - 再生押下後、この時間(ミリ秒)内に発話開始を検知できないと失敗とし、`StartConfirmTimeout`扱いにします
- `StartConfirmMaxRetries` (default: `2`)
  - `StartConfirmTimeoutMs` 超過時の再試行回数です
  - 再試行時は `MoveToStart` → `PressPlay` → 開始確認 をやり直します
  - `VoicepeakOneShot.SpeakOnce` では再試行せず、超過で即 `StartConfirmTimeout` になります
- `StopConfirmMs` (default: `200`)
  - 発話開始後、この時間(ミリ秒)だけ閾値未満が続いたら終了と判定します
- `MaxSpeakingDurationSec` (default: `300`)
  - 発話開始後、この秒数を超えても終了しない場合はエラーとします

## RuntimeConfig
- `MaxQueuedJobs` (default: `500`)
  - 常駐ランタイムの待機キュー上限です
- `BootValidation` (default: `Required`)
  - 起動時バリデーション失敗をどう扱うかを指定します(`Required`/`Optional`/`Disabled`)

## HookConfig
- `HookCommandTimeoutMs` (default: `500`)
  - 修飾キー中立化フックへのコマンド送信タイムアウト(ミリ秒)です
- `HookConnectTimeoutMs` (default: `300`)
  - 修飾キー中立化フックの1回接続試行タイムアウト(ミリ秒)です
- `HookConnectTotalWaitMs` (default: `8000`)
  - 修飾キー中立化フックの接続待機総時間(ミリ秒)です

## DebugConfig
- `LogTextCandidates` (default: `false`)
  - 入力欄候補収集の詳細ログを出力します
  - `ReadInputTextDetailed(...)` の候補一覧と推定情報ログを有効化します
- `LogModifierHookStats` (default: `false`)
  - 修飾キー中立化フックの統計ログを出力します
  - `modifier_hook_stats_probe_*` と `modifier_hook_stats` を有効化します
- `UiaProbeRecycleIntervalSec` (default: `1800`)
  - UIA探索サブプロセス再起動の判定間隔(秒)です
  - UIA探索は不具合があるようで際限無くメモリを消費するため、定期的に再起動することで消費量の削減を試みます
- `LogMinimumLevel` (default: `warn`)
  - core/plugin/workerログの最小出力レベルです
  - `debug`/`info`/`warn`/`error`を指定できます
  - 指定レベル未満のログは出力しません
