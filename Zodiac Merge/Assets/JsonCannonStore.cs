using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class JsonCanonStore : MonoBehaviour
{
    [Serializable]
    public class CanonEntry
    {
        public string key;
        public AIResult result;
        public int tier;
        public string created;
    }

    [Serializable]
    class CanonWrapper
    {
        public List<CanonEntry> entries = new();
    }

    public string fileName = "canon.json";
    string PathFile => Path.Combine(Application.persistentDataPath, fileName);
    Dictionary<string, CanonEntry> map = new();

    void Awake() { Load(); }

    public bool TryGet(string key, out AIResult res)
    {
        if (map.TryGetValue(key, out var ce)) { res = ce.result; return true; }
        res = null; return false;
    }

    public void Put(string key, int tier, AIResult res)
    {
        map[key] = new CanonEntry
        {
            key = key,
            tier = tier,
            result = res,
            created = DateTime.UtcNow.ToString("o")
        };
        Save();
    }

    void Save()
    {
        var w = new CanonWrapper { entries = new List<CanonEntry>(map.Values) };
        File.WriteAllText(PathFile, JsonUtility.ToJson(w, true));
    }

    void Load()
    {
        if (!File.Exists(PathFile)) return;
        try
        {
            var json = File.ReadAllText(PathFile);
            var w = JsonUtility.FromJson<CanonWrapper>(json);
            map.Clear();
            if (w?.entries != null)
                foreach (var e in w.entries) map[e.key] = e;
        }
        catch { map.Clear(); }
    }
}
