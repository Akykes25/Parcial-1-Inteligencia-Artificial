using UnityEngine;

/// <summary>
/// Simple in-game feedback panel. The colors in the world and this panel make
/// the current FSM state and the main steering decisions easy to identify.
/// </summary>
public class SimulationHUD : MonoBehaviour
{
    private SimulationBootstrap simulation;
    private GUIStyle headerStyle;
    private GUIStyle bodyStyle;
    private GUIStyle smallStyle;
    private bool stylesReady;

    public void Initialize(SimulationBootstrap owner)
    {
        simulation = owner;
    }

    private void OnGUI()
    {
        if (simulation == null || simulation.Hunter == null)
        {
            return;
        }

        EnsureStyles();
        HunterController hunter = simulation.Hunter;
        int activeBoids = simulation.CountAliveBoids();
        int inactiveBoids = simulation.CountInactiveBoids();
        int collectedBoids = simulation.CountCollectedBoids();
        int detectedBoids = hunter.CountAliveBoidsInVision();

        GUI.color = new Color(0.02f, 0.04f, 0.09f, 0.94f);
        GUI.Box(new Rect(16f, 16f, 510f, 302f), GUIContent.none);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(32f, 29f, 478f, 280f));
        GUILayout.Label("PARCIAL 1 IA - BOIDS + FSM", headerStyle);
        GUILayout.Label(
            "NPC Cazador | Estado FSM: " + hunter.CurrentStateName,
            bodyStyle);
        GUILayout.Label("Accion: " + hunter.CurrentAction, bodyStyle);
        GUILayout.Label(
            "TBA restante: " + hunter.TBARemaining.ToString("0.0")
            + "s | Ataques: " + hunter.AttackCount,
            bodyStyle);
        GUILayout.Label(
            "Objetivo ataque: " + GetTargetName(hunter.CurrentTarget),
            bodyStyle);
        GUILayout.Label(
            "Objeto interes: " + GetInterestName(hunter.CurrentObjective)
            + " | Activos: " + simulation.ActiveInterestCount + "/5",
            bodyStyle);
        GUILayout.Label(
            "Boids activos: " + activeBoids
            + " | Inactivos: " + inactiveBoids
            + " | Recolectando: " + collectedBoids
            + " | Total: " + simulation.Boids.Count,
            bodyStyle);
        GUILayout.Label("Detectados por hunter: " + detectedBoids, bodyStyle);
        GUILayout.Label(
            "FSM: " + hunter.LastTransition
            + " | Gather: " + hunter.GatherCount,
            smallStyle);
        GUILayout.Label(
            "Boids: " + CountState(BoidBehaviourState.Flocking)
            + " Flocking / " + CountState(BoidBehaviourState.Arrive)
            + " Arrive / " + CountState(BoidBehaviourState.Evade)
            + " Evade",
            smallStyle);
        GUILayout.Label(
            "Colores: rojo Patrol | naranja Attack | verde Gather | "
            + "amarillo Arrive | magenta Evade",
            smallStyle);
        GUILayout.EndArea();

        GUI.color = new Color(0.02f, 0.04f, 0.09f, 0.86f);
        GUI.Box(new Rect(Screen.width - 310f, 16f, 294f, 105f), GUIContent.none);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(Screen.width - 298f, 28f, 270f, 86f));
        GUILayout.Label("Sensores locales", bodyStyle);
        GUILayout.Label(
            "Boid vision: limitada a " + GetBoidHunterVisionSummary(),
            smallStyle);
        GUILayout.Label(
            "Hunter vision/retencion: "
            + hunter.VisionRadius.ToString("0.##") + "m / "
            + hunter.AttackRetentionRadius.ToString("0.##") + "m",
            smallStyle);
        GUILayout.Label("Separacion < Alineacion/Cohesion", smallStyle);
        GUILayout.EndArea();
    }

    private int CountState(BoidBehaviourState state)
    {
        int count = 0;
        foreach (BoidAgent boid in simulation.Boids)
        {
            if (boid != null && boid.BehaviourState == state)
            {
                count++;
            }
        }

        return count;
    }

    private string GetTargetName(BoidAgent target)
    {
        if (target == null || target.IsCollected)
        {
            return "ninguno";
        }

        return target.name + " (vida " + target.Health.ToString("0") + ")";
    }

    private string GetInterestName(InterestObject interest)
    {
        if (interest == null || !interest.IsAvailable)
        {
            return "ninguno";
        }

        return interest.name + " (vida " + interest.Life.ToString("0.0") + ")";
    }

    private string GetBoidHunterVisionSummary()
    {
        float minimumRadius = float.MaxValue;
        float maximumRadius = float.MinValue;
        int validBoids = 0;

        foreach (BoidAgent boid in simulation.Boids)
        {
            if (boid == null)
            {
                continue;
            }

            minimumRadius = Mathf.Min(minimumRadius, boid.HunterDetectionRadius);
            maximumRadius = Mathf.Max(maximumRadius, boid.HunterDetectionRadius);
            validBoids++;
        }

        if (validBoids == 0)
        {
            return "sin Boids";
        }

        if (Mathf.Approximately(minimumRadius, maximumRadius))
        {
            return minimumRadius.ToString("0.##") + "m";
        }

        return minimumRadius.ToString("0.##")
            + "-" + maximumRadius.ToString("0.##") + "m";
    }

    private void EnsureStyles()
    {
        if (stylesReady)
        {
            return;
        }

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.25f, 0.85f, 1f) }
        };

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };

        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = new Color(0.7f, 0.78f, 0.9f) }
        };

        stylesReady = true;
    }
}
