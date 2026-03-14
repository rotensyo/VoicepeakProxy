# Build From Source

## 前提

- Windows
- `.NET SDK`導入済み
- `.NET Framework 4.8`ターゲット開発環境

## ビルド

プロジェクト直下で実行します。

```powershell
dotnet build
```

主な生成物です。

- `bin/Debug/net48/VoicepeakProxyCore.dll`

## テスト

```powershell
dotnet test tests/auto/VoicepeakProxyCore.Tests.csproj
```

手動テストは別プロジェクトです。

```powershell
dotnet test tests/manual/VoicepeakProxyCore.ManualTests.csproj
```

## 主な依存関係

- `NAudio`
- `UIAutomationClient`
- `UIAutomationTypes`
- `WindowsBase`
