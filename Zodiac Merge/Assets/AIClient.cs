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
    public string model = "llama3.1"; // any schema-capable model
    public float temperature = 0.3f;   // can drop to 0 for max determinism

    // JSON Schema Ollama will enforce
    // (kept small & forgiving; validate emoji client-side if you want stricter)
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

    public async Task<AIResult> GenerateAsync(
        string[] sourceNames, string[] sourceEmojis, int targetTier, MergeMode mode)
    {
        string userPrompt = BuildUserPrompt(sourceNames, sourceEmojis, targetTier, mode);
        string sys = "You return ONLY the fields described by the provided JSON Schema.";

        var payload = $@"{{
      ""model"": ""{model}"",
      ""messages"": [
        {{""role"": ""system"", ""content"": ""{Escape(sys)}""}},
        {{""role"": ""user"",   ""content"": ""{Escape(userPrompt)}""}}
      ],
      ""stream"": false,
      ""format"": {schemaJson},
      ""options"": {{ ""temperature"": {temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}
    }}";

        using var req = new UnityWebRequest(chatUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"Ollama chat error: {req.error}");
            return Fallback(sourceNames, sourceEmojis, targetTier, mode);
        }

        // Ollama /api/chat returns: { message: { role, content }, ... }
        try
        {
            var root = JsonUtility.FromJson<ChatRoot>(req.downloadHandler.text);
            var json = root.message.content;                 // this is schema-valid JSON
            return JsonUtility.FromJson<AIResult>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Parse fail: {e.Message}");
            return Fallback(sourceNames, sourceEmojis, targetTier, mode);
        }
    }

    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class ChatRoot { public ChatMessage message; }

    string BuildUserPrompt(string[] n, string[] e, int tier, MergeMode mode)
    {
        // Tier “spice” (weirdness ramps by tier, but structure is still enforced by schema)
        string spice = tier >= 7 ? "embrace surreal, dreamlike metaphors."
                    : tier >= 5 ? "allow abstract nouns and folklore hints."
                    : tier >= 3 ? "allow mild mythic metaphors."
                    : "stay concrete and visual.";

        // Mode grammar: Fusion vs Action (A acts on B)
        string modeText = mode == MergeMode.Fusion
          ? "Combine the items into a SINGLE concrete creature/object with a 1–2 word name."
          : "Pretend the FIRST item acts on the others; produce a SINGLE resultant object with a 1–2 word name hinting that action.";

        var list = "";
        for (int i = 0; i < n.Length; i++) list += $"{(i > 0 ? ", " : "")}{(i < e.Length ? e[i] : "")} {n[i]}";

        return $"Tier {tier}. {spice} {modeText} Items: {list}";
    }

    static string Escape(string s) =>
      s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    AIResult Fallback(string[] src, string[] emo, int tier, MergeMode mode)
    {
        var name = mode == MergeMode.Fusion ? $"{src[0]}-{src[^1]}" : $"{src[0]}-{(src.Length > 1 ? src[1] : src[0])}";
        var emoji = (emo != null && emo.Length > 0 && !string.IsNullOrEmpty(emo[0])) ? emo[0] : "??";
        var weight = Mathf.Clamp(2 + tier / 2, 1, 7);
        return new AIResult { emoji = emoji, name = name, gloss = "Locally synthesized.", weight = weight, tags = new[] { "local" } };
    }
}
