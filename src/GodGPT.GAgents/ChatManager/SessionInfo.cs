using System;
using System.Collections.Generic;
using Orleans;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class SessionInfo
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
    [Id(3)] public List<Guid> ShareIds { get; set; } = new List<Guid>();
    
    // Voice-related fields
    [Id(4)] public bool VoiceEnabled { get; set; } = false;
    [Id(5)] public string DefaultVoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";
    [Id(6)] public int TotalVoiceMessages { get; set; } = 0;
    [Id(7)] public long TotalVoiceDataSize { get; set; } = 0; // Size in bytes
    public Guid? LastSpeechGAgentId { get; set; }
    
    // Voice-related fields
    public Dictionary<string, Guid>? SpeechGAgentIds { get; set; }
}