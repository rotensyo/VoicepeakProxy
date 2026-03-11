namespace VoicepeakProxyCore;

// 入力文字列を比較用に正規化
internal static class InputTextNormalizer
{
    // 改行を除去して前後空白を除去
    public static string Normalize(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\r", string.Empty)
                    .Replace("\n", string.Empty)
                    .Trim();
    }
}
