using Google.GenAI;
using Google.GenAI.Types;

namespace Agentism;

public class TalkAgent(string name, string systemInstruction, string apiKey) {
    public string Name { get; set; } = name;

    private readonly List<Content> _chatHistory = [];

    private readonly Client _client = new(apiKey: apiKey);
    private readonly GenerateContentConfig _config = new() {
        Temperature = 0.1,
        SystemInstruction = new Content { Parts = [new Part { Text = systemInstruction }] }
    };

    public async Task<string> ThinkAsync(string userInput) {
        _chatHistory.Add(new Content {
            Role = "user",
            Parts = [new Part { Text = userInput }]
        });

        var response = await _client.Models.GenerateContentAsync(
            model: "gemini-3.1-flash-lite",
            contents: _chatHistory,
            config: _config
        );

        string reply = response?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "I have nothing to say.";

        _chatHistory.Add(new Content {
            Role = "model",
            Parts = [new Part { Text = reply }]
        });

        return reply;
    }
    public void ClearHistory() => _chatHistory.Clear();
}