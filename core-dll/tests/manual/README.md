# Manual Tests

このディレクトリは、実際のVOICEPEAK UIを目視確認する手動テスト用です。

- 自動テスト用の`VoicepeakProxyCore.sln`には含めません
- 通常の`dotnet test core-dll/tests/auto/VoicepeakProxyCore.Tests.csproj`では実行されません
- 実行前に`voicepeak.exe`を起動してください
- VSCodeから実行する場合は`VoicepeakProxyCore.manual.code-workspace`を開いてください
- `core-dll/tests/manual/CompositeMoveToStartManualTests.cs`を開くと各`[TestMethod]`から実行できます

## 実行方法

```powershell
dotnet test core-dll/tests/manual/VoicepeakProxyCore.ManualTests.csproj
```

主な確認内容です。

- 入力欄フォーカス後に`Ctrl+Up`で先頭移動できること
- 起動時バリデーションと単発読み上げが成功すること
