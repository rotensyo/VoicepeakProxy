using System.IO;

namespace BouyomiVoicepeakBridge.Shared
{
    // パイプの文字列フレーミングを提供
    public static class PipeFraming
    {
        // 文字列をフレーム書き込み
        public static void WriteFrame(BinaryWriter writer, string text)
        {
            writer.Write(text ?? string.Empty);
            writer.Flush();
        }

        // 文字列フレームを読み取り
        public static bool TryReadFrame(BinaryReader reader, out string text)
        {
            try
            {
                text = reader.ReadString();
                return true;
            }
            catch (EndOfStreamException)
            {
                text = string.Empty;
                return false;
            }
        }
    }
}
