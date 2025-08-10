using System.Collections.Generic;
using UnityEngine;

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }

    [Header("Prefab")]
    public GameObject bubblePrefab;

    [Header("Seeds (pick 4 at start)")]
    public List<string> seedNames = new() { "Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake", "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig" };
    public List<string> seedEmojis = new() { "🐀", "🐂", "🐯", "🐇", "🐲", "🐍", "🐎", "🐐", "🐒", "🐓", "🐶", "🐖" };

    [Header("Spawn")]
    public int startCount = 4;
    public float spawnRadius = 3f;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => SpawnSeeds();

    void SpawnSeeds()
    {
        // pick 4 random distinct seeds
        List<int> idxs = new();
        while (idxs.Count < startCount)
        {
            int i = Random.Range(0, Mathf.Min(seedNames.Count, seedEmojis.Count));
            if (!idxs.Contains(i)) idxs.Add(i);
        }
        foreach (var i in idxs)
        {
            Vector2 p = Random.insideUnitCircle.normalized * spawnRadius;
            var go = Instantiate(bubblePrefab, p, Quaternion.identity);
            var b = go.GetComponent<Bubble>();
            string name = seedNames[i];
            string emo = seedEmojis[i];
            b.Init(Slugify(name), name, emo, 1, 3);
            b.ApplyImpulse(Random.insideUnitCircle * 1.5f);
        }
    }

    public void OnTierEvent(int createdTier)
    {
        switch (createdTier)
        {
            case 3: Tier3_Event(); break;
            case 6: Tier6_Event(); break;
            case 7: Tier7_Event(); break;
            case 8: Tier8_Event(); break;
        }
    }

    void Shockwave(float power = 5f)
    {
        var all = FindObjectsOfType<Bubble>();
        foreach (var b in all)
        {
            Vector2 dir = (b.transform.position - Vector3.zero).normalized;
            b.ApplyImpulse(dir * power);
        }
        // TODO: add screen shake / time slow for drama
    }

    void Tier3_Event()
    {
        // gentle slow-mo & mini-shock
        Time.timeScale = 0.8f;
        Shockwave(2f);
        Invoke(nameof(NormalTime), 0.5f);
    }

    void Tier6_Event()
    {
        // big ring shock; transient gravity boost
        Shockwave(6f);
        var gw = FindObjectOfType<GravityWell>();
        if (gw) { float old = gw.pullStrength; gw.pullStrength *= 1.5f; Invoke(nameof(ResetGravity), 1.5f); void ResetGravity() { gw.pullStrength = old; } }
    }

    void Tier7_Event()
    {
        // semantic inversion window could be toggled via a flag checked in MergeScanner (left as TODO)
        // for now: dramatic slow-mo burst
        Time.timeScale = 0.6f;
        Shockwave(8f);
        Invoke(nameof(NormalTime), 0.7f);
    }

    void Tier8_Event()
    {
        // black-hole collapse teaser or massive shock
        Shockwave(12f);
        // TODO optional: spawn a temporary singularity that sucks all bubbles inward
    }

    void NormalTime() => Time.timeScale = 1f;

    static string Slugify(string s)
    {
        s = s.ToLowerInvariant();
        var arr = System.Array.FindAll(s.ToCharArray(), char.IsLetterOrDigit);
        return new string(arr);
    }
}
