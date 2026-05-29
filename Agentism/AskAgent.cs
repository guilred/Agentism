using Google.GenAI;
using Google.GenAI.Types;

namespace Agentism;

using Google.GenAI;
using Google.GenAI.Types;

public class AskAgent(string name, string systemInstruction, string apiKey) {
    public string Name { get; set; } = name;

    private readonly Client _client = new(apiKey: apiKey);
    private readonly GenerateContentConfig _config = new() {
        Temperature = 0.1,
        SystemInstruction = new Content { Parts = [new Part { Text = systemInstruction }] }
    };

    public async Task<string> ThinkAsync(string userInput) {
        var response = await _client.Models.GenerateContentAsync(
            model: "gemini-3.1-flash-lite",
            contents: userInput,
            config: _config
        );

        return response?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "I have nothing to say.";
    }
}