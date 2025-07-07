using System.Threading.Tasks;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace GodGPT.GAgents.SpeechChat;

public interface ISpeechService
{
    Task<string> SpeechToTextAsync(byte[] audioData);
    Task<byte[]> TextToSpeechAsync(string text);
    Task<string> SpeechToTextAsync(byte[] audioData, VoiceLanguageEnum language);
    Task<byte[]> TextToSpeechAsync(string text, VoiceLanguageEnum language);
    Task<(byte[] AudioData, AudioMetadata Metadata)> TextToSpeechWithMetadataAsync(string text, VoiceLanguageEnum language);
}