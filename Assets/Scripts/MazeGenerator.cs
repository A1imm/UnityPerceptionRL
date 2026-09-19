using System.Collections.Generic;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{
    [Header("Wymiary labiryntu")]
    [Tooltip("Liczba komórek w osi X.")]
    [SerializeField] private int gridWidth = 4;

    [Tooltip("Liczba komórek w osi Z.")]
    [SerializeField] private int gridHeight = 4;

    [Tooltip("Rozmiar pojedynczej komórki w metrach.")]
    [SerializeField] private float cellSize = 2.25f;

    [Header("Wygląd ścian labiryntu")]
    [Tooltip("Wysokość generowanych ścian - powinna odpowiadać wysokości " +
             "ścian zewnętrznych areny, żeby uniemożliwić przeskoczenie.")]
    [SerializeField] private float wallHeight = 3f;

    [Tooltip("Grubość generowanych ścian.")]
    [SerializeField] private float wallThickness = 0.15f;

    [Tooltip("Materiał nakładany na wygenerowane ściany (dla spójności " +
             "wizualnej z pozostałymi elementami środowiska).")]
    [SerializeField] private Material wallMaterial;

    [Header("Referencje")]
    [Tooltip("Obiekt celu - jego pozycja lokalna X/Z zostanie ustawiona " +
         "na środek losowo wybranej komórki innej niż startowa.")]
    [SerializeField] private Transform goal;

    public Vector3 StartLocalPosition { get; private set; }

    private readonly List<GameObject> spawnedWalls = new List<GameObject>();

    private bool[,] horizontalWalls;

    private bool[,] verticalWalls;

    private float MazeOriginX => -(gridWidth * cellSize) / 2f;
    private float MazeOriginZ => -(gridHeight * cellSize) / 2f;

    public void GenerateMaze()
    {
        ClearWalls();
        InitWallGrids();
        CarveMaze();
        BuildWallGameObjects();
        PlaceStartAndGoal();
    }

    private void ClearWalls()
    {
        foreach (GameObject wall in spawnedWalls)
        {
            if (wall != null)
            {
                Destroy(wall);
            }
        }
        spawnedWalls.Clear();
    }

    private void InitWallGrids()
    {

        horizontalWalls = new bool[gridWidth, Mathf.Max(gridHeight - 1, 0)];
        verticalWalls = new bool[Mathf.Max(gridWidth - 1, 0), gridHeight];

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight - 1; y++)
            {
                horizontalWalls[x, y] = true;
            }
        }

        for (int x = 0; x < gridWidth - 1; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                verticalWalls[x, y] = true;
            }
        }
    }

    private void CarveMaze()
    {
        bool[,] visited = new bool[gridWidth, gridHeight];
        Stack<Vector2Int> stack = new Stack<Vector2Int>();

        Vector2Int start = new Vector2Int(0, 0);
        visited[start.x, start.y] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            Vector2Int current = stack.Peek();
            List<Vector2Int> unvisitedNeighbors = GetUnvisitedNeighbors(current, visited);

            if (unvisitedNeighbors.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int next = unvisitedNeighbors[Random.Range(0, unvisitedNeighbors.Count)];
            RemoveWallBetween(current, next);
            visited[next.x, next.y] = true;
            stack.Push(next);
        }
    }

    private List<Vector2Int> GetUnvisitedNeighbors(Vector2Int cell, bool[,] visited)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (cell.y > 0 && !visited[cell.x, cell.y - 1])
        {
            result.Add(new Vector2Int(cell.x, cell.y - 1));
        }
        if (cell.y < gridHeight - 1 && !visited[cell.x, cell.y + 1])
        {
            result.Add(new Vector2Int(cell.x, cell.y + 1));
        }
        if (cell.x > 0 && !visited[cell.x - 1, cell.y])
        {
            result.Add(new Vector2Int(cell.x - 1, cell.y));
        }
        if (cell.x < gridWidth - 1 && !visited[cell.x + 1, cell.y])
        {
            result.Add(new Vector2Int(cell.x + 1, cell.y));
        }

        return result;
    }

    private void RemoveWallBetween(Vector2Int a, Vector2Int b)
    {
        if (a.x == b.x)
        {
            int y = Mathf.Min(a.y, b.y);
            horizontalWalls[a.x, y] = false;
        }
        else
        {
            int x = Mathf.Min(a.x, b.x);
            verticalWalls[x, a.y] = false;
        }
    }

    private void BuildWallGameObjects()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight - 1; y++)
            {
                if (horizontalWalls[x, y])
                {
                    Vector3 localPos = new Vector3(
                        MazeOriginX + (x + 0.5f) * cellSize,
                        wallHeight / 2f,
                        MazeOriginZ + (y + 1) * cellSize);

                    Vector3 scale = new Vector3(cellSize + wallThickness, wallHeight, wallThickness);
                    SpawnWallSegment(localPos, scale);
                }
            }
        }

        for (int x = 0; x < gridWidth - 1; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (verticalWalls[x, y])
                {
                    Vector3 localPos = new Vector3(
                        MazeOriginX + (x + 1) * cellSize,
                        wallHeight / 2f,
                        MazeOriginZ + (y + 0.5f) * cellSize);
                    Vector3 scale = new Vector3(wallThickness, wallHeight, cellSize + wallThickness);
                    SpawnWallSegment(localPos, scale);
                }
            }
        }
    }

    private void SpawnWallSegment(Vector3 localPosition, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "MazeWallSegment";
        wall.tag = "wall";
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = scale;

        if (wallMaterial != null)
        {
            wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
        }

        spawnedWalls.Add(wall);
    }

    private void PlaceStartAndGoal()
    {
        Vector2Int startCell = new Vector2Int(0, 0);
        Vector2Int goalCell = PickRandomOtherCell(startCell);

        StartLocalPosition = CellCenterLocalPosition(startCell);

        if (goal != null)
        {
            Vector3 goalCenter = CellCenterLocalPosition(goalCell);
            Vector3 currentGoalLocal = goal.localPosition;
            goal.localPosition = new Vector3(goalCenter.x, currentGoalLocal.y, goalCenter.z);
        }
    }

    public Vector2Int GetCellCoordinates(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);

        int cx = Mathf.Clamp(
            Mathf.FloorToInt((local.x - MazeOriginX) / cellSize), 0, gridWidth - 1);
        int cy = Mathf.Clamp(
            Mathf.FloorToInt((local.z - MazeOriginZ) / cellSize), 0, gridHeight - 1);

        return new Vector2Int(cx, cy);
    }

    private Vector2Int PickRandomOtherCell(Vector2Int startCell)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (cell != startCell)
                {
                    candidates.Add(cell);
                }
            }
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private Vector3 CellCenterLocalPosition(Vector2Int cell)
    {
        return new Vector3(
            MazeOriginX + (cell.x + 0.5f) * cellSize,
            0f,
            MazeOriginZ + (cell.y + 0.5f) * cellSize);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 size = new Vector3(gridWidth * cellSize, 0.02f, gridHeight * cellSize);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}