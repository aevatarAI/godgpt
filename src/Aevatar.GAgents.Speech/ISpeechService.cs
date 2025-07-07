using System.Threading.Tasks;

namespace Aevatar.GAgents.Speech;

public interface ISpeechService
{
    Task<string> SpeechToTextAsync(byte[] audioData);
    Task<byte[]> TextToSpeechAsync(string text);
    Task<string> SpeechToTextAsync(byte[] audioData, VoiceLanguageEnum language);
    Task<byte[]> TextToSpeechAsync(string text, VoiceLanguageEnum language);
}