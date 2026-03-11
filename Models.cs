using System;
using System.Collections.Generic;
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
}

// 分割後の発話セグメント
internal sealed class Segment
{
    public string Text { get; set; } = string.Empty;
    public int PausePreMs { get; set; }
}

// リクエストを内部ジョブへ変換
internal static class JobCompiler
{
    private static readonly Regex PauseRegex = new Regex(@"\[\[pause:(-?\d+)\]\]", RegexOptions.Compiled);

    // 入力内容を検証しながらジョブ化
    public static Job Compile(SpeakRequest req, AppConfig config, RequestValidationMode validationMode)
    {
        if (req == null)
        {
            throw new InvalidOperationException("request は null にできません");
        }

        string sourceText = req.Text;
        EnqueueMode sourceMode = req.Mode;

        if (validationMode == RequestValidationMode.Disabled)
        {
            sourceText ??= string.Empty;
        }
        else if (validationMode == RequestValidationMode.Lenient)
        {
            sourceText ??= string.Empty;
        }
        else
        {
            if (sourceText == null)
            {
                throw new InvalidOperationException("text は null にできません");
            }
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

        foreach (Match m in PauseRegex.Matches(input))
        {
            // pause制御は置換対象から除外
            string chunk = ApplyReplaceRules(input.Substring(index, m.Index - index), config.TextTransform.ReplaceRules);
            if (chunk.Length > 0)
            {
                segments.Add(new Segment
                {
                    Text = chunk,
                    PausePreMs = pendingPause
                });
                pendingPause = 0;
            }

            int pauseValue = int.Parse(m.Groups[1].Value);
            if (pauseValue < 0)
            {
                pauseValue = 0;
            }

            pendingPause += pauseValue;
            index = m.Index + m.Length;
        }

        string tail = ApplyReplaceRules(input.Substring(index), config.TextTransform.ReplaceRules);
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
        if (segments.Count == 0)
        {
            segments.Add(new Segment { Text = string.Empty, PausePreMs = 0 });
        }

        return new Job
        {
            Mode = mode,
            Interrupt = req.Interrupt,
            Segments = segments,
            TrailingPauseMs = trailingPause
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
