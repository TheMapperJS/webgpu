using System.Numerics;

namespace SilkWebGpuPbr;

public enum BlockType : ushort
{
    Air = 0,
    Dirt = 1,
    Grass = 2,
    Stone = 3,
    Wood = 4,
    Leaves = 5,
    Water = 6,
    Sand = 7,
    Snow = 8,
    Bedrock = 9,
    GoldOre = 10
}

public static class BlockProperties
{
    public static bool IsTransparent(BlockType type)
    {
        return type is BlockType.Air or BlockType.Water or BlockType.Leaves;
    }

    public static bool IsSolid(BlockType type)
    {
        return type != BlockType.Air;
    }

    public static bool IsOpaque(BlockType type)
    {
        return type != BlockType.Air && type != BlockType.Water;
    }

    public static Vector4 GetFaceColor(BlockType type, Vector3 normal)
    {
        bool isTop = normal.Y > 0.5f;
        bool isBottom = normal.Y < -0.5f;

        return type switch
        {
            BlockType.Dirt => new Vector4(0.5f, 0.33f, 0.18f, 1.0f),
            BlockType.Grass => isTop ? new Vector4(0.22f, 0.72f, 0.22f, 1.0f)
                             : isBottom ? new Vector4(0.5f, 0.33f, 0.18f, 1.0f)
                             : new Vector4(0.38f, 0.5f, 0.2f, 1.0f),
            BlockType.Stone => new Vector4(0.52f, 0.52f, 0.54f, 1.0f),
            BlockType.Wood => (isTop || isBottom)
                             ? new Vector4(0.6f, 0.45f, 0.3f, 1.0f)
                             : new Vector4(0.38f, 0.25f, 0.15f, 1.0f),
            BlockType.Leaves => new Vector4(0.18f, 0.55f, 0.18f, 0.9f),
            BlockType.Water => new Vector4(0.2f, 0.45f, 0.85f, 0.65f),
            BlockType.Sand => new Vector4(0.88f, 0.82f, 0.54f, 1.0f),
            BlockType.Snow => isTop ? new Vector4(0.95f, 0.96f, 0.98f, 1.0f)
                            : new Vector4(0.88f, 0.9f, 0.92f, 1.0f),
            BlockType.Bedrock => new Vector4(0.18f, 0.18f, 0.2f, 1.0f),
            BlockType.GoldOre => new Vector4(0.85f, 0.7f, 0.2f, 1.0f),
            _ => new Vector4(1.0f, 0.0f, 1.0f, 1.0f)
        };
    }
}

public struct VoxelVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;

    public VoxelVertex(Vector3 position, Vector3 normal, Vector4 color)
    {
        Position = position;
        Normal = normal;
        Color = color;
    }
}
