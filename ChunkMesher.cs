using System.Buffers;
using System.Numerics;

namespace SilkWebGpuPbr;

public readonly struct ChunkMeshData : IDisposable
{
    public readonly VoxelVertex[] Vertices;
    public readonly uint[] Indices;
    public readonly int VertexCount;
    public readonly int IndexCount;

    public ChunkMeshData(VoxelVertex[] vertices, uint[] indices, int vertexCount, int indexCount)
    {
        Vertices = vertices;
        Indices = indices;
        VertexCount = vertexCount;
        IndexCount = indexCount;
    }

    public void Dispose()
    {
        if (Vertices != null)
        {
            ArrayPool<VoxelVertex>.Shared.Return(Vertices);
        }

        if (Indices != null)
        {
            ArrayPool<uint>.Shared.Return(Indices);
        }
    }
}

public static class ChunkMesher
{
    private static readonly Vector3[] VoxelVertices =
    [
        new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0), // Front
        new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1)  // Back
    ];

    private static readonly Vector3[] FaceNormals =
    [
        new Vector3(0, 0, -1), // Front
        new Vector3(0, 0, 1),  // Back
        new Vector3(-1, 0, 0), // Left
        new Vector3(1, 0, 0),  // Right
        new Vector3(0, 1, 0),  // Top
        new Vector3(0, -1, 0)  // Bottom
    ];

    private static readonly int[][] FaceVertices =
    [
        [0, 1, 2, 3], // Front (-Z)
        [5, 4, 7, 6], // Back (+Z)
        [4, 0, 3, 7], // Left (-X)
        [1, 5, 6, 2], // Right (+X)
        [3, 2, 6, 7], // Top (+Y)
        [4, 5, 1, 0]  // Bottom (-Y)
    ];

    private static readonly (int x, int y, int z)[] FaceOffsets =
    [
        (0, 0, -1), // Front
        (0, 0, 1),  // Back
        (-1, 0, 0), // Left
        (1, 0, 0),  // Right
        (0, 1, 0),  // Top
        (0, -1, 0)  // Bottom
    ];

    private static readonly (Vector3 u, Vector3 v)[] FaceTangents =
    [
        (new Vector3(1, 0, 0), new Vector3(0, 1, 0)),  // Front (-Z)
        (new Vector3(-1, 0, 0), new Vector3(0, 1, 0)), // Back (+Z)
        (new Vector3(0, 0, 1), new Vector3(0, 1, 0)),  // Left (-X)
        (new Vector3(0, 0, -1), new Vector3(0, 1, 0)), // Right (+X)
        (new Vector3(1, 0, 0), new Vector3(0, 0, 1)),  // Top (+Y)
        (new Vector3(1, 0, 0), new Vector3(0, 0, -1))  // Bottom (-Y)
    ];

    private static readonly (int du, int dv)[][] FaceVertexCornerSigns =
    [
        [ (-1, -1), (+1, -1), (+1, +1), (-1, +1) ], // Front
        [ (-1, -1), (+1, -1), (+1, +1), (-1, +1) ], // Back
        [ (+1, -1), (-1, -1), (-1, +1), (+1, +1) ], // Left
        [ (+1, -1), (-1, -1), (-1, +1), (+1, +1) ], // Right
        [ (-1, -1), (+1, -1), (+1, +1), (-1, +1) ], // Top
        [ (-1, -1), (+1, -1), (+1, +1), (-1, +1) ]  // Bottom
    ];

    public static bool ShouldRenderFace(BlockType current, BlockType neighbor)
    {
        if (current == BlockType.Air) return false;
        if (current == neighbor) return false;

        if (BlockProperties.IsOpaque(current))
        {
            return !BlockProperties.IsOpaque(neighbor);
        }

        return neighbor == BlockType.Air || BlockProperties.IsOpaque(neighbor);
    }

    public static float CalculateAoFactor(bool side1Solid, bool side2Solid, bool cornerSolid)
    {
        if (side1Solid && side2Solid)
        {
            return 0.4f;
        }

        int solidCount = (side1Solid ? 1 : 0) + (side2Solid ? 1 : 0) + (cornerSolid ? 1 : 0);
        return solidCount switch
        {
            0 => 1.0f,
            1 => 0.78f,
            2 => 0.58f,
            _ => 0.4f
        };
    }

    public static ChunkMeshData CreateMesh(Chunk chunk, Func<int, int, int, BlockType>? worldBlockAt = null)
    {
        int capacityFaces = 4096;
        VoxelVertex[] vertices = ArrayPool<VoxelVertex>.Shared.Rent(capacityFaces * 4);
        uint[] indices = ArrayPool<uint>.Shared.Rent(capacityFaces * 6);

        int vertexCount = 0;
        int indexCount = 0;

        BlockType GetBlockAt(int bx, int by, int bz)
        {
            if (worldBlockAt != null)
            {
                int gX = (int)chunk.Position.X * Chunk.Width + bx;
                int gY = (int)chunk.Position.Y * Chunk.Height + by;
                int gZ = (int)chunk.Position.Z * Chunk.Depth + bz;
                return worldBlockAt(gX, gY, gZ);
            }
            return chunk.GetBlock(bx, by, bz);
        }

        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int y = 0; y < Chunk.Height; y++)
            {
                for (int z = 0; z < Chunk.Depth; z++)
                {
                    BlockType block = chunk.GetBlock(x, y, z);
                    if (block == BlockType.Air)
                    {
                        continue;
                    }

                    Vector3 positionOffset = new Vector3(x, y, z);

                    for (int f = 0; f < 6; f++)
                    {
                        var (ox, oy, oz) = FaceOffsets[f];
                        BlockType neighbor = GetBlockAt(x + ox, y + oy, z + oz);

                        if (ShouldRenderFace(block, neighbor))
                        {
                            if (vertexCount + 4 > vertices.Length)
                            {
                                var newVertices = ArrayPool<VoxelVertex>.Shared.Rent(vertices.Length * 2);
                                Array.Copy(vertices, newVertices, vertexCount);
                                ArrayPool<VoxelVertex>.Shared.Return(vertices);
                                vertices = newVertices;
                            }
                            if (indexCount + 6 > indices.Length)
                            {
                                var newIndices = ArrayPool<uint>.Shared.Rent(indices.Length * 2);
                                Array.Copy(indices, newIndices, indexCount);
                                ArrayPool<uint>.Shared.Return(indices);
                                indices = newIndices;
                            }

                            Vector3 normal = FaceNormals[f];
                            Vector4 baseColor = BlockProperties.GetFaceColor(block, normal);

                            var (u, v) = FaceTangents[f];
                            float[] aoFactors = new float[4];

                            for (int vertIdx = 0; vertIdx < 4; vertIdx++)
                            {
                                int cornerIndex = FaceVertices[f][vertIdx];
                                Vector3 localPos = VoxelVertices[cornerIndex];

                                var (du, dv) = FaceVertexCornerSigns[f][vertIdx];

                                int s1X = x + ox + (int)(u.X * du);
                                int s1Y = y + oy + (int)(u.Y * du);
                                int s1Z = z + oz + (int)(u.Z * du);

                                int s2X = x + ox + (int)(v.X * dv);
                                int s2Y = y + oy + (int)(v.Y * dv);
                                int s2Z = z + oz + (int)(v.Z * dv);

                                int cX = x + ox + (int)(u.X * du + v.X * dv);
                                int cY = y + oy + (int)(u.Y * du + v.Y * dv);
                                int cZ = z + oz + (int)(u.Z * du + v.Z * dv);

                                bool s1Solid = BlockProperties.IsOpaque(GetBlockAt(s1X, s1Y, s1Z));
                                bool s2Solid = BlockProperties.IsOpaque(GetBlockAt(s2X, s2Y, s2Z));
                                bool cSolid = BlockProperties.IsOpaque(GetBlockAt(cX, cY, cZ));

                                float ao = CalculateAoFactor(s1Solid, s2Solid, cSolid);
                                aoFactors[vertIdx] = ao;

                                Vector4 finalColor = new Vector4(baseColor.X * ao, baseColor.Y * ao, baseColor.Z * ao, baseColor.W);
                                vertices[vertexCount + vertIdx] = new VoxelVertex(positionOffset + localPos, normal, finalColor);
                            }

                            uint baseVert = (uint)vertexCount;

                            if (aoFactors[0] + aoFactors[2] < aoFactors[1] + aoFactors[3])
                            {
                                indices[indexCount + 0] = baseVert + 1;
                                indices[indexCount + 1] = baseVert + 2;
                                indices[indexCount + 2] = baseVert + 3;
                                indices[indexCount + 3] = baseVert + 1;
                                indices[indexCount + 4] = baseVert + 3;
                                indices[indexCount + 5] = baseVert + 0;
                            }
                            else
                            {
                                indices[indexCount + 0] = baseVert + 0;
                                indices[indexCount + 1] = baseVert + 1;
                                indices[indexCount + 2] = baseVert + 2;
                                indices[indexCount + 3] = baseVert + 2;
                                indices[indexCount + 4] = baseVert + 3;
                                indices[indexCount + 5] = baseVert + 0;
                            }

                            vertexCount += 4;
                            indexCount += 6;
                        }
                    }
                }
            }
        }

        return new ChunkMeshData(vertices, indices, vertexCount, indexCount);
    }
}
