using UnityEngine;

public class GravityWell : MonoBehaviour
{
    public float pullStrength = 2.5f;  // overall gravity

    void FixedUpdate()
    {
        var bubbles = GameObject.FindObjectsByType<Bubble>(FindObjectsSortMode.None);
        Vector3 center = Vector3.zero; // world origin as well center
        foreach (var b in bubbles)
        {
            var rb = b.GetComponent<Rigidbody2D>();
            Vector2 dir = (center - b.transform.position);
            float dist = dir.magnitude; // Remove minimum clamp for true gravity
            float force = pullStrength * b.AttractorMass;
            rb.AddForce(dir.normalized * force);
        }
    }
}
