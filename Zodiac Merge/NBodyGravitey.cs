using UnityEngine;

// Simple n-body gravity: O(n^2), fine for ~100 bodies.
public class NBodyGravity2D : MonoBehaviour
{
    [Tooltip("Gravitational constant (tune with your masses)")]
    public float G = 0.15f;

    [Tooltip("Softening length to avoid singularities")]
    public float softening = 0.25f;

    [Header("Optional center attractor")]
    public bool useCenter = true;
    public float centerMass = 40f;   // 'anchor' mass at (0,0)

    void FixedUpdate()
    {
        var bodies = FindObjectsOfType<Bubble>();
        int n = bodies.Length;
        if (n < 2) { PullToCenterOnly(bodies); return; }

        // pairwise
        for (int i = 0; i < n; i++)
        {
            var bi = bodies[i];
            var rbi = bi.GetComponent<Rigidbody2D>();
            Vector2 pi = bi.transform.position;

            // optional central well
            if (useCenter && centerMass > 0f)
            {
                Vector2 dc = -pi; // center at (0,0)
                float d2c = dc.sqrMagnitude + softening * softening;
                float invDc = 1.0f / Mathf.Sqrt(d2c);
                Vector2 dirC = dc * invDc;
                float Fcmag = G * bi.GravMass * centerMass / d2c;
                rbi.AddForce(dirC * Fcmag, ForceMode2D.Force);
            }

            for (int j = i + 1; j < n; j++)
            {
                var bj = bodies[j];
                var rbj = bj.GetComponent<Rigidbody2D>();

                Vector2 diff = (Vector2)bj.transform.position - pi;
                float d2 = diff.sqrMagnitude + softening * softening;
                float invD = 1.0f / Mathf.Sqrt(d2);
                Vector2 dir = diff * invD;                 // normalized

                // Newton: F = G * m1 * m2 / r^2
                float Fmag = G * bi.GravMass * bj.GravMass / d2;
                Vector2 F = dir * Fmag;

                // equal & opposite
                rbi.AddForce(F, ForceMode2D.Force);
                rbj.AddForce(-F, ForceMode2D.Force);
            }
        }
    }

    void PullToCenterOnly(Bubble[] bodies)
    {
        if (!useCenter || centerMass <= 0f) return;
        foreach (var b in bodies)
        {
            var rb = b.GetComponent<Rigidbody2D>();
            Vector2 p = b.transform.position;
            Vector2 dc = -p;
            float d2c = dc.sqrMagnitude + softening * softening;
            float invDc = 1.0f / Mathf.Sqrt(d2c);
            Vector2 dirC = dc * invDc;
            float Fcmag = G * b.GravMass * centerMass / d2c;
            rb.AddForce(dirC * Fcmag, ForceMode2D.Force);
        }
    }
}
