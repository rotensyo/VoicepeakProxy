using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace VoicepeakProxyCore;

// ジョブ投入モード
internal enum JobMode
{
    Queue,
    Next,
    Flush
}

// 公開キュー投入モード
public enum EnqueueMode
{
    Queue,
    Next,
    Flush
}

// 常駐API向け入力モデル
public sealed class SpeakRequest
{
    public string Text { get; set; } = string.Empty;
    public EnqueueMode Mode { get; set; } = EnqueueMode.Queue;
    public bool Interrupt { get; set; }
}

// 実行キュー上の内部ジョブ
internal sealed class Job
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public JobMode Mode { get; set; }
    public bool Interrupt { get; set; }
    public List<Segment> Segments { get; set; } = new List<Segment>();
    public int TrailingPauseMs { get; set; }
    public bool IsDelayOnly { get; set; }
}

// 分割後の発話セグメント
internal sealed class Segment
{
    public string Text { get; set; } = string.Empty;
    public int PausePreMs { get; set; }
}

// pauseトークン解析を共通化
internal static class PauseTokenParser
{
    private static readonly Regex PauseRegex = new Regex(@"\[\[pause:(-?\d+)\]\]", RegexOptions.Compiled);

    // pauseトークン一致を列挙
    internal static MatchCollection Matches(string input)
    {
        return PauseRegex.Matches(input ?? string.Empty);
    }

    // pause値を0以上へ正規化
    internal static int NormalizePauseValue(string raw)
    {
        int pauseValue = int.Parse(raw);
        return pauseValue < 0 ? 0 : pauseValue;
    }

    // pauseトークンを除去した文字列を返す
    internal static string StripTokens(string input)
    {
        string source = input ?? string.Empty;
        MatchCollection matches = Matches(source);
        if (matches.Count == 0)
        {
            return source;
        }

        StringBuilder builder = new StringBuilder(source.Length);
        int index = 0;
        foreach (Match m in matches)
        {
            builder.Append(source, index, m.Index - index);
            index = m.Index + m.Length;
        }

        builder.Append(source, index, source.Length - index);
        return builder.ToString();
    }
}

// リクエストを内部ジョブへ変換
internal static class JobCompiler
{
    // 入力内容を検証しながらジョブ化
    public static Job Compile(SpeakRequest req, AppConfig config)
    {
        if (req == null)
        {
            throw new InvalidOperationException("request は null にできません");
        }

        string sourceText = req.Text;
        EnqueueMode sourceMode = req.Mode;

        if (sourceText == null)
        {
            throw new InvalidOperationException("text は null にできません");
        }

        JobMode mode = sourceMode switch
        {
            EnqueueMode.Queue => JobMode.Queue,
            EnqueueMode.Next => JobMode.Next,
            EnqueueMode.Flush => JobMode.Flush,
            _ => throw new InvalidOperationException("mode が不正です")
        };

        string input = sourceText ?? string.Empty;
        List<Segment> segments = new List<Segment>();
        int trailingPause = 0;
        int index = 0;
        int pendingPause = 0;

        foreach (Match m in PauseTokenParser.Matches(input))
        {
            // pause制御は置換対象から除外
            string chunk = ApplyReplaceRules(input.Substring(index, m.Index - index), config.Text.ReplaceRules);
            if (chunk.Length > 0)
            {
                segments.Add(new Segment
                {
                    Text = chunk,
                    PausePreMs = pendingPause
                });
                pendingPause = 0;
            }

            int pauseValue = PauseTokenParser.NormalizePauseValue(m.Groups[1].Value);

            pendingPause += pauseValue;
            index = m.Index + m.Length;
        }

        string tail = ApplyReplaceRules(input.Substring(index), config.Text.ReplaceRules);
        if (tail.Length > 0)
        {
            segments.Add(new Segment
            {
                Text = tail,
                PausePreMs = pendingPause
            });
            pendingPause = 0;
        }

        trailingPause += pendingPause;

        int totalPause = trailingPause;
        int speakableLength = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i] ?? new Segment();
            totalPause += segment.PausePreMs;
            speakableLength += InputTextNormalizer.Normalize(segment.Text).Length;
        }

        bool isDelayOnly = false;
        if (speakableLength == 0)
        {
            if (totalPause > 0)
            {
                isDelayOnly = true;
                segments = new List<Segment> { new Segment { Text = string.Empty, PausePreMs = 0 } };
                trailingPause = totalPause;
            }
            else
            {
                throw new InvalidOperationException("text は空文字にできません");
            }
        }

        return new Job
        {
            Mode = mode,
            Interrupt = req.Interrupt,
            Segments = segments,
            TrailingPauseMs = trailingPause,
            IsDelayOnly = isDelayOnly
        };
    }

    // 置換ルールを順次適用
    private static string ApplyReplaceRules(string input, List<ReplaceRule> rules)
    {
        string current = input ?? string.Empty;
        if (rules == null)
        {
            return current;
        }

        for (int i = 0; i < rules.Count; i++)
        {
            ReplaceRule rule = rules[i];
            if (rule == null || string.IsNullOrEmpty(rule.From))
            {
                continue;
            }

            current = current.Replace(rule.From, rule.To ?? string.Empty);
        }

        return current;
    }
}

// 常駐API向け受理結果
public enum EnqueueStatus
{
    Accepted,
    QueueFull,
    InvalidRequest
}

// 常駐API向け受理結果
public sealed class EnqueueResult
{
    public EnqueueStatus Status { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Succeeded => Status == EnqueueStatus.Accepted;
}
