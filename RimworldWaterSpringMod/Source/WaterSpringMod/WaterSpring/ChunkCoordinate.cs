using System;
using Verse;

namespace WaterSpringMod.WaterSpring
{
    /// <summary>
    /// Represents a chunk coordinate in the world for organizing water tiles into spatial groups
    /// </summary>
    public struct ChunkCoordinate : IEquatable<ChunkCoordinate>
    {
        // Size of a chunk in cells (default 8x8)
        public const int DefaultChunkSize = 8;
        
        // Chunk coordinates
        public readonly int X;
        public readonly int Z;
        
        // The size of this specific chunk
        public readonly int ChunkSize;
        
        /// <summary>
        /// Create a new chunk coordinate
        /// </summary>
        public ChunkCoordinate(int x, int z, int chunkSize = DefaultChunkSize)
        {
            X = x;
            Z = z;
            ChunkSize = chunkSize;
        }
        
        /// <summary>
        /// Create a chunk coordinate from a world position
        /// </summary>
        public static ChunkCoordinate FromPosition(IntVec3 position, int chunkSize = DefaultChunkSize)
        {
            return new ChunkCoordinate(
                position.x / chunkSize,
                position.z / chunkSize,
                chunkSize
            );
        }
        
        /// <summary>
        /// Get the minimum IntVec3 position contained in this chunk
        /// </summary>
        public IntVec3 MinCorner
        {
            get => new IntVec3(X * ChunkSize, 0, Z * ChunkSize);
        }
        
        /// <summary>
        /// Get the maximum IntVec3 position contained in this chunk (inclusive)
        /// </summary>
        public IntVec3 MaxCorner
        {
            get => new IntVec3((X + 1) * ChunkSize - 1, 0, (Z + 1) * ChunkSize - 1);
        }
        
        /// <summary>
        /// Get all adjacent chunk coordinates (including diagonals)
        /// </summary>
        public ChunkCoordinate[] GetAdjacentChunks()
        {
            ChunkCoordinate[] result = new ChunkCoordinate[8];
            int index = 0;
            
            // Check all 8 adjacent chunks
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue; // Skip self
                    
                    result[index++] = new ChunkCoordinate(X + dx, Z + dz, ChunkSize);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Check if a position is contained within this chunk
        /// </summary>
        public bool Contains(IntVec3 position)
        {
            return position.x >= X * ChunkSize && 
                   position.x < (X + 1) * ChunkSize &&
                   position.z >= Z * ChunkSize && 
                   position.z < (Z + 1) * ChunkSize;
        }
        
        // Override equality operators and methods
        public bool Equals(ChunkCoordinate other)
        {
            return X == other.X && Z == other.Z && ChunkSize == other.ChunkSize;
        }
        
        public override bool Equals(object obj)
        {
            return obj is ChunkCoordinate other && Equals(other);
        }
        
        public override int GetHashCode()
        {
            return (X * 397) ^ Z;
        }
        
        public static bool operator ==(ChunkCoordinate left, ChunkCoordinate right)
        {
            return left.Equals(right);
        }
        
        public static bool operator !=(ChunkCoordinate left, ChunkCoordinate right)
        {
            return !left.Equals(right);
        }
        
        public override string ToString()
        {
            return $"Chunk({X}, {Z})";
        }
    }
}
