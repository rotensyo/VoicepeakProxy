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

        string withoutNewlines = value.Replace("\r", string.Empty)
            .Replace("\n", string.Empty);

        return RemoveNonBmpCharacters(withoutNewlines).Trim();
    }

    // 非BMP文字を除去
    private static string RemoveNonBmpCharacters(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }

                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
