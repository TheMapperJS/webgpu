using System.Collections.Concurrent;
using System.Numerics;
using Silk.NET.WebGPU;

namespace SilkWebGpuPbr;

public unsafe class WorldManager : IDisposable
{
    private readonly WebGPU _webGpu;
    private readonly Device* _device;
    private readonly Queue* _queue;

    private readonly Dictionary<Vector3, Chunk> _chunks = new();
    private readonly Dictionary<Vector3, DynamicGpuMesh> _chunkMeshes = new();

    private readonly ConcurrentQueue<(Vector3 Position, ChunkMeshData MeshData)> _meshingQueue = new();

    public WorldManager(WebGPU webGpu, Device* device, Queue* queue)
    {
        _webGpu = webGpu;
        _device = device;
        _queue = queue;
    }

    public BlockType GetBlockAtWorld(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0) return BlockType.Air;

        int chunkX = FloorDiv(worldX, Chunk.Width);
        int chunkY = FloorDiv(worldY, Chunk.Height);
        int chunkZ = FloorDiv(worldZ, Chunk.Depth);

        Vector3 chunkPos = new Vector3(chunkX, chunkY, chunkZ);
        if (_chunks.TryGetValue(chunkPos, out Chunk? chunk))
        {
            int localX = worldX - chunkX * Chunk.Width;
            int localY = worldY - chunkY * Chunk.Height;
            int localZ = worldZ - chunkZ * Chunk.Depth;
            return chunk.GetBlock(localX, localY, localZ);
        }

        return BlockType.Air;
    }

    public void SetBlockAtWorld(int worldX, int worldY, int worldZ, BlockType type)
    {
        int chunkX = FloorDiv(worldX, Chunk.Width);
        int chunkY = FloorDiv(worldY, Chunk.Height);
        int chunkZ = FloorDiv(worldZ, Chunk.Depth);

        Vector3 chunkPos = new Vector3(chunkX, chunkY, chunkZ);
        if (_chunks.TryGetValue(chunkPos, out Chunk? chunk))
        {
            int localX = worldX - chunkX * Chunk.Width;
            int localY = worldY - chunkY * Chunk.Height;
            int localZ = worldZ - chunkZ * Chunk.Depth;
            chunk.SetBlock(localX, localY, localZ, type);
        }
    }

    private static int FloorDiv(int a, int b)
    {
        return a >= 0 ? a / b : (a - b + 1) / b;
    }

    public void GenerateWorld(int sizeX = 6, int sizeY = 2, int sizeZ = 6)
    {
        _chunks.Clear();

        // 1. Allocate chunks
        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    Vector3 pos = new Vector3(x, y, z);
                    _chunks[pos] = new Chunk(pos);
                }
            }
        }

        int totalWidth = sizeX * Chunk.Width;
        int totalHeight = sizeY * Chunk.Height;
        int totalDepth = sizeZ * Chunk.Depth;

        const int waterLevel = 14;

        // 2. Generate Base Terrain
        for (int wx = 0; wx < totalWidth; wx++)
        {
            for (int wz = 0; wz < totalDepth; wz++)
            {
                float h1 = MathF.Sin(wx * 0.04f) * MathF.Cos(wz * 0.04f) * 12.0f;
                float h2 = MathF.Sin(wx * 0.12f + 1.0f) * MathF.Cos(wz * 0.12f + 0.5f) * 4.0f;
                int height = (int)(22.0f + h1 + h2);
                height = Math.Clamp(height, 2, totalHeight - 10);

                for (int wy = 0; wy < totalHeight; wy++)
                {
                    if (wy == 0)
                    {
                        SetBlockAtWorld(wx, wy, wz, BlockType.Bedrock);
                    }
                    else if (wy < height)
                    {
                        if (wy < 5 && ((wx * 11 + wy * 17 + wz * 23) % 19 == 0))
                        {
                            SetBlockAtWorld(wx, wy, wz, BlockType.GoldOre);
                        }
                        else if (wy < height - 3)
                        {
                            SetBlockAtWorld(wx, wy, wz, BlockType.Stone);
                        }
                        else if (wy < height - 1)
                        {
                            BlockType sub = height <= waterLevel + 1 ? BlockType.Sand : BlockType.Dirt;
                            SetBlockAtWorld(wx, wy, wz, sub);
                        }
                        else
                        {
                            BlockType surface = height >= 32 ? BlockType.Snow
                                            : height <= waterLevel + 1 ? BlockType.Sand
                                            : BlockType.Grass;
                            SetBlockAtWorld(wx, wy, wz, surface);
                        }
                    }
                    else if (wy <= waterLevel)
                    {
                        SetBlockAtWorld(wx, wy, wz, BlockType.Water);
                    }
                }
            }
        }

        // 3. Generate Trees
        for (int wx = 4; wx < totalWidth - 4; wx++)
        {
            for (int wz = 4; wz < totalDepth - 4; wz++)
            {
                if (wx % 11 == 3 && wz % 11 == 5)
                {
                    int height = 0;
                    for (int wy = totalHeight - 1; wy >= 0; wy--)
                    {
                        if (GetBlockAtWorld(wx, wy, wz) != BlockType.Air && GetBlockAtWorld(wx, wy, wz) != BlockType.Water)
                        {
                            height = wy + 1;
                            break;
                        }
                    }

                    if (GetBlockAtWorld(wx, height - 1, wz) == BlockType.Grass)
                    {
                        GrowTree(wx, height, wz);
                    }
                }
            }
        }

        // 4. Queue Meshing tasks with cross-chunk lookup
        foreach (var pair in _chunks)
        {
            Vector3 pos = pair.Key;
            Chunk chunk = pair.Value;
            Task.Run(() => MeshChunkAsync(pos, chunk));
        }
    }

    public void GrowTree(int trunkX, int startY, int trunkZ)
    {
        int trunkHeight = 5;

        for (int y = 0; y < trunkHeight; y++)
        {
            SetBlockAtWorld(trunkX, startY + y, trunkZ, BlockType.Wood);
        }

        int leafBaseY = startY + trunkHeight - 2;
        int leafTopY = startY + trunkHeight + 1;

        for (int ly = leafBaseY; ly <= leafTopY; ly++)
        {
            int radius = ly >= leafTopY - 1 ? 1 : 2;

            for (int lx = -radius; lx <= radius; lx++)
            {
                for (int lz = -radius; lz <= radius; lz++)
                {
                    if (Math.Abs(lx) == radius && Math.Abs(lz) == radius && ly == leafTopY)
                    {
                        continue;
                    }

                    int wx = trunkX + lx;
                    int wy = ly;
                    int wz = trunkZ + lz;

                    if (GetBlockAtWorld(wx, wy, wz) == BlockType.Air)
                    {
                        SetBlockAtWorld(wx, wy, wz, BlockType.Leaves);
                    }
                }
            }
        }
    }

    private void MeshChunkAsync(Vector3 position, Chunk chunk)
    {
        ChunkMeshData meshData = ChunkMesher.CreateMesh(chunk, GetBlockAtWorld);
        _meshingQueue.Enqueue((position, meshData));
    }

    public void Update()
    {
        while (_meshingQueue.TryDequeue(out var result))
        {
            if (!_chunkMeshes.TryGetValue(result.Position, out DynamicGpuMesh? mesh))
            {
                mesh = new DynamicGpuMesh(_webGpu, _device, _queue, $"Chunk {result.Position}");
                _chunkMeshes[result.Position] = mesh;
            }

            mesh.Update(result.MeshData);
            result.MeshData.Dispose();
        }
    }

    public void Draw(RenderPassEncoder* pass, ChunkRenderer renderer)
    {
        foreach (var mesh in _chunkMeshes.Values)
        {
            renderer.Draw(pass, mesh);
        }
    }

    public void Dispose()
    {
        foreach (var mesh in _chunkMeshes.Values)
        {
            mesh.Dispose();
        }
        _chunkMeshes.Clear();
    }
}
