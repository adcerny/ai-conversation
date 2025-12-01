using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

public class ConversationLogger
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Configures NLog to log messages into a Markdown document.
    /// The directory for the log file is loaded from configuration, and the file name is generated in code.
    /// </summary>
    public static void ConfigureLogging(IConfiguration configuration)
    {
        var config = new LoggingConfiguration();

        // Load the directory path from configuration (default to current directory if not set).
        string logDirectory = configuration["Logger:Directory"] ?? ".";
        if (!logDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            logDirectory += Path.DirectorySeparatorChar;
        }

        // Construct the file name with a datestamp.
        string dateStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string fileName = $"conversationLog-{dateStamp}.md";
        string logFilePath = Path.Combine(logDirectory, fileName);

        // Configure a file target for Markdown output.
        var mdTarget = new FileTarget("mdLog")
        {
            FileName = logFilePath,
            // A simple Markdown header. You can adjust as needed.
            Header = "# Conversation Log\n\n---\n\n",
            Footer = "\n\n---\n_End of Conversation Log_",
            Layout = "${message}",
            KeepFileOpen = true,
            ConcurrentWrites = true
        };

        config.AddTarget(mdTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, mdTarget);
        LogManager.Configuration = config;
    }

    /// <summary>
    /// Logs the introduction section at the start of the conversation log, using the template from configuration.
    /// </summary>
    /// <param name="configuration">App configuration containing the template.</param>
    /// <param name="subject">Conversation subject.</param>
    /// <param name="models">Comma-separated list of models.</param>
    /// <param name="rounds">Number of rounds.</param>
    public void LogIntroduction(IConfiguration configuration, string subject, string models, int rounds)
    {
        string template = configuration["IntroductionTemplate"] ?? "";
        if (string.IsNullOrWhiteSpace(template)) return;

        // Replace placeholders
        template = template.Replace("{subject}", subject)
                           .Replace("{models}", models)
                           .Replace("{rounds}", rounds.ToString());

        Logger.Info(template + "\n");
    }


    /// <summary>
    /// Logs a chatbot's response as a Markdown entry, with improved formatting for readability.
    /// </summary>
    /// <param name="modelName">The name of the model sending the response.</param>
    /// <param name="response">The text of the response.</param>
    /// <param name="color">The HTML color to use for the text.</param>
    /// <param name="round">Optional round number for section header.</param>
    public void LogResponse(string modelName, string response, string color, int? round = null)
    {
        // Replace newline characters with Markdown line breaks for blockquote formatting
        string mdResponse = Regex.Replace(response, @"(\r\n|\n|\r)", "\n>");

        // Build the Markdown log entry.
        string roundHeader = round.HasValue ? $"## Round {round.Value}\n\n" : "";
        string logEntry = roundHeader +
            $"**Model:** <span style=\"color:{color}; font-weight:bold;\">{modelName}</span>\n\n" +
            $"> {mdResponse}\n\n";
        Logger.Info(logEntry);
    }
}
