using UnityEngine;
using TMPro;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Bubble : MonoBehaviour
{
    [Header("Runtime")]
    public string id;            // stable canon id (slug)
    public string displayName;   // LLM name
    public string emoji = "??";
    public int tier = 1;         // T1..T8+
    public int points = 1;       // 2^(tier-1)
    public int weight = 3;       // 1..7 from LLM; affects attraction

    [Header("Refs")]
    public TextMeshProUGUI label;

    [Header("Tuning")]
    public float baseRadius = 0.4f;     // R8 base size (T1)
    public float spriteScale = 1.0f;

    Rigidbody2D rb;
    CircleCollider2D col;
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        if (!label) label = GetComponentInChildren<TextMeshProUGUI>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        UpdateVisuals();
    }

    public void Init(string id, string name, string emoji, int tier, int weight)
    {
        this.id = id;
        this.displayName = name;
        this.emoji = string.IsNullOrEmpty(emoji) ? "??" : emoji;
        this.weight = Mathf.Clamp(weight, 1, 7);
        SetTier(tier);
        RefreshMass();
        UpdateVisuals();
    }

    public void SetTier(int t)
    {
        tier = Mathf.Max(1, t);
        points = 1 << (tier - 1);
        float r = baseRadius * Mathf.Pow(10f, (tier - 1) / 8f); // R8
        col.radius = r;
        transform.localScale = Vector3.one * (r * spriteScale);
        RefreshMass();
    }

    public void RefreshMass()
    {
        // gravitational & inertial mass for 2D physics
        float m = Mathf.Max(0.1f, weight * points);
        rb.mass = m;
    }

    public float GravMass => rb.mass; // convenience


    public void UpdateVisuals()
    {
        if (label)
        {
            label.text = $"{emoji}\n<size=60%>{points}</size>";
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    public void ApplyImpulse(Vector2 impulse) => rb.AddForce(impulse, ForceMode2D.Impulse);

    // Simple “attractor mass” used by GravityWell
    public float AttractorMass => Mathf.Max(0.5f, weight) * points;
}
