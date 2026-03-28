namespace BouyomiVoicepeakBridge.Shared
{
    // 発話要求を保持
    public sealed class WorkerSpeakRequest
    {
        // speak/ping/shutdown等の実行コマンド
        public string Command { get; set; }
        // 棒読みちゃん側のタスクID
        public int TaskId { get; set; }
        // 読み上げ対象文字列
        public string Text { get; set; }

        public WorkerSpeakRequest()
        {
            Command = "speak";
            TaskId = -1;
            Text = string.Empty;
        }
    }

    // 受理結果を保持
    public sealed class WorkerSpeakResponse
    {
        public bool Accepted { get; set; }
        public string ErrorMessage { get; set; }

        public WorkerSpeakResponse()
        {
            Accepted = false;
            ErrorMessage = string.Empty;
        }
    }
}
