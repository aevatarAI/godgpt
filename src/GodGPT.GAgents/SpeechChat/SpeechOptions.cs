namespace GodGPT.GAgents.SpeechChat;

public class SpeechOptions
{
    public string SubscriptionKey { get; set; }
    public string Region { get; set; }
    public string Endpoint { get; set; }
    public string RecognitionLanguage { get; set; } = "zh-CN";
    public string SynthesisLanguage { get; set; } = "zh-CN";
    public string SynthesisVoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";
}