using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

public class CombinedAreaGenerator : MonoBehaviour
{
    private enum WallOrientation { Horizontal, Vertical }

    private struct WallSlot
    {
        public WallOrientation orientation;
        public int x;
        public int y;
    }

    [Header("Wymiary labiryntu")]
    [SerializeField] private int gridWidth = 4;
    [SerializeField] private int gridHeight = 4;
    [SerializeField] private float cellSize = 2.25f;

    [Header("Wygląd ścian")]
    [SerializeField] private float wallHeight = 3f;
    [SerializeField] private float wallThickness = 0.15f;
    [SerializeField] private Material wallMaterial;

    [Header("Przeszkody (kwadratowe platformy w miejscu ścian)")]
    [SerializeField] private Material hazardMaterial;
    [SerializeField] private int hazardCount = 2;
    [SerializeField] private float hazardSize = 0.5f;

    [Tooltip("Wysokość przeszkody - ustawiona wyraźnie powyżej " +
             "maksymalnego zasięgu skoku agenta (~0,8 m przy " +
             "jumpSpeed=4), żeby uniemożliwić jej przeskoczenie, ale " +
             "niższa niż pełna wysokość ściany (3 m) dla odróżnienia " +
             "wizualnego. Wcześniej testowano niższą wersję (0,5 m, " +
             "teoretycznie przeskakiwalną), ale doprowadziło to do " +
             "niepożądanego zachowania - agent próbował niedopracowanych, " +
             "powtarzalnych prób skoku zamiast nauczenia się skutecznego " +
             "omijania przeszkody.")]
    [SerializeField] private float hazardHeight = 2f;

    [Header("Obiekt do zebrania (platforma cylindryczna + kula)")]
    [Tooltip("Nadrzędny obiekt platformy - jego pozycja X/Z zostanie " +
             "ustawiona na środek wybranej komórki. Obiekt do zebrania " +
             "(kula) powinien być jego dzieckiem, tak jak w środowisku 1.")]
    [SerializeField] private Transform collectiblePlatform;

    [Header("Cel (sama kula)")]
    [SerializeField] private Transform goal;

    public Vector3 StartLocalPosition { get; private set; }

    private readonly List<GameObject> spawnedWalls = new List<GameObject>();

    private bool[,] horizontalWalls;
    private bool[,] verticalWalls;

    private float MazeOriginX => -(gridWidth * cellSize) / 2f;
    private float MazeOriginZ => -(gridHeight * cellSize) / 2f;

    public void GenerateArea()
    {
        ApplyCurriculumDifficulty();

        ClearWalls();
        InitWallGrids();
        CarveMaze();

        Vector2Int startCell = new Vector2Int(0, 0);
        StartLocalPosition = CellCenterLocalPosition(startCell);

        List<WallSlot> allWallSlots = CollectAllWallSlots();
        Shuffle(allWallSlots);

        Vector2Int collectibleCell = PickCellAdjacentToRandomWall(allWallSlots, startCell);

        Vector2Int goalCell = PickRandomCell(new List<Vector2Int> { startCell, collectibleCell });

        List<WallSlot> hazardCandidates = new List<WallSlot>();
        foreach (WallSlot slot in allWallSlots)
        {
            (Vector2Int cellA, Vector2Int cellB) = GetSlotCells(slot);
            bool touchesCollectible = cellA == collectibleCell || cellB == collectibleCell;
            bool touchesGoal = cellA == goalCell || cellB == goalCell;

            if (!touchesCollectible && !touchesGoal)
            {
                hazardCandidates.Add(slot);
            }
        }

        List<WallSlot> hazardSlots = PickSeparatedHazardSlots(hazardCandidates, hazardCount);
        List<WallSlot> remainingSlots = new List<WallSlot>();
        foreach (WallSlot slot in allWallSlots)
        {
            if (!hazardSlots.Contains(slot))
            {
                remainingSlots.Add(slot);
            }
        }

        BuildWalls(hazardSlots, remainingSlots);
        PlaceCollectiblePlatform(collectibleCell);
        PlaceGoal(goalCell);
    }

    private void ApplyCurriculumDifficulty()
    {
        const float defaultLevel = 3f;
        float rawLevel = defaultLevel;

        if (Academy.IsInitialized)
        {
            rawLevel = Academy.Instance.EnvironmentParameters.GetWithDefault(
                "maze_difficulty", defaultLevel);
        }

        int level = Mathf.RoundToInt(Mathf.Clamp(rawLevel, 0f, 3f));

        switch (level)
        {
            case 0:
                gridWidth = 2;
                gridHeight = 2;
                hazardCount = 0;
                break;
            case 1:
                gridWidth = 3;
                gridHeight = 3;
                hazardCount = 0;
                break;
            case 2:
                gridWidth = 3;
                gridHeight = 3;
                hazardCount = 1;
                break;
            default:
                gridWidth = 4;
                gridHeight = 4;
                hazardCount = 2;
                break;
        }
    }

    private void ClearWalls()
    {
        foreach (GameObject wall in spawnedWalls)
        {
            if (wall != null) Destroy(wall);
        }
        spawnedWalls.Clear();
    }

    private void InitWallGrids()
    {
        horizontalWalls = new bool[gridWidth, Mathf.Max(gridHeight - 1, 0)];
        verticalWalls = new bool[Mathf.Max(gridWidth - 1, 0), gridHeight];

        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight - 1; y++)
                horizontalWalls[x, y] = true;

        for (int x = 0; x < gridWidth - 1; x++)
            for (int y = 0; y < gridHeight; y++)
                verticalWalls[x, y] = true;
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
            result.Add(new Vector2Int(cell.x, cell.y - 1));
        if (cell.y < gridHeight - 1 && !visited[cell.x, cell.y + 1])
            result.Add(new Vector2Int(cell.x, cell.y + 1));
        if (cell.x > 0 && !visited[cell.x - 1, cell.y])
            result.Add(new Vector2Int(cell.x - 1, cell.y));
        if (cell.x < gridWidth - 1 && !visited[cell.x + 1, cell.y])
            result.Add(new Vector2Int(cell.x + 1, cell.y));

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

    private List<WallSlot> CollectAllWallSlots()
    {
        List<WallSlot> slots = new List<WallSlot>();

        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight - 1; y++)
                if (horizontalWalls[x, y])
                    slots.Add(new WallSlot { orientation = WallOrientation.Horizontal, x = x, y = y });

        for (int x = 0; x < gridWidth - 1; x++)
            for (int y = 0; y < gridHeight; y++)
                if (verticalWalls[x, y])
                    slots.Add(new WallSlot { orientation = WallOrientation.Vertical, x = x, y = y });

        return slots;
    }

    private void BuildWalls(List<WallSlot> hazardSlots, List<WallSlot> normalSlots)
    {
        foreach (WallSlot slot in hazardSlots)
        {
            BuildHazardPlatform(slot);
        }

        foreach (WallSlot slot in normalSlots)
        {
            BuildWallSegment(slot);
        }
    }

    private List<WallSlot> PickSeparatedHazardSlots(List<WallSlot> shuffledSlots, int count)
    {
        List<WallSlot> selected = new List<WallSlot>();
        HashSet<Vector2Int> usedCells = new HashSet<Vector2Int>();

        foreach (WallSlot slot in shuffledSlots)
        {
            if (selected.Count >= count)
            {
                break;
            }

            (Vector2Int cellA, Vector2Int cellB) = GetSlotCells(slot);

            if (usedCells.Contains(cellA) || usedCells.Contains(cellB))
            {
                continue;
            }

            selected.Add(slot);
            usedCells.Add(cellA);
            usedCells.Add(cellB);
        }

        return selected;
    }

    private (Vector2Int, Vector2Int) GetSlotCells(WallSlot slot)
    {
        if (slot.orientation == WallOrientation.Horizontal)
        {
            return (new Vector2Int(slot.x, slot.y), new Vector2Int(slot.x, slot.y + 1));
        }

        return (new Vector2Int(slot.x, slot.y), new Vector2Int(slot.x + 1, slot.y));
    }

    private void BuildHazardPlatform(WallSlot slot)
    {
        Vector3 center = WallSlotCenterPosition(slot);

        GameObject hazard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hazard.name = "Hazard";
        hazard.tag = "hazard";
        hazard.transform.SetParent(transform, false);
        hazard.transform.localPosition = new Vector3(center.x, hazardHeight / 2f, center.z);
        hazard.transform.localScale = new Vector3(hazardSize, hazardHeight, hazardSize);

        if (hazardMaterial != null)
        {
            hazard.GetComponent<Renderer>().sharedMaterial = hazardMaterial;
        }

        spawnedWalls.Add(hazard);
    }

    private void BuildWallSegment(WallSlot slot)
    {
        Vector3 localPos;
        Vector3 scale;

        if (slot.orientation == WallOrientation.Horizontal)
        {
            localPos = new Vector3(
                MazeOriginX + (slot.x + 0.5f) * cellSize,
                wallHeight / 2f,
                MazeOriginZ + (slot.y + 1) * cellSize);
            scale = new Vector3(cellSize + wallThickness, wallHeight, wallThickness);
        }
        else
        {
            localPos = new Vector3(
                MazeOriginX + (slot.x + 1) * cellSize,
                wallHeight / 2f,
                MazeOriginZ + (slot.y + 0.5f) * cellSize);
            scale = new Vector3(wallThickness, wallHeight, cellSize + wallThickness);
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.tag = "wall";
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = scale;

        if (wallMaterial != null)
        {
            wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
        }

        spawnedWalls.Add(wall);
    }

    private Vector3 WallSlotCenterPosition(WallSlot slot)
    {
        return slot.orientation == WallOrientation.Horizontal
            ? new Vector3(MazeOriginX + (slot.x + 0.5f) * cellSize, 0f, MazeOriginZ + (slot.y + 1) * cellSize)
            : new Vector3(MazeOriginX + (slot.x + 1) * cellSize, 0f, MazeOriginZ + (slot.y + 0.5f) * cellSize);
    }

    private Vector2Int PickCellAdjacentToRandomWall(List<WallSlot> slots, Vector2Int excludeCell)
    {
        WallSlot slot = slots[Random.Range(0, slots.Count)];

        Vector2Int cellA;
        Vector2Int cellB;

        if (slot.orientation == WallOrientation.Horizontal)
        {
            cellA = new Vector2Int(slot.x, slot.y);
            cellB = new Vector2Int(slot.x, slot.y + 1);
        }
        else
        {
            cellA = new Vector2Int(slot.x, slot.y);
            cellB = new Vector2Int(slot.x + 1, slot.y);
        }

        if (cellA == excludeCell) return cellB;
        if (cellB == excludeCell) return cellA;

        return Random.value < 0.5f ? cellA : cellB;
    }

    private Vector2Int PickRandomCell(List<Vector2Int> excluded)
    {
        Vector2Int cell;
        do
        {
            cell = new Vector2Int(Random.Range(0, gridWidth), Random.Range(0, gridHeight));
        }
        while (excluded.Contains(cell));

        return cell;
    }

    private void PlaceCollectiblePlatform(Vector2Int cell)
    {
        if (collectiblePlatform == null) return;

        Vector3 center = CellCenterLocalPosition(cell);
        Vector3 current = collectiblePlatform.localPosition;
        collectiblePlatform.localPosition = new Vector3(center.x, current.y, center.z);
    }

    private void PlaceGoal(Vector2Int cell)
    {
        if (goal == null) return;

        Vector3 center = CellCenterLocalPosition(cell);
        Vector3 current = goal.localPosition;
        goal.localPosition = new Vector3(center.x, current.y, center.z);
    }

    private void Shuffle(List<WallSlot> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private Vector3 CellCenterLocalPosition(Vector2Int cell)
    {
        return new Vector3(
            MazeOriginX + (cell.x + 0.5f) * cellSize,
            0f,
            MazeOriginZ + (cell.y + 0.5f) * cellSize);
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 size = new Vector3(gridWidth * cellSize, 0.02f, gridHeight * cellSize);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}