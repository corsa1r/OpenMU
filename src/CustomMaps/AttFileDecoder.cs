// <copyright file="AttFileDecoder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.CustomMaps;

/// <summary>
/// Decodes an editor-saved <c>EncTerrain&lt;N&gt;.att</c> binary into the 8-bit attribute
/// format that <c>MUnique.OpenMU.GameLogic.GameMapTerrain</c> expects in
/// <c>GameMapDefinition.TerrainData</c>.
/// </summary>
/// <remarks>
/// On-disk wire format (MuMain editor's CustomMapIO):
/// <code>
/// encrypted = MapFileEncrypt(BuxConvert(cleartext))
/// cleartext = [4-byte header][256*256 * WORD attribute grid]   // 131076 bytes total
/// </code>
/// To decode we apply the inverse: MapFileDecrypt → BuxConvert → parse WORDs → fold the
/// 16-bit TW_* bit flags down to OpenMU's 3-value scheme:
/// <c>0 = walkable, 1 = safezone, 2 = blocked</c>.
/// </remarks>
public static class AttFileDecoder
{
    /// <summary>Header bytes of the cleartext on-disk format.</summary>
    public const int CleartextHeaderSize = 4;

    /// <summary>Header bytes of the OpenMU TerrainData format (skipped via <c>AsSpan(3)</c>).</summary>
    public const int OpenMuHeaderSize = 3;

    /// <summary>Per-tile width/height of the attribute grid.</summary>
    public const int TerrainSize = 256;

    /// <summary>Tile count (256 * 256).</summary>
    public const int TileCount = TerrainSize * TerrainSize;

    /// <summary>Expected size of an editor-saved encrypted .att file.</summary>
    public const int ExpectedEncryptedSize = CleartextHeaderSize + (TileCount * 2);

    /// <summary>Output size of the OpenMU TerrainData buffer this decoder produces.</summary>
    public const int OpenMuTerrainDataSize = OpenMuHeaderSize + TileCount;

    /// <summary>MuMain's TW_SAFEZONE bit.</summary>
    private const ushort TwSafezone = 0x0001;

    /// <summary>MuMain's TW_NOMOVE bit.</summary>
    private const ushort TwNoMove = 0x0004;

    /// <summary>MuMain's TW_NOGROUND bit.</summary>
    private const ushort TwNoGround = 0x0008;

    private static readonly byte[] BuxCode = { 0xFC, 0xCF, 0xAB };

    private static readonly byte[] MapXorKey =
    {
        0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27,
        0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2,
    };

    /// <summary>
    /// Decodes an encrypted .att file's raw bytes into an OpenMU TerrainData buffer.
    /// </summary>
    /// <param name="encrypted">The full content of the on-disk .att file.</param>
    /// <returns>
    /// A buffer of <see cref="OpenMuTerrainDataSize"/> bytes that can be assigned directly
    /// to <c>GameMapDefinition.TerrainData</c>.
    /// </returns>
    public static byte[] DecodeToOpenMuTerrainData(ReadOnlySpan<byte> encrypted)
    {
        if (encrypted.Length != ExpectedEncryptedSize)
        {
            throw new ArgumentException(
                $".att size {encrypted.Length} != expected {ExpectedEncryptedSize}.",
                nameof(encrypted));
        }

        // Stage 1: MapFileDecrypt — undo the rolling XOR + key-feedback obfuscation.
        var stage1 = new byte[encrypted.Length];
        MapFileDecrypt(encrypted, stage1);

        // Stage 2: BuxConvert — self-inverse XOR with a 3-byte key.
        BuxConvert(stage1);

        // stage1 is now cleartext: [4-byte header][256*256 WORD grid] (little-endian).
        var output = new byte[OpenMuTerrainDataSize];
        // OpenMU only reads from index 3 onward; leave the 3-byte header at zero — the
        // contents don't matter, only the offset for AsSpan(3) does.

        int srcOffset = CleartextHeaderSize;
        for (int tile = 0; tile < TileCount; tile++)
        {
            // Cleartext stores WORDs little-endian: low byte first, then high byte.
            ushort attr = (ushort)(stage1[srcOffset] | (stage1[srcOffset + 1] << 8));
            srcOffset += 2;

            // Fold to OpenMU's tri-state. TW_NOMOVE / TW_NOGROUND outrank TW_SAFEZONE
            // because a blocked safezone is still blocked.
            byte value;
            if ((attr & (TwNoMove | TwNoGround)) != 0)
            {
                value = 2; // blocked
            }
            else if ((attr & TwSafezone) != 0)
            {
                value = 1; // walkable + safezone
            }
            else
            {
                value = 0; // walkable
            }

            output[OpenMuHeaderSize + tile] = value;
        }

        return output;
    }

    private static void MapFileDecrypt(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        byte mapKey = 0x5E;
        for (int i = 0; i < src.Length; i++)
        {
            byte s = src[i];
            dst[i] = (byte)((s ^ MapXorKey[i % 16]) - mapKey);
            mapKey = (byte)(s + 0x3D);
        }
    }

    private static void BuxConvert(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] ^= BuxCode[i % 3];
        }
    }
}
