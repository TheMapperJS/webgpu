using System.Numerics;
using SilkWebGpuPbr;
using Xunit;

namespace SilkWebGpuPbr.Tests;

public class VoxelEngineTests
{
    [Fact]
    public void TestBlockProperties()
    {
        Assert.True(BlockProperties.IsTransparent(BlockType.Air));
        Assert.True(BlockProperties.IsTransparent(BlockType.Water));
        Assert.True(BlockProperties.IsTransparent(BlockType.Leaves));
        Assert.False(BlockProperties.IsTransparent(BlockType.Stone));
        Assert.False(BlockProperties.IsTransparent(BlockType.Grass));

        Assert.True(BlockProperties.IsOpaque(BlockType.Stone));
        Assert.True(BlockProperties.IsOpaque(BlockType.Dirt));
        Assert.False(BlockProperties.IsOpaque(BlockType.Water));
        Assert.False(BlockProperties.IsOpaque(BlockType.Air));

        // Grass multi-face colors: top vs side vs bottom
        Vector4 grassTop = BlockProperties.GetFaceColor(BlockType.Grass, new Vector3(0, 1, 0));
        Vector4 grassSide = BlockProperties.GetFaceColor(BlockType.Grass, new Vector3(1, 0, 0));
        Vector4 grassBottom = BlockProperties.GetFaceColor(BlockType.Grass, new Vector3(0, -1, 0));

        Assert.NotEqual(grassTop, grassSide);
        Assert.NotEqual(grassTop, grassBottom);

        // Wood multi-face colors: top vs side
        Vector4 woodTop = BlockProperties.GetFaceColor(BlockType.Wood, new Vector3(0, 1, 0));
        Vector4 woodSide = BlockProperties.GetFaceColor(BlockType.Wood, new Vector3(1, 0, 0));

        Assert.NotEqual(woodTop, woodSide);
    }

    [Fact]
    public void TestChunkFaceCulling()
    {
        // Solid against Air -> should render
        Assert.True(ChunkMesher.ShouldRenderFace(BlockType.Stone, BlockType.Air));

        // Solid against Solid -> should cull (false)
        Assert.False(ChunkMesher.ShouldRenderFace(BlockType.Stone, BlockType.Dirt));

        // Solid against Water -> should render
        Assert.True(ChunkMesher.ShouldRenderFace(BlockType.Stone, BlockType.Water));

        // Water against Water -> should cull
        Assert.False(ChunkMesher.ShouldRenderFace(BlockType.Water, BlockType.Water));

        // Air against anything -> should cull
        Assert.False(ChunkMesher.ShouldRenderFace(BlockType.Air, BlockType.Stone));
    }

    [Fact]
    public void TestAmbientOcclusionCalculation()
    {
        // No neighbor blocks -> max brightness (1.0)
        float unoccluded = ChunkMesher.CalculateAoFactor(side1Solid: false, side2Solid: false, cornerSolid: false);
        Assert.Equal(1.0f, unoccluded);

        // One side block -> partial AO
        float oneSide = ChunkMesher.CalculateAoFactor(side1Solid: true, side2Solid: false, cornerSolid: false);
        Assert.True(oneSide < 1.0f);

        // Two adjacent side blocks -> maximum occlusion (0.4)
        float twoSides = ChunkMesher.CalculateAoFactor(side1Solid: true, side2Solid: true, cornerSolid: false);
        Assert.Equal(0.4f, twoSides);
    }

    [Fact]
    public void TestWorldGenerationFeatures()
    {
        Chunk singleChunk = new Chunk(new Vector3(0, 0, 0));

        // Verify SetBlock and GetBlock on Chunk
        singleChunk.SetBlock(5, 5, 5, BlockType.Stone);
        Assert.Equal(BlockType.Stone, singleChunk.GetBlock(5, 5, 5));
        Assert.Equal(BlockType.Air, singleChunk.GetBlock(0, 0, 0));

        // Verify ChunkMesher generates mesh
        using ChunkMeshData meshData = ChunkMesher.CreateMesh(singleChunk);
        Assert.True(meshData.VertexCount > 0);
        Assert.True(meshData.IndexCount > 0);
        Assert.Equal(24, meshData.VertexCount); // 6 faces * 4 vertices for 1 isolated cube
        Assert.Equal(36, meshData.IndexCount);  // 6 faces * 6 indices for 1 isolated cube
    }
}
