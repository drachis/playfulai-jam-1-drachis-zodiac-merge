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
        var all = Object.FindObjectsByType<Bubble>(FindObjectsSortMode.None); // replaced FindObjectsOfType<Bubble>()
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
        float regionR = RadiusForTierRegion(targetTier);

        // eligible sources are bubbles with tier < targetTier
        var eligible = all.Where(b => b.tier < targetTier).ToArray();
        if (eligible.Length == 0) return false;

        // pick a seed (heaviest/closest to center)
        Vector3 center = Vector3.zero;
        var seed = eligible.OrderByDescending(b => b.points + b.weight)
                           .ThenBy(b => Vector3.SqrMagnitude(b.transform.position - center))
                           .FirstOrDefault();
        if (!seed) return false;

        // ?? YOUR BLOCK GOES HERE (neighbor region + visual debug ring)
        Vector3 seedPos = seed.transform.position;

#if UNITY_EDITOR
        DebugDrawCircle(seedPos, regionR, Color.cyan);
#endif

        List<Bubble> pool = new();
        foreach (var b in eligible)
        {
            float d = Vector2.Distance(b.transform.position, seedPos);
            // Match visuals: allow their own size to count a bit
            float allowance = Mathf.Min(b.WorldRadius, 0.75f);
            if (d <= regionR + allowance)
                pool.Add(b);
        }
        if (pool.Count == 0) return false;

        // Greedy minimal subset nearest to seed to reach the threshold
        int sum = 0;
        List<Bubble> consume = new();
        foreach (var b in pool.OrderBy(b => Vector3.SqrMagnitude(b.transform.position - seedPos)))
        {
            consume.Add(b);
            sum += b.points;
            if (sum >= threshold) break;
        }
        if (sum < threshold) return false;

        var mode = targetTier <= 2 ? MergeMode.Fusion :
                   (targetTier % 2 == 1 ? MergeMode.Action : MergeMode.Fusion);

        string key = ComboKey(consume.Select(c => c.displayName).ToArray(), mode);

        _ = ResolveAndSpawnAsync(consume, targetTier, mode, key);
        return true;
    }

    // R8-style region growth for the merge ring around the seed
    float RadiusForTierRegion(int targetTier)
    {
        // T2 uses base, then increase by Renard R8 steps per tier
        return baseAdjacencyRadius * Mathf.Pow(10f, (targetTier - 2) / 8f);
    }

#if UNITY_EDITOR
    void DebugDrawCircle(Vector3 c, float r, Color col, int steps = 64)
    {
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = (i / (float)steps) * Mathf.PI * 2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            Debug.DrawLine(prev, p, col, 0f, false);
            prev = p;
        }
    }
#endif


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
        foreach (var b in Object.FindObjectsByType<Bubble>(FindObjectsSortMode.None)) // replaced FindObjectsOfType<Bubble>()
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

        // if near center, shift out a bit
        Vector2 radial = ((Vector2)pos).sqrMagnitude < 0.01f
            ? (Vector2)Random.insideUnitCircle.normalized
            : ((Vector2)pos).normalized;

        Vector2 spawn = (Vector2)pos + radial * 0.35f;
        var go = Instantiate(prefab, spawn, Quaternion.identity);
        var bub = go.GetComponent<Bubble>();
        string slug = Slugify(res.name);
        bub.Init(slug, res.name, res.emoji, tier, res.weight);

        // tangential kick ~ perpendicular to radial
        Vector2 tangential = new Vector2(-radial.y, radial.x);
        float kick = 1.0f; // tune with G/centerMass
        bub.ApplyImpulse(tangential * kick);

        var rb = go.GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
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
