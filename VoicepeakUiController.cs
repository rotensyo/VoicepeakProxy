using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace VoicepeakProxyCore;

// VOICEPEAKのUI操作を担当
internal sealed class VoicepeakUiController : IVoicepeakUiController
{
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint WmKillFocus = 0x0008;
    private static readonly HashSet<string> ExcludedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "音量",
        "ボイス",
        "バージョン",
        "設定",
        "感情"
    };

    private readonly UiConfig _ui;
    private readonly DebugConfig _debug;
    private readonly Dictionary<char, List<SentenceBreakTrigger>> _sentenceBreakTriggerIndex;
    private readonly AppLogger _log;
    private readonly IVoicepeakProcessApi _processApi;

    // UI設定とロガーを保持
    public VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log)
        : this(ui, debug, log, new DefaultVoicepeakProcessApi())
    {
    }

    internal VoicepeakUiController(UiConfig ui, DebugConfig debug, AppLogger log, IVoicepeakProcessApi processApi)
    {
        _ui = ui;
        _debug = debug ?? new DebugConfig();
        _log = log;
        _processApi = processApi ?? new DefaultVoicepeakProcessApi();
        _sentenceBreakTriggerIndex = BuildSentenceBreakTriggerIndex(ui);
    }

    // 対象プロセスとメインウィンドウを解決
    public bool TryResolveTarget(out Process process, out IntPtr mainHwnd)
    {
        process = null;
        mainHwnd = IntPtr.Zero;

        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        if (matches.Length == 0)
        {
            return false;
        }

        process = matches[0];
        mainHwnd = _processApi.WaitMainWindowHandle(process, 3000);
        return mainHwnd != IntPtr.Zero;
    }

    public bool TryResolveTargetByPid(int pid, out Process process, out IntPtr mainHwnd)
    {
        process = null;
        mainHwnd = IntPtr.Zero;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            process = _processApi.GetProcessById(pid);
        }
        catch
        {
            return false;
        }

        try
        {
            if (process == null || process.HasExited)
            {
                return false;
            }

            if (!string.Equals(process.ProcessName, "voicepeak", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        mainHwnd = _processApi.WaitMainWindowHandle(process, 3000);
        return mainHwnd != IntPtr.Zero;
    }

    public int GetVoicepeakProcessCount()
    {
        Process[] matches = _processApi.GetProcessesByName("voicepeak");
        return matches.Length;
    }

    public bool IsAlive(Process process)
    {
        return process != null && !process.HasExited;
    }

    // 入力欄をクリア
    public bool ClearInput(Process process, IntPtr mainHwnd, int actionDelayMs)
    {
        if (!MoveToStart(mainHwnd, actionDelayMs))
        {
            return false;
        }

        for (int pass = 0; pass < 3; pass++)
        {
            ReadInputResult read = ReadInputTextDetailed(mainHwnd);
            int baseLength = read.Success ? read.TotalLength : 0;
            if (baseLength < 0)
            {
                baseLength = 0;
            }

            int clearSteps = Math.Max(30, baseLength + 30);
            for (int i = 0; i < clearSteps; i++)
            {
                if (!SendKey(mainHwnd, VirtualKey.Delete))
                {
                    return false;
                }
            }

            ReadInputResult after = ReadInputTextDetailed(mainHwnd);
            if (after.Success && after.TotalLength == 0)
            {
                return true;
            }

            if (!MoveToStart(mainHwnd, actionDelayMs))
            {
                return false;
            }
        }

        ReadInputResult finalRead = ReadInputTextDetailed(mainHwnd);
        _log.Warn("clear_input_incomplete " +
            $"success={finalRead.Success} totalLength={finalRead.TotalLength} source={finalRead.Source}");
        return finalRead.Success && finalRead.TotalLength == 0;
    }

    // 文字列を1文字ずつ送信
    public bool TypeText(IntPtr mainHwnd, string text, int charDelayMs)
    {
        string send = text ?? string.Empty;
        HashSet<int> enterPositions = ComputeSentenceBreakEnterPositions(send);
        for (int i = 0; i < send.Length; i++)
        {
            char c = send[i];
            bool isLastCharInSegment = i == send.Length - 1;
            if (!SendChar(mainHwnd, c))
            {
                return false;
            }

            if (charDelayMs > 0)
            {
                Thread.Sleep(charDelayMs);
            }

            // セグメント末尾では改行を抑止
            if (!isLastCharInSegment && enterPositions.Contains(i))
            {
                if (!SendKey(mainHwnd, VirtualKey.Return))
                {
                    return false;
                }

                if (charDelayMs > 0)
                {
                    Thread.Sleep(charDelayMs);
                }
            }
        }

        return true;
    }

    // 再生ショートカットを送信
    public bool PressPlay(IntPtr mainHwnd)
    {
        SendWindowMessage(mainHwnd, WmKillFocus, IntPtr.Zero, IntPtr.Zero);
        if (_ui.PlayPreShortcutDelayMs > 0)
        {
            Thread.Sleep(_ui.PlayPreShortcutDelayMs);
        }

        return SendShortcut(mainHwnd, _ui.PlayShortcut);
    }

    public bool MoveToStart(IntPtr mainHwnd, int actionDelayMs)
    {
        SleepActionDelay(actionDelayMs);
        return SendShortcut(mainHwnd, _ui.MoveToStartShortcut);
    }

    public bool MoveToEnd(IntPtr mainHwnd, int actionDelayMs)
    {
        SleepActionDelay(actionDelayMs);
        return SendShortcut(mainHwnd, _ui.MoveToEndShortcut);
    }

    public bool PressBackspace() => SendKey(IntPtr.Zero, VirtualKey.Back);
    public bool PressBackspace(IntPtr mainHwnd) => SendKey(mainHwnd, VirtualKey.Back);
    public bool PressDelete(IntPtr mainHwnd) => SendKey(mainHwnd, VirtualKey.Delete);

    internal static bool IsValidShortcut(string raw)
    {
        return ShortcutSpec.TryParse(raw, out _);
    }

    public ReadInputResult ReadInputTextDetailed(IntPtr mainHwnd)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(mainHwnd);
            List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount: 200);
            if (_debug.LogTextCandidates)
            {
                LogTextCandidates(candidates);
            }

            AutomationElement bestInput = FindBestInputBox(candidates);
            if (bestInput == null)
            {
                return ReadInputResult.Fail(ReadInputSource.NoCandidate, string.Empty, 0);
            }

            string primary = TryGetElementTextOrWindowTextSafe(bestInput);
            string normalizedPrimary = NormalizeForPanelCompare(primary);
            int totalLength = EstimateTotalInputTextLength(root, bestInput);
            if (totalLength < normalizedPrimary.Length)
            {
                totalLength = normalizedPrimary.Length;
            }

            return ReadInputResult.Ok(normalizedPrimary, totalLength, ReadInputSource.PrimaryUiA);
        }
        catch
        {
            return ReadInputResult.Fail(ReadInputSource.Exception, string.Empty, 0);
        }
    }

    private bool SendShortcut(IntPtr hwnd, string shortcut)
    {
        if (!ShortcutSpec.TryParse(shortcut, out ShortcutSpec spec))
        {
            return false;
        }

        List<VirtualKey> downs = new List<VirtualKey>();
        try
        {
            if (spec.Control)
            {
                SendKeyDown(hwnd, VirtualKey.Control);
                downs.Add(VirtualKey.Control);
            }

            if (spec.Shift)
            {
                SendKeyDown(hwnd, VirtualKey.Shift);
                downs.Add(VirtualKey.Shift);
            }

            if (spec.Alt)
            {
                SendKeyDown(hwnd, VirtualKey.Menu);
                downs.Add(VirtualKey.Menu);
            }

            SendKeyDown(hwnd, spec.Key);
            SendKeyUp(hwnd, spec.Key);
            return true;
        }
        finally
        {
            for (int i = downs.Count - 1; i >= 0; i--)
            {
                SendKeyUp(hwnd, downs[i]);
            }
        }
    }

    private bool SendKey(IntPtr hwnd, VirtualKey key)
    {
        return SendKeyDown(hwnd, key) && SendKeyUp(hwnd, key);
    }

    private bool SendKeyDown(IntPtr hwnd, VirtualKey key)
    {
        return SendWindowMessage(hwnd, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
    }

    private bool SendKeyUp(IntPtr hwnd, VirtualKey key)
    {
        return SendWindowMessage(hwnd, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
    }

    private bool SendChar(IntPtr hwnd, char c)
    {
        return SendWindowMessage(hwnd, WmChar, (IntPtr)c, IntPtr.Zero);
    }

    private static bool SendWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        IntPtr result;
        IntPtr ok = SendMessageTimeout(hWnd, msg, wParam, lParam, SmtoAbortIfHung, 1000, out result);
        return ok != IntPtr.Zero;
    }

    private static AutomationElement FindBestInputBox(List<TextCandidateInfo> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        AutomationElement best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            TextCandidateInfo candidate = candidates[i];
            if (candidate.Score > bestScore)
            {
                bestScore = candidate.Score;
                best = candidate.Element;
            }
        }

        return best;
    }

    private static int EstimateTotalInputTextLength(AutomationElement mainWindow, AutomationElement fallbackInputBox)
    {
        if (mainWindow != null)
        {
            List<AutomationElement> candidates = CollectTextLikeElements(mainWindow, maxCount: 200);
            int sum = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                AutomationElement e = candidates[i];
                var r = e.Current.BoundingRectangle;
                if (r.IsEmpty)
                {
                    continue;
                }

                string t = NormalizeForLength(TryGetElementTextOrWindowTextSafe(e));
                if (t.Length == 0)
                {
                    continue;
                }

                sum += t.Length;
            }

            if (sum > 0)
            {
                return sum;
            }
        }

        string fallback = NormalizeForLength(TryGetElementTextOrWindowTextSafe(fallbackInputBox));
        return fallback.Length;
    }

    private static List<AutomationElement> CollectTextLikeElements(AutomationElement root, int maxCount)
    {
        List<TextCandidateInfo> candidates = CollectTextCandidates(root, maxCount);
        List<AutomationElement> result = new List<AutomationElement>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            result.Add(candidates[i].Element);
        }

        return result;
    }

    private static List<TextCandidateInfo> CollectTextCandidates(AutomationElement root, int maxCount)
    {
        List<TextCandidateInfo> result = new List<TextCandidateInfo>();
        if (root == null || maxCount <= 0)
        {
            return result;
        }

        Queue<AutomationElement> queue = new Queue<AutomationElement>();
        queue.Enqueue(root);

        while (queue.Count > 0 && result.Count < maxCount)
        {
            AutomationElement e = queue.Dequeue();
            if (TryBuildTextCandidate(e, out TextCandidateInfo candidate))
            {
                result.Add(candidate);
            }

            AutomationElementCollection children = e.FindAll(TreeScope.Children, Condition.TrueCondition);
            for (int i = 0; i < children.Count; i++)
            {
                queue.Enqueue(children[i]);
            }
        }

        return result;
    }

    private static bool TryBuildTextCandidate(AutomationElement element, out TextCandidateInfo candidate)
    {
        candidate = default(TextCandidateInfo);
        if (element == null)
        {
            return false;
        }

        ControlType controlType = element.Current.ControlType;
        if (IsExcludedControlType(controlType))
        {
            return false;
        }

        bool textLike = controlType == ControlType.Edit
                        || controlType == ControlType.Document
                        || controlType == ControlType.Text
                        || HasTextLikePattern(element);
        if (!textLike || IsExcludedName(element.Current.Name))
        {
            return false;
        }

        var rect = element.Current.BoundingRectangle;
        bool visible = !rect.IsEmpty && rect.Width > 10 && rect.Height > 10;
        if (!visible)
        {
            return false;
        }

        string text = TryGetElementTextOrWindowTextSafe(element);
        double score = rect.Width * rect.Height;
        if (controlType == ControlType.Edit)
        {
            score += 10000;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            score += 1000000;
        }

        candidate = new TextCandidateInfo(element, score);
        return true;
    }

    internal static bool IsExcludedControlType(ControlType controlType)
    {
        if (controlType == null)
        {
            return false;
        }

        string programmaticName = controlType.ProgrammaticName;
        return string.Equals(programmaticName, "ControlType.Slider", StringComparison.Ordinal)
            || string.Equals(programmaticName, "ControlType.ComboBox", StringComparison.Ordinal)
            || string.Equals(programmaticName, "ControlType.ScrollBar", StringComparison.Ordinal);
    }

    private void LogTextCandidates(List<TextCandidateInfo> candidates)
    {
        int count = candidates != null ? candidates.Count : 0;
        _log.Debug($"text_candidates_begin count={count}");
        if (candidates == null)
        {
            _log.Debug("text_candidates_end");
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            TextCandidateInfo c = candidates[i];
            AutomationElement e = c.Element;
            var r = e.Current.BoundingRectangle;
            bool hasTextPattern = e.TryGetCurrentPattern(TextPattern.Pattern, out _);
            bool hasValuePattern = e.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            string text = TryGetElementTextOrWindowTextSafe(e);
            _log.Debug(
                "text_candidate " +
                $"index={i} " +
                $"controlType={e.Current.ControlType.ProgrammaticName} " +
                $"name=\"{SanitizeForLog(e.Current.Name)}\" " +
                $"automationId=\"{SanitizeForLog(e.Current.AutomationId)}\" " +
                $"className=\"{SanitizeForLog(e.Current.ClassName)}\" " +
                $"hasTextPattern={hasTextPattern} " +
                $"hasValuePattern={hasValuePattern} " +
                $"rect=({r.Left:F1},{r.Top:F1},{r.Width:F1},{r.Height:F1}) " +
                $"score={c.Score:F1} " +
                $"text=\"{SanitizeForLog(text)}\"");
        }

        _log.Debug("text_candidates_end");
    }

    private static string NormalizeForPanelCompare(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static string NormalizeForLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private static bool HasTextLikePattern(AutomationElement element)
    {
        if (element == null)
        {
            return false;
        }

        return element.TryGetCurrentPattern(TextPattern.Pattern, out _) ||
               element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
    }

    internal static bool IsExcludedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return ExcludedNames.Contains(name.Trim());
    }

    private static string GetElementText(AutomationElement element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObj))
        {
            return ((ValuePattern)valuePatternObj).Current.Value ?? string.Empty;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPatternObj))
        {
            return (((TextPattern)textPatternObj).DocumentRange.GetText(-1) ?? string.Empty).TrimEnd('\r', '\n');
        }

        return string.Empty;
    }

    private static string TryGetElementTextOrWindowTextSafe(AutomationElement element)
    {
        try
        {
            return GetElementTextOrWindowText(element);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetElementTextOrWindowText(AutomationElement element)
    {
        if (element == null)
        {
            return string.Empty;
        }

        string text = GetElementText(element);
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        int nativeHandle = element.Current.NativeWindowHandle;
        if (nativeHandle == 0)
        {
            return string.Empty;
        }

        IntPtr hWnd = new IntPtr(nativeHandle);
        int length = (int)SendMessageTimeout(hWnd, WmGetTextLength, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 500, out _);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(length + 1);
        SendMessageTimeout(hWnd, WmGetText, new IntPtr(sb.Capacity), sb, SmtoAbortIfHung, 500, out _);
        return sb.ToString();
    }

    private static string SanitizeForLog(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }

    private static IntPtr WaitMainWindowHandle(Process process, int timeoutMs)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (process.HasExited)
            {
                return IntPtr.Zero;
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            Thread.Sleep(50);
        }

        return IntPtr.Zero;
    }

    private static void SleepActionDelay(int actionDelayMs)
    {
        if (actionDelayMs > 0)
        {
            Thread.Sleep(actionDelayMs);
        }
    }

    private static Dictionary<char, List<SentenceBreakTrigger>> BuildSentenceBreakTriggerIndex(UiConfig ui)
    {
        var index = new Dictionary<char, List<SentenceBreakTrigger>>();
        if (ui?.SentenceBreakTriggers == null)
        {
            return index;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        int order = 0;

        for (int i = 0; i < ui.SentenceBreakTriggers.Count; i++)
        {
            string token = ui.SentenceBreakTriggers[i];
            if (!string.IsNullOrEmpty(token))
            {
                if (used.Add(token))
                {
                    char key = token[0];
                    if (!index.TryGetValue(key, out List<SentenceBreakTrigger> bucket))
                    {
                        bucket = new List<SentenceBreakTrigger>();
                        index[key] = bucket;
                    }

                    bucket.Add(new SentenceBreakTrigger(token, order));
                    order++;
                }
            }
        }

        foreach (List<SentenceBreakTrigger> bucket in index.Values)
        {
            // 最長一致優先
            // 同長は設定順優先
            bucket.Sort((a, b) =>
            {
                int len = b.Text.Length.CompareTo(a.Text.Length);
                if (len != 0)
                {
                    return len;
                }

                return a.Order.CompareTo(b.Order);
            });
        }

        return index;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        StringBuilder lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private enum VirtualKey
    {
        Back = 0x08,
        Return = 0x0D,
        Delete = 0x2E,
        Space = 0x20,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        Home = 0x24,
        End = 0x23,
        Control = 0x11,
        Shift = 0x10,
        Menu = 0x12
    }

    // 改行挿入位置を事前計算
    private HashSet<int> ComputeSentenceBreakEnterPositions(string text)
    {
        HashSet<int> positions = new HashSet<int>();
        if (!_ui.SendEnterAfterSentenceBreak || string.IsNullOrEmpty(text))
        {
            return positions;
        }

        int index = 0;
        while (index < text.Length)
        {
            if (!_sentenceBreakTriggerIndex.TryGetValue(text[index], out List<SentenceBreakTrigger> candidates))
            {
                index++;
                continue;
            }

            SentenceBreakTrigger matched = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                SentenceBreakTrigger candidate = candidates[i];
                if (!MatchesTrigger(text, index, candidate.Text))
                {
                    continue;
                }

                matched = candidate;
                break;
            }

            if (matched == null)
            {
                index++;
                continue;
            }

            positions.Add(index + matched.Text.Length - 1);
            index += matched.Text.Length;
        }

        return positions;
    }

    // 指定位置でトークン一致を確認
    private static bool MatchesTrigger(string text, int startIndex, string trigger)
    {
        if (string.IsNullOrEmpty(trigger))
        {
            return false;
        }

        if (startIndex < 0 || startIndex + trigger.Length > text.Length)
        {
            return false;
        }

        return string.Compare(text, startIndex, trigger, 0, trigger.Length, StringComparison.Ordinal) == 0;
    }

    // 改行トリガー情報を保持
    private sealed class SentenceBreakTrigger
    {
        public SentenceBreakTrigger(string text, int order)
        {
            Text = text;
            Order = order;
        }

        public string Text { get; }
        public int Order { get; }
    }

    private sealed class ShortcutSpec
    {
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public VirtualKey Key { get; set; }

        public static bool TryParse(string raw, out ShortcutSpec spec)
        {
            spec = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] parts = raw.Split('+');
            ShortcutSpec temp = new ShortcutSpec();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Control = true;
                    continue;
                }

                if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Shift = true;
                    continue;
                }

                if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    temp.Alt = true;
                    continue;
                }

                if (!TryParseKey(p, out VirtualKey key))
                {
                    return false;
                }

                temp.Key = key;
            }

            if (temp.Key == 0)
            {
                return false;
            }

            spec = temp;
            return true;
        }

        private static bool TryParseKey(string text, out VirtualKey key)
        {
            key = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (text.Equals("Space", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.Space;
                return true;
            }

            if (text.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.Home;
                return true;
            }

            if (text.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                key = VirtualKey.End;
                return true;
            }

            if (text.Length <= 3 && text.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(text.Substring(1), out int fn) && fn >= 1 && fn <= 12)
                {
                    key = (VirtualKey)((int)VirtualKey.F1 + (fn - 1));
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct TextCandidateInfo
    {
        public AutomationElement Element { get; }
        public double Score { get; }

        public TextCandidateInfo(AutomationElement element, double score)
        {
            Element = element;
            Score = score;
        }
    }

    private sealed class DefaultVoicepeakProcessApi : IVoicepeakProcessApi
    {
        public Process[] GetProcessesByName(string processName) => Process.GetProcessesByName(processName);

        public Process GetProcessById(int pid) => Process.GetProcessById(pid);

        public IntPtr WaitMainWindowHandle(Process process, int timeoutMs) => VoicepeakUiController.WaitMainWindowHandle(process, timeoutMs);
    }
}
