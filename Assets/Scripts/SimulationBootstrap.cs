using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connects the scene objects with the simulation logic. The arena, hunter,
/// boids and HUD are authored in the Unity Hierarchy; this class only finds
/// those references, initializes them and spawns the temporary interest
/// objects requested by the hunter.
/// </summary>
public class SimulationBootstrap : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private HunterController hunter;
    [SerializeField] private Transform patrolRoute;
    [SerializeField] private Transform interestContainer;
    [SerializeField] private SimulationHUD hud;
    [SerializeField] private Material interestMaterial;
    [SerializeField] private List<BoidAgent> boids = new List<BoidAgent>();
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [Header("Simulation setup")]
    [SerializeField] private int minimumBoidCount = 6;
    [SerializeField] private Vector2 arenaSize = new Vector2(34f, 22f);
    [SerializeField] private int randomSeed = 2026;

    private List<InterestObject> interestObjects = new List<InterestObject>();
    private bool simulationBuilt;

    public static SimulationBootstrap Instance { get; private set; }
    public List<BoidAgent> Boids => boids;
    public List<Transform> Waypoints => waypoints;
    public HunterController Hunter => hunter;
    public int InterestObjectPoolSize => interestObjects.Count;
    public int ActiveInterestCount
    {
        get
        {
            int count = 0;
            foreach (InterestObject interest in interestObjects)
            {
                if (interest != null && interest.IsAvailable)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        BuildSimulation();
    }

    private void BuildSimulation()
    {
        if (simulationBuilt)
        {
            return;
        }

        simulationBuilt = true;
        Random.InitState(randomSeed);
        Application.targetFrameRate = 60;

        DiscoverSceneReferences();
        ValidateSceneReferences();
        ConfigureSceneCamera();

        interestObjects.Clear();

        if (hunter != null)
        {
            hunter.Initialize(this, new List<Transform>(waypoints));
        }

        for (int i = 0; i < boids.Count; i++)
        {
            BoidAgent boid = boids[i];
            if (boid == null)
            {
                continue;
            }

            Color color = Color.HSVToRGB(
                0.5f + (i % 4) * 0.025f,
                0.55f,
                0.95f);
            boid.Initialize(this, i, color);

            Vector3 radialOffset = boid.transform.position - new Vector3(0f, 0f, 2f);
            Vector3 initialDirection = Vector3.Cross(Vector3.up, radialOffset);
            if (initialDirection.sqrMagnitude < 0.01f)
            {
                initialDirection = boid.transform.forward;
            }

            boid.SetInitialVelocity(initialDirection);
        }

        if (hud != null)
        {
            hud.Initialize(this);
        }
    }

    private void ConfigureSceneCamera()
    {
        // La iluminacion, la niebla y los materiales pertenecen a la escena.
        // El bootstrap solo acomoda la camara para que el parcial pueda verse
        // correctamente y no cambia el aspecto global al entrar en Play.
        Camera sceneCamera = Camera.main;
        if (sceneCamera != null)
        {
            sceneCamera.transform.position = new Vector3(0f, 18f, -20f);
            sceneCamera.transform.LookAt(new Vector3(0f, 0f, 1.5f));
            sceneCamera.fieldOfView = 56f;
        }
    }

    private void DiscoverSceneReferences()
    {
        if (hunter == null)
        {
            hunter = FindFirstObjectByType<HunterController>();
        }

        if (hud == null)
        {
            hud = FindFirstObjectByType<SimulationHUD>();
        }

        if (patrolRoute == null)
        {
            GameObject routeObject = GameObject.Find("PatrolRoute");
            if (routeObject != null)
            {
                patrolRoute = routeObject.transform;
            }
        }

        if (interestContainer == null)
        {
            GameObject containerObject = GameObject.Find("InterestObjects");
            if (containerObject != null)
            {
                interestContainer = containerObject.transform;
            }
        }

        BoidAgent[] sceneBoids = FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
        if (sceneBoids.Length > 0)
        {
            boids = new List<BoidAgent>(sceneBoids);
            boids.Sort((first, second) => string.CompareOrdinal(
                first.name,
                second.name));
        }

        if (patrolRoute != null && patrolRoute.childCount > 0)
        {
            waypoints.Clear();
            for (int i = 0; i < patrolRoute.childCount; i++)
            {
                Transform waypoint = patrolRoute.GetChild(i);
                if (waypoint != null)
                {
                    waypoints.Add(waypoint);
                }
            }
        }
    }

    private void ValidateSceneReferences()
    {
        if (hunter == null)
        {
            Debug.LogError("[SimulationBootstrap] Falta un HunterController en la escena.");
        }

        if (boids.Count < minimumBoidCount)
        {
            Debug.LogError(string.Format(
                "[SimulationBootstrap] Se necesitan al menos {0} BoidAgent en la escena y se encontraron {1}.",
                minimumBoidCount,
                boids.Count));
        }

        if (waypoints.Count < 2)
        {
            Debug.LogError("[SimulationBootstrap] PatrolRoute necesita al menos dos waypoints.");
        }

        if (hud == null)
        {
            Debug.LogWarning("[SimulationBootstrap] No se encontro SimulationHUD; la simulacion seguira sin panel.");
        }
    }

    public InterestObject SpawnInterestObject()
    {
        Vector3 position = GetRandomInterestPosition();
        InterestObject reusableInterest = FindReusableInterestObject();
        if (reusableInterest != null)
        {
            reusableInterest.Activate(position, 4f);
            return reusableInterest;
        }

        GameObject interestObject = CreatePrimitive(
            PrimitiveType.Cylinder,
            "Objeto_Interes",
            position,
            new Vector3(0.75f, 0.2f, 0.75f));

        if (interestContainer != null)
        {
            interestObject.transform.SetParent(interestContainer, true);
        }

        InterestObject interest = interestObject.AddComponent<InterestObject>();
        interest.Initialize(4f);
        interestObjects.Add(interest);
        return interest;
    }

    private InterestObject FindReusableInterestObject()
    {
        for (int i = 0; i < interestObjects.Count; i++)
        {
            InterestObject interest = interestObjects[i];
            if (interest == null)
            {
                interestObjects.RemoveAt(i);
                i--;
                continue;
            }

            if (!interest.IsAvailable)
            {
                return interest;
            }
        }

        return null;
    }

    public Vector3 GetRandomBoidSpawnPosition()
    {
        float halfWidth = arenaSize.x * 0.5f - 2f;
        float halfDepth = arenaSize.y * 0.5f - 2f;
        return new Vector3(
            Random.Range(-halfWidth, halfWidth),
            0.7f,
            Random.Range(2f - halfDepth, 2f + halfDepth));
    }

    private Vector3 GetRandomInterestPosition()
    {
        float halfWidth = arenaSize.x * 0.5f - 3f;
        float halfDepth = arenaSize.y * 0.5f - 3f;
        return new Vector3(
            Random.Range(-halfWidth, halfWidth),
            0.35f,
            Random.Range(2f - halfDepth, 2f + halfDepth));
    }

    public Vector3 KeepInsideArena(Vector3 position, ref Vector3 currentVelocity)
    {
        float halfWidth = arenaSize.x * 0.5f - 1.1f;
        float halfDepth = arenaSize.y * 0.5f - 1.1f;
        float minZ = 2f - halfDepth;
        float maxZ = 2f + halfDepth;

        if (position.x < -halfWidth)
        {
            position.x = -halfWidth;
            currentVelocity.x = Mathf.Abs(currentVelocity.x);
        }
        else if (position.x > halfWidth)
        {
            position.x = halfWidth;
            currentVelocity.x = -Mathf.Abs(currentVelocity.x);
        }

        if (position.z < minZ)
        {
            position.z = minZ;
            currentVelocity.z = Mathf.Abs(currentVelocity.z);
        }
        else if (position.z > maxZ)
        {
            position.z = maxZ;
            currentVelocity.z = -Mathf.Abs(currentVelocity.z);
        }

        return position;
    }

    public int CountAliveBoids()
    {
        int count = 0;
        foreach (BoidAgent boid in boids)
        {
            if (boid != null && boid.IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    public int CountInactiveBoids()
    {
        int count = 0;
        foreach (BoidAgent boid in boids)
        {
            if (boid != null && boid.IsDead)
            {
                count++;
            }
        }

        return count;
    }

    public int CountCollectedBoids()
    {
        int count = 0;
        foreach (BoidAgent boid in boids)
        {
            if (boid != null && boid.IsCollected)
            {
                count++;
            }
        }

        return count;
    }

    private GameObject CreatePrimitive(
        PrimitiveType primitiveType,
        string objectName,
        Vector3 position,
        Vector3 scale)
    {
        GameObject createdObject = GameObject.CreatePrimitive(primitiveType);
        createdObject.name = objectName;
        createdObject.transform.position = position;
        createdObject.transform.localScale = scale;

        Renderer objectRenderer = createdObject.GetComponent<Renderer>();
        if (objectRenderer != null)
        {
            if (interestMaterial != null)
            {
                objectRenderer.sharedMaterial = interestMaterial;
            }
            else
            {
                Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpLitShader != null)
                {
                    objectRenderer.sharedMaterial = new Material(urpLitShader);
                }
            }
        }

        return createdObject;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.7f, 1f, 0.2f);
        Gizmos.DrawWireCube(
            new Vector3(0f, 0.05f, 2f),
            new Vector3(arenaSize.x, 0.1f, arenaSize.y));

        if (waypoints == null)
        {
            return;
        }

        Gizmos.color = new Color(0.55f, 0.25f, 1f, 0.7f);
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            if (waypoints[i] != null && waypoints[i + 1] != null)
            {
                Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
            }
        }
    }
}
