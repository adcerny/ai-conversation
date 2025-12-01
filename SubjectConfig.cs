using System.Collections.Generic;

internal class SubjectConfig
{
    public Dictionary<string, ChatModelConfig> Models { get; set; } = new();

    public string ReminderPrompt { get; set; } = string.Empty;

    public int? ReminderInterval { get; set; }
}
