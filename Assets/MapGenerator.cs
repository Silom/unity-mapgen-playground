using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class MapGenerator : MonoBehaviour
{
    public int height;
    public int width;

    public string seed;
    public bool useRandomSeed;

    // amount of tiles a solid rock formation must have in order to be drawn into the map
    [Range(1, 200)]
    public int minimalSolidRegionSize;
    [Range(1, 200)]
    public int minimalEmptyRegionSize;
    // 0 no cennections, 1 cave clusters, 2 guarantee connections between all caves
    [Range(0, 2)]
    public int caveConnectivity;

    // smoothing corners and elevations
    [Range(1, 15)]
    public int smoothIntervals;

    // Rock:Air ratio. Cave systems between 50 - 60, lots of air below that, lots of rock above that.
    [Range(0, 100)]
    public int randomFillPercentage;

    // use perlin noise to generate a map shape and iterate over the map for smoothing
    int[,] map;

    // Start is called before the first frame update
    void Start()
    {
        GenerateMap();
    }

    void GenerateMap()
    {
        map = new int[width, height];

        RandomFillMap();

        for (int i = 0; i < smoothIntervals; i++)
        {
            SmoothMap();
        }

        ProcessMap();

        int borderSize = 1;
        int[,] borderdMap = new int[width + borderSize * 2, height + borderSize * 2];

        for (int x = 0; x < borderdMap.GetLength(0); x++)
        {
            for (int y = 0; y < borderdMap.GetLength(1); y++)
            {
                if (x >= borderSize && x < width + borderSize && y >= borderSize && y < height + borderSize)
                {
                    borderdMap[x, y] = map[x - borderSize, y - borderSize];
                } else
                {
                    borderdMap[x, y] = 1;
                }
            }
        }

        // after smoothing we turn the map into a mesh which includes a new level of smoothing (marching squares)
        MeshGenerator meshGen = GetComponent<MeshGenerator>();
        meshGen.GenerateMesh(map, 1);
    }

    void ProcessMap()
    {
        // Wall/solid regions
        List<List<Coord>> solidRegions = GetRegions(1);

        // This will remove any small caves
        foreach (List<Coord> solidRegion in solidRegions)
        {
            if (solidRegion.Count < minimalSolidRegionSize)
            {
                foreach(Coord tile in solidRegion)
                {
                    // fill with empty
                    map[tile.tileX, tile.tileY] = 0;
                }
            }
        }

        // Cave regions
        List<List<Coord>> caveRegions = GetRegions(0);
        List<Cave> remainingCaves = new List<Cave>();

        // This will remove any small caves
        foreach (List<Coord> caveRegion in caveRegions)
        {
            if (caveRegion.Count < minimalEmptyRegionSize)
            {
                foreach (Coord tile in caveRegion)
                {
                    // fill with solid
                    map[tile.tileX, tile.tileY] = 1;
                }
            } else
            {
                remainingCaves.Add(new Cave(caveRegion, map));
            }
        }

        // setup caves to be sorted by containing tile size and give the first cave the attribute to be the main room
        // could function as spawn but mainly the goal is to offer the map gen to ability to connect with all caves into one big cave system
        remainingCaves.Sort();
        remainingCaves[0].isMainCave = true;
        remainingCaves[0].isAccessibleFromMainCave = true;
        
        if (caveConnectivity > 0)
        {
            ConnectClosestCave(remainingCaves);
        }
    }


    // check all caves and draw a line between them with empty space
    void ConnectClosestCave(List<Cave> allCaves, bool forceAccessibilityFromMainCave = false)
    {
        List<Cave> caveListA = new List<Cave>();
        List<Cave> caveListB = new List<Cave>();

        if (caveConnectivity > 1 && forceAccessibilityFromMainCave)
        {
            foreach (Cave cave in allCaves)
            {
                if (cave.isAccessibleFromMainCave)
                {
                    caveListB.Add(cave);
                } else
                {
                    caveListA.Add(cave);
                }
            }
        } else
        {
            caveListA = allCaves;
            caveListB = allCaves;
        }

        int idealDistance = 0;
        Coord idealTileA = new Coord();
        Coord idealTileB = new Coord();
        Cave idealCaveA = new Cave();
        Cave idealCaveB = new Cave();
        bool foundConnection = false;

        foreach (Cave caveA in caveListA)
        {
            if (!forceAccessibilityFromMainCave)
            {
                foundConnection = false;
                // omit existing connection
                if (caveA.connectedCaves.Count > 0)
                {
                    continue;
                }
            }

            foreach (Cave caveB in caveListB)
            {
                if (caveA == caveB || caveA.IsConnected(caveB))
                {
                    continue;
                }

                for (int tileIndexA = 0; tileIndexA < caveA.edgeTiles.Count; tileIndexA++)
                {
                    for (int tileIndexB = 0; tileIndexB < caveB.edgeTiles.Count; tileIndexB++)
                    {
                        Coord tileA = caveA.edgeTiles[tileIndexA];
                        Coord tileB = caveB.edgeTiles[tileIndexB];
                        // combine the square root for both coords in order to determine distance
                        int distanceBetweenRooms = (int)(Mathf.Pow(tileA.tileX - tileB.tileX, 2) + Mathf.Pow(tileA.tileY - tileB.tileY, 2));

                        if (distanceBetweenRooms < idealDistance || !foundConnection)
                        {
                            idealDistance = distanceBetweenRooms;
                            foundConnection = true;
                            idealTileA = tileA;
                            idealTileB = tileB;
                            idealCaveA = caveA;
                            idealCaveB = caveB;
                        }
                    }
                }
            }
            if (foundConnection && !forceAccessibilityFromMainCave)
            {
                CreateCavePassage(idealCaveA, idealCaveB, idealTileA, idealTileB);
            }
        }

        // if force is on we need to link the other cave system with this one (the main cave system)
        if (foundConnection && caveConnectivity > 1 && forceAccessibilityFromMainCave)
        {
            CreateCavePassage(idealCaveA, idealCaveB, idealTileA, idealTileB);
            ConnectClosestCave(allCaves, true);
        }

        // this will redo this function in case some connections are not yet established
        if (!forceAccessibilityFromMainCave)
        {
            ConnectClosestCave(allCaves, true);
        }
    }

    void CreateCavePassage (Cave caveA, Cave caveB, Coord tileA, Coord tileB)
    {
        Cave.ConnectCaves(caveA, caveB);
        // is in 3d orientation top down
        //Debug.DrawLine(CoordToWorldPoint(tileA), CoordToWorldPoint(tileB), Color.green, 100);

        List<Coord> line = GetLine(tileA, tileB);
        foreach (Coord coord in line)
        {
            // TODO put passage size as param
            DrawCircle(coord, 2);
        }
    }


    void DrawCircle (Coord coord, int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                int drawX = coord.tileX + x;
                int drawY = coord.tileY + y;

                if (IsInMapRange(drawX, drawY))
                {
                    map[drawX, drawY] = 0;
                }
            } 
        }
    }

    List<Coord> GetLine(Coord from, Coord to)
    {
        List<Coord> line = new List<Coord>();

        int x = from.tileX;
        int y = from.tileY;

        int dx = to.tileX - from.tileX;
        int dy = to.tileY - from.tileY;

        bool inverted = false;
        int step = Math.Sign(dx);
        int gradientStep = Math.Sign(dy);

        int longest = Mathf.Abs(dx);
        int shortest = Mathf.Abs(dy);

        if (longest < shortest)
        {
            inverted = true;
            longest = Mathf.Abs(dy);
            shortest = Mathf.Abs(dx);

            step = Math.Sign(dy);
            gradientStep = Math.Sign(dx);
        }

        int gradientAccumulation = longest / 2;
        for (int i = 0; i < longest; i++)
        {
            line.Add(new Coord(x, y));
            if (inverted)
            {
                y += step;
            } else
            {
                x += step;
            }

            gradientAccumulation += shortest;
            if (gradientAccumulation >= longest)
            {
                if (inverted)
                {
                    x += gradientStep;
                } else
                {
                    y += gradientStep;
                }
                gradientAccumulation -= longest;
            }
        }
        return line;
    }


    // get coords based on map information to draw debug points
    Vector3 CoordToWorldPoint(Coord tile)
    {
        return new Vector3(-width / 2 + .5f + tile.tileX, 2, -height / 2 + .5f + tile.tileY);
    }


    List<List<Coord>> GetRegions(int tileType)
    {
        List<List<Coord>> regions = new List<List<Coord>>();
        int[,] mapFlags = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (mapFlags[x, y] == 0 && map[x, y] == tileType)
                {
                    List<Coord> newRegion = GetRegionTiles(x, y);
                    regions.Add(newRegion);

                    foreach (Coord tile in newRegion)
                    {
                        mapFlags[tile.tileX, tile.tileY] = 1;
                    }
                }
            }
        }

        return regions;
    }


    List<Coord> GetRegionTiles(int startX, int startY)
    {
        List<Coord> tiles = new List<Coord>();
        int[,] mapFlags = new int[width, height];
        int tileType = map[startX, startY];

        Queue<Coord> queue = new Queue<Coord>();
        queue.Enqueue(new Coord(startX, startY));
        mapFlags[startX, startY] = 1;

        while(queue.Count > 0)
        {
            Coord tile = queue.Dequeue();
            tiles.Add(tile);

            for (int x = tile.tileX - 1; x <= tile.tileX + 1; x++)
            {
                for (int y = tile.tileY - 1; y <= tile.tileY + 1; y++)
                {
                    if (IsInMapRange(x, y) && (y == tile.tileY || x == tile.tileX))
                    {
                        if (mapFlags[x, y] == 0 && map[x, y] == tileType)
                        {
                            mapFlags[x, y] = 1;
                            queue.Enqueue(new Coord(x, y));
                        }
                    }
                }
            }
        }

        return tiles;
    }


    bool IsInMapRange(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }


    void RandomFillMap()
    {
        if (useRandomSeed)
        {
            seed = Time.time.ToString();
        }
        System.Random proudoRandom = new System.Random(seed.GetHashCode());


        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || x == width -1 || y == 0 || y == height - 1)
                {
                    map[x, y] = 1;
                } else
                {
                    map[x, y] = (proudoRandom.Next(0, 100) < randomFillPercentage) ? 1 : 0;
                }
            }
        }
    }

    void SmoothMap ()
    {

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int neighbourWallTiles = GetSurroundingWallCount(x, y);
                if (neighbourWallTiles > 4)
                {
                    map[x, y] = 1;
                } else if (neighbourWallTiles < 4)
                {
                    map[x, y] = 0;
                }
            }
        }
    }

    // loop 3x3 grid for checking what neughbours contain
    int GetSurroundingWallCount(int gridX, int gridY)
    {
        int wallCount = 0;
        for (int neighbourX = gridX - 1; neighbourX <= gridX + 1; neighbourX++)
        {
            for (int neighbourY = gridY - 1; neighbourY <= gridY + 1; neighbourY++)
            {
                if (IsInMapRange(neighbourX, neighbourY))
                {
                    if (neighbourX != gridX || neighbourY != gridY)
                    {
                        wallCount += map[neighbourX, neighbourY];
                    }
                } else
                {
                    // incurage wall growth around the edge of the map
                    wallCount++;
                }
            }
        }
        return wallCount;
    }

    struct Coord
    {
        public int tileX;
        public int tileY;

        public Coord(int x, int y)
        {
            tileX = x;
            tileY = y;
        }
    }


    class Cave : IComparable<Cave>
    {
        public List<Coord> tiles;
        public List<Coord> edgeTiles;
        public List<Cave> connectedCaves;
        public int caveSize;

        public bool isAccessibleFromMainCave;
        public bool isMainCave;

        public Cave() { }

        public Cave(List<Coord> caveTiles, int[,] map)
        {
            tiles = caveTiles;
            caveSize = tiles.Count;
            connectedCaves = new List<Cave>();

            edgeTiles = new List<Coord>();

            foreach(Coord tile in tiles)
            {
                for(int x = tile.tileX - 1; x <= tile.tileX + 1; x++)
                {
                    for (int y = tile.tileY - 1; y <= tile.tileY + 1; y++)
                    {
                        if (x == tile.tileX || y == tile.tileY)
                        {
                            if (map[x, y] == 1)
                            {
                                edgeTiles.Add(tile);
                            }
                        }
                    }
                }
            }
        }

        public void SetAccessibleFromMainCave()
        {
            if (!isAccessibleFromMainCave)
            {
                isAccessibleFromMainCave = true;
                foreach (Cave connectedCave in connectedCaves)
                {
                    connectedCave.SetAccessibleFromMainCave();
                }
            }
        }

        public static void ConnectCaves(Cave caveA, Cave caveB)
        {
            if (caveA.isAccessibleFromMainCave)
            {
                caveB.SetAccessibleFromMainCave();
            } else if (caveB.isAccessibleFromMainCave)
            {
                caveA.SetAccessibleFromMainCave();
            }
            caveA.connectedCaves.Add(caveB);
            caveB.connectedCaves.Add(caveA);
        }

        public bool IsConnected(Cave otherCave)
        {
            return connectedCaves.Contains(otherCave);
        }

        public int CompareTo(Cave otherCave)
        {
            return otherCave.caveSize.CompareTo(caveSize);
        }
    }
}
