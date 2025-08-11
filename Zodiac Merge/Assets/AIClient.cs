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
    [Header("Ollama")]
    public string chatUrl = "http://localhost:11434/api/chat";
    public string model = "gemma3n:latest"; 
    public float temperature = 0.3f;  

    // Ollama structured output JSON Schema
    readonly string schemaJson = @"
    {
      ""type"": ""object"",
      ""properties"": {
        ""emoji"": { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 4 },
        ""name"":  { ""type"": ""string"", ""minLength"": 1, ""maxLength"": 40 },
        ""gloss"": { ""type"": ""string"", ""minLength"": 4, ""maxLength"": 160 },
        ""weight"":{ ""type"": ""integer"", ""minimum"": 1, ""maximum"": 7 },
        ""tags"":  { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""default"": [] }
      },
      ""required"": [""emoji"", ""name"", ""gloss"", ""weight""]
    }";

    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ChatRoot { public ChatMessage message; }

    public async Task<AIResult> GenerateAsync(string[] sourceNames, string[] sourceEmojis, int targetTier, MergeMode mode)
    {
        string userPrompt = BuildUserPrompt(sourceNames, sourceEmojis, targetTier, mode);
        string sys = "You return ONLY the fields described by the provided JSON Schema.";

        // NOTE: Here we do not escape schemaJson — it's a raw JSON object
        string payload = $@"{{
          ""model"": ""{model}"",
          ""messages"": [
            {{""role"": ""system"", ""content"": ""{Escape(sys)}""}},
            {{""role"": ""user"",   ""content"": ""{Escape(userPrompt)}""}}
          ],
          ""stream"": false,
          ""format"": {schemaJson},
          ""options"": {{ ""temperature"": {temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}
        }}";

        Debug.Log("📤 Sending to Ollama:\n" + payload);

        using var req = new UnityWebRequest(chatUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        // Wait for completion properly
#if UNITY_2023_1_OR_NEWER
        await req.SendWebRequest();
#else
        var op = req.SendWebRequest();
        while (!op.isDone)
            await Task.Yield();
#endif

        Debug.Log($"📥 Response code: {req.responseCode}");
        string raw = req.downloadHandler.text;
        Debug.Log($"📥 Raw Ollama response:\n{raw}");

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"⚠️ Ollama error: {req.error}");
            return Fallback(sourceNames, sourceEmojis, targetTier, mode);
        }

        try
        {
            // Outer API wrapper
            var root = JsonUtility.FromJson<ChatRoot>(raw);
            if (root?.message?.content == null)
            {
                Debug.LogWarning("No message.content in response");
                return Fallback(sourceNames, sourceEmojis, targetTier, mode);
            }

            // Inner JSON (model output)
            string innerJson = root.message.content.Trim();
            Debug.Log($"📦 Parsed content (model output JSON):\n{innerJson}");

            // Parse into AIResult
            var result = JsonUtility.FromJson<AIResult>(innerJson);
            if (result == null)
            {
                Debug.LogWarning("⚠️ Failed to parse AIResult from inner JSON");
                return Fallback(sourceNames, sourceEmojis, targetTier, mode);
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ Exception parsing Ollama output: {e.Message}");
            return Fallback(sourceNames, sourceEmojis, targetTier, mode);
        }
    }

    string BuildUserPrompt(string[] n, string[] e, int tier, MergeMode mode)
    {
        string spice = tier >= 7 ? "embrace surreal, dreamlike metaphors."
                    : tier >= 5 ? "allow abstract nouns and folklore hints."
                    : tier >= 3 ? "allow mild mythic metaphors."
                    : "stay concrete and visual.";

        string modeText = mode == MergeMode.Fusion
            ? "Combine the items into a SINGLE concrete creature/object with a 1–2 word name."
            : "Pretend the FIRST item acts on the others; produce a SINGLE resultant object with a short action‑hinting name.";

        var list = "";
        for (int i = 0; i < n.Length; i++)
            list += $"{(i > 0 ? ", " : "")}{(i < e.Length ? e[i] : "")} {n[i]}";

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
        return new AIResult { emoji = emoji, name = name, gloss = "Locally synthesized.", weight = weight, tags = new[] { "local" } };
    }

    // Public test method that MUST be awaited
    public async Task TestOllamaConnection()
    {
        Debug.Log("🚀 Testing Ollama connection...");
        var result = await GenerateAsync(
            new[] { "TestItem1", "TestItem2" },
            new[] { "😀", "🚀" },
            5,
            MergeMode.Fusion
        );

        if (result != null)
            Debug.Log($"✅ Test Success: {result.name} | {result.emoji}");
        else
            Debug.LogWarning("❌ Test Failed: No result returned.");
    }
}