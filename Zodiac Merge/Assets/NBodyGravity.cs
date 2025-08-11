using UnityEngine;

// Smooth n-body + gentle spring to center
public class NBodyGravity2D : MonoBehaviour
{
    [Header("Gravity")]
    public float G = 0.10f;            // Newtonian constant (tune)
    public float softening = 0.30f;    // avoids singularities
    public float maxAccel = 20f;       // cap per-step accel to reduce jitter

    [Header("Center bias (spring)")]
    public float kSpring = 0.25f;      // small linear spring toward (0,0)

    [Header("Perf")]
    public bool everyOtherStep = false;
    bool toggle;

    void FixedUpdate()
    {
        if (everyOtherStep && (toggle = !toggle)) return;

        var bodies = BubbleRegistry.Active;
        int n = bodies.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            var bi = bodies[i];
            var rbi = bi.GetComponent<Rigidbody2D>();
            if (!rbi || !rbi.simulated) continue;

            Vector2 pi = bi.transform.position;
            Vector2 accel = Vector2.zero;

            // pairwise attraction
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var bj = bodies[j];
                Vector2 d = (Vector2)bj.transform.position - pi;
                float d2 = d.sqrMagnitude + softening * softening;
                Vector2 dir = d / Mathf.Sqrt(d2); // normalized
                float a = G * bj.GravMass / d2;   // a = G*m_other/r^2
                accel += dir * a;
            }

            // gentle spring to center (guarantees inward trend)
            accel += (Vector2.zero - pi) * kSpring;

            // clamp accel to avoid spikes
            if (accel.sqrMagnitude > maxAccel * maxAccel)
                accel = accel.normalized * maxAccel;

            rbi.AddForce(accel * rbi.mass, ForceMode2D.Force);
        }
    }
}
