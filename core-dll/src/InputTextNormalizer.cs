namespace VoicepeakProxyCore;

// 入力文字列の正規化を提供
internal static class InputTextNormalizer
{
    // 通常入力向けに改行を区切りへ正規化
    public static string NormalizeForTyping(string value)
    {
        // 絵文字由来文字を先に除去
        string cleaned = RemoveEmojiCharacters(value ?? string.Empty);
        // 改行種別をLFへ統一
        string normalizedNewlines = NormalizeNewlineVariants(cleaned);
        // 先頭末尾の空白改行ランを除去
        string trimmed = normalizedNewlines.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(trimmed.Length);
        int index = 0;
        while (index < trimmed.Length)
        {
            char c = trimmed[index];
            if (!char.IsWhiteSpace(c))
            {
                // 非空白はそのまま維持
                builder.Append(c);
                index++;
                continue;
            }

            int runStart = index;
            bool hasLineBreak = false;
            // 空白連続区間を走査
            while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
            {
                if (trimmed[index] == '\n' || trimmed[index] == '\r')
                {
                    hasLineBreak = true;
                }

                index++;
            }

            if (hasLineBreak)
            {
                // 空白改行混在ランは改行1つへ圧縮
                builder.Append('\n');
                continue;
            }

            // 改行を含まない空白ランは維持
            builder.Append(trimmed, runStart, index - runStart);
        }

        return builder.ToString();
    }

    // 入力検証向けに改行除去して前後空白を除去
    public static string NormalizeForValidation(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        string withoutNewlines = NormalizeNewlineVariants(value).Replace("\n", string.Empty);

        return RemoveEmojiCharacters(withoutNewlines).Trim();
    }

    // 改行の表現差をLFへ統一
    private static string NormalizeNewlineVariants(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\r' || c == '\n')
            {
                if (i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if ((c == '\r' && next == '\n') || (c == '\n' && next == '\r'))
                    {
                        i++;
                    }
                }

                builder.Append('\n');
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    // 絵文字由来の文字を除去
    private static string RemoveEmojiCharacters(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            // 絵文字関連シーケンスをまとめてスキップ
            if (TryGetEmojiSequenceSkipLength(value, i, out int skipLength))
            {
                i += skipLength - 1;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    // 絵文字関連シーケンスのスキップ長を取得
    private static bool TryGetEmojiSequenceSkipLength(string value, int index, out int skipLength)
    {
        skipLength = 0;
        char c = value[index];

        if (char.IsHighSurrogate(c))
        {
            skipLength = (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) ? 2 : 1;
            return true;
        }

        if (char.IsLowSurrogate(c))
        {
            skipLength = 1;
            return true;
        }

        if (IsEmojiControlCharacter(c))
        {
            skipLength = 1;
            return true;
        }

        if (IsKeycapBase(c) && TryGetKeycapSequenceLength(value, index, out int keycapLength))
        {
            skipLength = keycapLength;
            return true;
        }

        if (TryGetEmojiVariationSequenceLength(value, index, out int emojiVariationLength))
        {
            skipLength = emojiVariationLength;
            return true;
        }

        return false;
    }

    // 絵文字構成制御文字を判定
    private static bool IsEmojiControlCharacter(char c)
    {
        return c == '\u200D' || c == '\uFE0E' || c == '\uFE0F' || c == '\u20E3';
    }

    // キーキャップ基底文字を判定
    private static bool IsKeycapBase(char c)
    {
        return (c >= '0' && c <= '9') || c == '#' || c == '*';
    }

    // キーキャップシーケンス長を取得
    private static bool TryGetKeycapSequenceLength(string value, int index, out int length)
    {
        length = 0;
        int next = index + 1;
        if (next >= value.Length)
        {
            return false;
        }

        if (value[next] == '\uFE0F')
        {
            next++;
        }

        if (next < value.Length && value[next] == '\u20E3')
        {
            length = next - index + 1;
            return true;
        }

        return false;
    }

    // VS16付き絵文字シーケンス長を取得
    private static bool TryGetEmojiVariationSequenceLength(string value, int index, out int length)
    {
        length = 0;
        int next = index + 1;
        if (next >= value.Length)
        {
            return false;
        }

        if (value[next] != '\uFE0F')
        {
            return false;
        }

        length = 2;
        return true;
    }
}
