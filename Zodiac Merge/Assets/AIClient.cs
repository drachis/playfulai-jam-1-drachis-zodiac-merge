using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class AIResult
{
    public string emoji;
    public string name;
    public string gloss;
    public int weight;
    public string[] tags;
}

public enum MergeMode { Fusion, Action }

public class AIClient : MonoBehaviour
{
    // Helper method to send a request to Ollama and get the raw response
    private async Task<string> SendOllamaRequestAsync(string payload)
    {
        using var req = new UnityWebRequest(chatUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
#if UNITY_2023_1_OR_NEWER
        await req.SendWebRequest();
#else
        var op = req.SendWebRequest();
        while (!op.isDone)
            await Task.Yield();
#endif
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"⚠️ Ollama error: {req.error}");
            return null;
        }
        return req.downloadHandler.text;
    }

    // Helper method to parse the Ollama response and extract AIResult
    private AIResult ParseOllamaResult(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var root = JsonUtility.FromJson<ChatRoot>(raw);
            if (root?.message?.content == null)
            {
                Debug.LogWarning("No message.content in response");
                return null;
            }
            string innerJson = root.message.content.Trim();
            Debug.Log($"📦 Parsed content (model output JSON):\n{innerJson}");
            var result = JsonUtility.FromJson<AIResult>(innerJson);
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ Exception parsing Ollama output: {e.Message}");
            return null;
        }
    }
// Remove stray brace
    [Header("Ollama")]
    public string chatUrl = "http://localhost:11434/api/chat";
    public string model = "gemma3n:latest"; 
    public float temperature = 0.3f;

  public string actionPrompt = "Combine the creatures into a SINGLE hybrid creature with a 1–2 word name.";
  public string mergePrompt = "Pretend the FIRST creature does fuses with the others; produce a SINGLE child creature with a 1–2 word name.";
 

    // Ollama structured output JSON Schema
  readonly string nameSchemaJson = @"
    {
      ""type"": ""object"",
      ""properties"": {
        ""name"":  { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 40 }
      },
      ""required"": [""name""]
    }";

    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ChatRoot { public ChatMessage message; }

    public async Task<AIResult> GenerateAsync(string[] sourceNames, string[] sourceEmojis, int targetTier, MergeMode mode)
    {
        // Step 1: Get merged creature name
        string userPrompt = BuildUserPrompt(sourceNames, targetTier, mode);
        string sys = "You return ONLY the fields described by the provided JSON Schema.";
        string payload = "{"
            + "\"model\": \"" + model + "\"," 
            + "\"messages\": ["
            + "{\"role\": \"system\", \"content\": \"" + Escape(sys) + "\"},"
            + "{\"role\": \"user\", \"content\": \"" + Escape(userPrompt) + "\"}"
            + "],"
            + "\"stream\": false,"
            + "\"format\": " + nameSchemaJson + ","
            + "\"options\": { \"temperature\": " + "1e" + targetTier + " }"
            + "}";

        Debug.Log("📤 Sending to Ollama (step 1):\n" + payload);
        string raw = await SendOllamaRequestAsync(payload);
        Debug.Log($"📥 Raw Ollama response (step 1):\n{raw}");
        AIResult result = ParseOllamaResult(raw);
        if (result == null || string.IsNullOrEmpty(result.name))
        {
            Debug.LogWarning("⚠️ Failed to get merged name from Ollama");
            return Fallback(sourceNames, sourceEmojis, targetTier, mode);
        }
        string mergedName = result.name;

        // Step 2: Ask for 1-4 emojis representing the merged creature
        string emojiPrompt = $"Give 1-4 emoji that best represent the creature named '{mergedName}'. Respond ONLY with emoji, no text.";
            string emojiSchemaJson = "{\"type\":\"object\",\"properties\":{\"emoji\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":4}},\"required\":[\"emoji\"]}";
            string emojiPayload = "{"
                + "\"model\": \"" + model + "\"," 
                + "\"messages\": ["
                + "{\"role\": \"system\", \"content\": \"You return ONLY the fields described by the provided JSON Schema.\"},"
                + "{\"role\": \"user\", \"content\": \"" + Escape(emojiPrompt) + "\"}"
                + "],"
                + "\"stream\": false,"
                + "\"format\": " + emojiSchemaJson + ","
                + "\"options\": { \"temperature\": " + "1e" + targetTier + " }"
                + "}";

        Debug.Log("📤 Sending to Ollama (step 2):\n" + emojiPayload);
        string emojiRaw = await SendOllamaRequestAsync(emojiPayload);
        Debug.Log($"📥 Raw Ollama response (step 2):\n{emojiRaw}");
        AIResult emojiResult = ParseOllamaResult(emojiRaw);
        if (emojiResult != null && !string.IsNullOrEmpty(emojiResult.emoji))
        {
            result.emoji = emojiResult.emoji;
        }
        return result;
    }

    string BuildUserPrompt(string[] n, int tier, MergeMode mode)
    {
        string spice = tier >= 7 ? "become surreal, overflow with dreamlike metaphors."
                    : tier >= 5 ? "allow abstract nouns and folklore hints."
                    : tier >= 3 ? "allow mild mythic metaphors."
                    : "stay concrete and visual.";

        string modeText = mode == MergeMode.Fusion
            ? actionPrompt
            : mergePrompt;

    var list = string.Join(", ", n);
    return $"Tier {tier}. {spice} {modeText} Items: {list}";
    }

    static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    AIResult Fallback(string[] src, string[] emo, int tier, MergeMode mode)
    {
        var name = mode == MergeMode.Fusion
            ? $"{src[0]}-{src[^1]}"
            : $"{src[0]}-{(src.Length > 1 ? src[1] : src[0])}";
        var emoji = (emo != null && emo.Length > 0 && !string.IsNullOrEmpty(emo[0])) ? emo[0] : "??";
        var weight = Mathf.Clamp(2 + tier / 2, 1, 7);
        return new AIResult { emoji = emoji, name = name };
    }

    // Public test method that MUST be awaited
    public async Task TestOllamaConnection()
    {
        Debug.Log("🚀 Testing Ollama connection...");
        var result = await GenerateAsync(
            new[] { "water horse","shadow ninja","Fire Dragon", "Earth Snake", "butt pirate",  "ben sherman"   },
            new[] { "return 2 emoji", "💦🐴","🌚🥷","🐍🌱","🏴‍☠️🍑","🦅🐿️" },
            1,
            MergeMode.Fusion
        );

        if (result != null)
            Debug.Log($"✅ Test Success: {result.name} | {result.emoji}");
        else
            Debug.LogWarning("❌ Test Failed: No result returned.");
    }
}