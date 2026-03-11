using System.Collections.Generic;
using VoicepeakProxyCore;

namespace VoicepeakProxyCore.Tests;

internal sealed class TestLogger : IAppLogger
{
    public List<string> DebugMessages { get; } = new List<string>();
    public List<string> InfoMessages { get; } = new List<string>();
    public List<string> WarnMessages { get; } = new List<string>();
    public List<string> ErrorMessages { get; } = new List<string>();

    public IEnumerable<string> AllMessages
    {
        get
        {
            foreach (string message in DebugMessages)
            {
                yield return "DEBUG " + message;
            }

            foreach (string message in InfoMessages)
            {
                yield return "INFO " + message;
            }

            foreach (string message in WarnMessages)
            {
                yield return "WARN " + message;
            }

            foreach (string message in ErrorMessages)
            {
                yield return "ERROR " + message;
            }
        }
    }

    public void Debug(string message) => DebugMessages.Add(message);
    public void Info(string message) => InfoMessages.Add(message);
    public void Warn(string message) => WarnMessages.Add(message);
    public void Error(string message) => ErrorMessages.Add(message);
}
