using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class MergeScanner : MonoBehaviour
{
    public float baseAdjacencyRadius = 1.2f;       // r0 for T2
    public float scanInterval = 0.2f;              // seconds
    public LayerMask bubbleLayer;
    public ParticleSystem burstFx;

    [Header("Refs")]
    public AIClient ai;
    public JsonCanonStore canon;

    float timer;

    void Reset() { bubbleLayer = LayerMask.GetMask("Bubble"); }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= scanInterval) { timer = 0f; TryMergeOnce(); }
    }

    void TryMergeOnce()
    {
        var all = FindObjectsOfType<Bubble>();
        if (all.Length == 0) return;

        // Determine highest target tier possible (look at total points heuristic)
        int maxPoints = all.Sum(b => b.points);
        int bestTarget = Mathf.Clamp(HighestTierFromPoints(maxPoints), 2, 8); // cap at 8 for jam
        for (int target = bestTarget; target >= 2; target--)
        {
            if (TryMergeForTier(all, target)) return; // do one merge per tick
        }
    }

    int HighestTierFromPoints(int sumPoints)
    {
        // find largest N s.t. 2^(N-1) <= sumPoints
        int n = 1; int p = 1;
        while ((p << 1) <= sumPoints) { p <<= 1; n++; }
        return n;
    }

    float R8RadiusForTier(int targetTier)
    {
        int k = targetTier - 2; // T2 uses k=0
        return baseAdjacencyRadius * Mathf.Pow(10f, k / 8f);
    }

    bool TryMergeForTier(Bubble[] all, int targetTier)
    {
        int threshold = 1 << (targetTier - 1);          // points required
        float radius = R8RadiusForTier(targetTier);

        // eligible sources are bubbles with tier < targetTier
        var eligible = all.Where(b => b.tier < targetTier).ToArray();
        if (eligible.Length == 0) return false;

        // pick seed = heaviest/closest to center to encourage merges near well
        Vector3 center = Vector3.zero;
        var seed = eligible.OrderByDescending(b => b.points + b.weight)
                           .ThenBy(b => Vector3.SqrMagnitude(b.transform.position - center))
                           .FirstOrDefault();
        if (!seed) return false;

        // gather neighbors within radius
        List<Bubble> pool = new();
        foreach (var b in eligible)
        {
            if ((b.transform.position - seed.transform.position).sqrMagnitude <= radius * radius)
                pool.Add(b);
        }
        if (pool.Count == 0) return false;

        // greedy minimal subset to reach threshold
        int sum = 0;
        List<Bubble> consume = new();
        foreach (var b in pool.OrderBy(b => Vector3.SqrMagnitude(b.transform.position - seed.transform.position)))
        {
            consume.Add(b);
            sum += b.points;
            if (sum >= threshold) break;
        }
        if (sum < threshold) return false;

        // build combo key (orderless multiset + mode by tier)
        var mode = targetTier <= 2 ? MergeMode.Fusion :
                   (targetTier % 2 == 1 ? MergeMode.Action : MergeMode.Fusion);

        string key = ComboKey(consume.Select(c => c.displayName).ToArray(), mode);
        // run async result
        _ = ResolveAndSpawnAsync(consume, targetTier, mode, key);
        return true;
    }

    async Task ResolveAndSpawnAsync(List<Bubble> consume, int targetTier, MergeMode mode, string key)
    {
        // position & effect seed
        Vector3 pos = Vector3.zero;
        foreach (var b in consume) pos += b.transform.position;
        pos /= consume.Count;

        // particle burst pre-despawn
        if (burstFx)
        {
            var fx = Instantiate(burstFx, pos, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, 3f);
        }

        // outward nudge
        foreach (var b in FindObjectsOfType<Bubble>())
        {
            Vector2 dir = (b.transform.position - pos).normalized;
            b.ApplyImpulse(dir * 0.5f);
        }

        // remove consumed
        foreach (var b in consume) Destroy(b.gameObject);

        // canon lookup / generation
        AIResult res;
        if (!canon.TryGet(key, out res))
        {
            var names = consume.Select(c => c.displayName).ToArray();
            var emos = consume.Select(c => c.emoji).ToArray();
            res = await ai.GenerateAsync(names, emos, targetTier, mode);
            canon.Put(key, targetTier, res);
        }

        // spawn result
        SpawnBubble(res, targetTier, pos);

        // tier events
        GameController.Instance?.OnTierEvent(targetTier);
    }

    void SpawnBubble(AIResult res, int tier, Vector3 pos)
    {
        var prefab = GameController.Instance.bubblePrefab;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        var bub = go.GetComponent<Bubble>();
        string slug = Slugify(res.name);
        bub.Init(slug, res.name, res.emoji, tier, res.weight);
        // small settling impulse
        bub.ApplyImpulse(Random.insideUnitCircle * 0.5f);
    }

    static string ComboKey(string[] names, MergeMode mode)
    {
        var list = names.ToList();
        list.Sort(System.StringComparer.InvariantCultureIgnoreCase);
        string joined = string.Join("+", list);
        return $"{mode}:{joined}";
    }

    static string Slugify(string s)
    {
        s = s.ToLowerInvariant();
        var arr = s.Where(char.IsLetterOrDigit).ToArray();
        return new string(arr);
    }
}
