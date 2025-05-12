using System;
using System.Collections.Generic;
using Orleans;

namespace Aevatar.GAgents.AI.Common
{
    /// <summary>
    /// Message metadata information class, stores additional information about messages
    /// </summary>
    [GenerateSerializer]
    public class ChatMessageInfo
    {
        [Id(0)] public long MessageId { get; set; }  // Unique identifier for the message
        [Id(1)] public Dictionary<string, string> ExtendedProperties { get; set; } = new Dictionary<string, string>(); // Extended properties
    }

    /// <summary>
    /// Class that encapsulates both message and its metadata
    /// </summary>
    [GenerateSerializer]
    public class ChatMessageWithInfo
    {
        [Id(0)] public ChatMessage Message { get; set; }
        [Id(1)] public ChatMessageInfo Info { get; set; }
    }
} 