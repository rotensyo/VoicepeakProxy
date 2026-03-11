namespace VoicepeakProxyCore;

// 起動時検証の実行方針
public enum BootValidationMode
{
    Required,
    Optional,
    Disabled
}

// リクエスト検証の実行方針
public enum RequestValidationMode
{
    Strict,
    Lenient,
    Disabled
}
