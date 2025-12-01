using System.Collections.Generic;

internal class SubjectConfig
{
    public int NumberOfRounds { get; set; }

    public Dictionary<string, ChatModelConfig> Models { get; set; } = new();
}
