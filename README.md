# VoicepeakProxy

VoicepeakProxy は、[VOICEPEAK](https://www.ah-soft.com/voice/)の自動読み上げを低遅延で実現するライブラリです。

本リポジトリは、VOICEPEAKの操作を行う本体ライブラリの `VoicepeakProxyCore.dll` と、このライブラリを棒読みちゃんプラグインとして運用する `VoicepeakProxy for 棒読みちゃん` で構成されています。

## できること
### コンセプト
- VOICEPEAKの起動済みウィンドウを直接操作することで、低遅延での読み上げを行います。
- ウィンドウフォーカスを奪わず、PC上の他の作業を阻害しないようにします。
- VOICEPEAKが他のウィンドウの背面に隠れていても読み上げできる実装にします。

### VoicepeakProxyCore.dll
- 単発APIによる読み上げ操作
- 常駐ランタイムによる複数読み上げ要求のキュー処理

### VoicepeakProxy for 棒読みちゃん
- `VoicepeakProxyCore.dll` 経由での棒読みちゃんとVOICEPEAKの連携

## できないこと
- 音声ファイルの出力・保存
- パラメータ調整やナレーター切り替えの自動操作
- ウィンドウを最小化(タスクバーに格納)した状態での読み上げ

## 実行環境
- Windows環境であること
- `.NET Framework 4.8` 実行環境があること
- `voicepeak.exe` が1プロセスだけ起動していること
- config内のショートカット設定(再生/停止、先頭に移動、すべてを選択、ペースト)がVOICEPEAK側と一致していること

## 注意点
- 本プロジェクトはVOICEPEAKおよび棒読みちゃんの公式プロジェクトではありません。
- `VOICEPEAK 1.2.21` を前提に作成されており、バージョンが異なると動作しない可能性があります。
- 実行時、操作対象のVOICEPEAKプロセス内でDLL読み込みとAPIフックを有効化します。
  - ユーザー入力由来の誤動作防止、ショートカット実行時の警告音抑制、仮想クリップボード経由の文字入力を実現するため、該当するWindows APIのみ呼び出し動作を一時的に上書きし、制御します。
  - 変更は実行中プロセスのメモリ上のみで、ローカルファイルは改変しません。VOICEPEAKを再起動すると通常状態に戻ります。


## VoicepeakProxy for 棒読みちゃん: マニュアル
### 初期設定
1. [Release](https://github.com/rotensyo/VoicepeakProxy/releases/latest) で配布されている最新の `VoicepeakProxy-Plugin.zip` をダウンロード・解凍してください。
1. `Plugin_VoicepeakProxy.dll` と `VoicepeakProxyWorker` ディレクトリを、棒読みちゃんの本体(BouyomiChan.exe)があるディレクトリにそれぞれ配置してください。
1. VOICEPEAKを起動してください。
1. 棒読みちゃんを起動し、その他タブからプラグインタブを開いて `VoicepeakProxy for 棒読みちゃん` のチェックボックスを有効化してください。
   - 起動失敗ダイアログが出た場合は無視してください。
1. `VoicepeakProxy for 棒読みちゃん` を選択した状態で右下の設定ボタンから設定を開き、 `UI操作・ショートカット` の各ショートカット設定(先頭に移動、すべてを選択、再生/停止、ペースト)にVOICEPEAKのショートカットキー設定を写してください。
   - VOICEPEAK側のショートカットキー設定をデフォルトのまま変更していない場合、そのままで問題ありません。
1. 必要に応じて、棒読みちゃん側設定画面 `プラグイン` 内の自動起動設定にVOICEPEAK本体へのパスを指定してください。
1. 設定が終わったらOKを押して設定画面を閉じ、棒読みちゃんを再起動してください。
1. "初期化完了"の音声が流れれば初期設定は完了です。
   - "初期化完了"の冒頭が途切れる場合がありますが、それ以降の読み上げは問題ありません。

### 使用方法
- 上記初期設定の完了後は、VOICEPEAK起動後に棒読みちゃんを起動すれば読み上げ準備が完了します。
- 自動起動設定にVOICEPEAKの.exeパスを指定していれば、棒読みちゃん起動時にVOICEPEAKも自動で起動されます。

## VoicepeakProxyCore.dll: マニュアル
### 使用方法
[Release](https://github.com/rotensyo/VoicepeakProxy/releases/latest) で配布されている最新の `VoicepeakProxyCore.zip` をダウンロード・解凍し、下記を参考に任意の環境で実装を行ってください。

### 単発実行API使用例

```csharp
using VoicepeakProxyCore;

var config = new AppConfig();

SpeakOnceResult result = VoicepeakOneShot.SpeakOnceWait(
    config,
    new SpeakOnceRequest { Text = "こんにちは。テストです。" });

Console.WriteLine($"status={result.Status} ok={result.Succeeded} segments={result.SegmentsExecuted}");
```

### 常駐ランタイムAPI使用例

```csharp
using VoicepeakProxyCore;

var config = new AppConfig();
using var runtime = VoicepeakRuntime.Start(config);

EnqueueResult result = runtime.Enqueue(new SpeakRequest
{
    Text = "こんにちは。テストです。",
    Mode = EnqueueMode.Queue,
    Interrupt = false
});

Console.WriteLine($"status={result.Status} jobId={result.JobId} error={result.ErrorMessage}");
```

### 詳細ドキュメント
- [APIエンドポイント詳細](docs/api-reference.md)
- [ビルド方法](docs/build-from-source.md)
- [設定値詳細](docs/configuration.md)

## License
[MIT License](LICENSE)
