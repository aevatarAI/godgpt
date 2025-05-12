using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class SessionInfoDto
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
    
    // Voice-related fields
    [Id(3)] public bool VoiceEnabled { get; set; } = false;
    [Id(4)] public string DefaultVoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";
    [Id(5)] public int TotalVoiceMessages { get; set; } = 0;
} 