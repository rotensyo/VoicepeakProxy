namespace VoicepeakProxyCore;

// 入力読み取りソース
internal enum ReadInputSource
{
    PrimaryUiA,
    NoCandidate,
    Exception
}

// 入力読み取り結果
internal readonly struct ReadInputResult
{
    public bool Success { get; }
    public string Text { get; }
    public int TotalLength { get; }
    public ReadInputSource Source { get; }

    private ReadInputResult(bool success, string text, int totalLength, ReadInputSource source)
    {
        Success = success;
        Text = text ?? string.Empty;
        TotalLength = totalLength;
        Source = source;
    }

    public static ReadInputResult Ok(string text, int totalLength, ReadInputSource source)
        => new ReadInputResult(true, text, totalLength, source);

    public static ReadInputResult Fail(ReadInputSource source, string text, int totalLength)
        => new ReadInputResult(false, text, totalLength, source);
}
