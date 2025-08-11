using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AIClient))]
public class AIClientEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        // Add a button to test Ollama connection
        if (GUILayout.Button("Test Ollama Connection"))
        {
            ((AIClient)target).TestOllamaConnection();
        }
    }
}
