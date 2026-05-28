// <copyright file="ShowCustomMapManifestPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Pushes the server-authoritative map manifest + warp list to the client right after world entry.
/// Lets the client render its Move List UI and pick the right render path (custom slot vs. classic world folder)
/// without consulting any local BMD.
/// </summary>
/// <remarks>
/// Wire format (C2 packet, opcode 0xCC, sub-op 0x01):
/// <code>
/// [0]      0xC2                       packet type (variable length, 2-byte length field)
/// [1-2]    total length               big-endian ushort
/// [3]      0xCC                       operation: custom map manifest
/// [4]      0x01                       sub-operation: full manifest + warp list
/// [5-6]    mapCount                   little-endian ushort
///   per map:
///     [0-1]  Number                   little-endian short
///     [2]    Discriminator            byte
///     [3]    IsCustomMap              0/1
///     [4]    NameLen                  byte (UTF-8 byte length)
///     [5..]  Name                     UTF-8 bytes (no NUL terminator)
/// [N-N+1]  warpCount                  little-endian ushort
///   per warp:
///     [0-1]  Index                    little-endian short
///     [2-3]  TargetMapNumber          little-endian short (Gate's map number)
///     [4-5]  LevelRequirement         little-endian short
///     [6-9]  Costs                    little-endian int
///     [10]   GateX1                   byte (suggested spawn coord)
///     [11]   GateY1                   byte
///     [12]   NameLen                  byte
///     [13..] Name                     UTF-8 bytes
/// </code>
/// </remarks>
[PlugIn]
[Display(Name = "Push Custom Map Manifest", Description = "Sends the server-authoritative map manifest and warp list to the client on world entry.")]
[Guid("F2A8E561-3C7B-49D8-A5E1-7F6D2B4C8A93")]
public sealed class ShowCustomMapManifestPlugIn : IShowCustomMapManifestPlugIn
{
    // Opcode 0xCC chosen because 0xBA is already in use by the legacy "Receive Skill Count" packet on the client.
    private const byte ManifestOperation = 0xCC;
    private const byte SubOpFullManifest = 0x01;
    private const int HeaderSize = 5;   // 0xC2 + 2-byte len + opcode + sub-op
    private const int MaxPacketSize = 32 * 1024;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="ShowCustomMapManifestPlugIn"/> class.</summary>
    public ShowCustomMapManifestPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowAsync()
    {
        this._player.Logger.LogInformation("[MapManifest] ShowAsync entered for player {Name}", this._player.SelectedCharacter?.Name ?? "<no-char>");

        if (this._player.Connection is not { } connection)
        {
            this._player.Logger.LogWarning("[MapManifest] No connection — aborting");
            return;
        }

        var config = this._player.GameContext.Configuration;
        if (config is null)
        {
            this._player.Logger.LogWarning("[MapManifest] No configuration — aborting");
            return;
        }

        var maps = config.Maps.ToList();
        var warps = config.WarpList.Where(w => w.Gate?.Map is not null).ToList();
        this._player.Logger.LogInformation("[MapManifest] Sending {Maps} maps, {Warps} warps", maps.Count, warps.Count);

        // Verify the IsCustomMap flag is actually true on disk for any custom maps the import created.
        foreach (var m in maps.Where(x => x.IsCustomMap))
        {
            this._player.Logger.LogInformation("[MapManifest] Custom map in payload: Number={Number} Name={Name} IsCustom={IsCustom}", m.Number, m.Name, m.IsCustomMap);
        }

        // Pre-compute name byte counts so we know the final packet length.
        var mapNames = new byte[maps.Count][];
        for (int i = 0; i < maps.Count; i++)
        {
            var name = maps[i].Name.ToString() ?? string.Empty;
            mapNames[i] = TruncateUtf8(name, maxBytes: 64);
        }

        var warpNames = new byte[warps.Count][];
        for (int i = 0; i < warps.Count; i++)
        {
            var name = warps[i].Name.ToString() ?? string.Empty;
            warpNames[i] = TruncateUtf8(name, maxBytes: 64);
        }

        int mapBytes = 0;
        for (int i = 0; i < mapNames.Length; i++)
        {
            mapBytes += 5 + mapNames[i].Length;
        }

        int warpBytes = 0;
        for (int i = 0; i < warpNames.Length; i++)
        {
            warpBytes += 13 + warpNames[i].Length;
        }

        var totalLength = HeaderSize
            + 2                 // map count
            + mapBytes
            + 2                 // warp count
            + warpBytes;

        if (totalLength > MaxPacketSize)
        {
            // Soft cap for v1 — if you somehow have thousands of maps/warps, we'd need chunked delivery.
            return;
        }

        await connection.SendAsync(() =>
        {
            var span = connection.Output.GetSpan(totalLength)[..totalLength];
            span[0] = 0xC2;
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1, 2), (ushort)totalLength);
            span[3] = ManifestOperation;
            span[4] = SubOpFullManifest;

            int offset = HeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)maps.Count);
            offset += 2;

            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i];
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), m.Number);
                offset += 2;
                span[offset++] = (byte)Math.Clamp(m.Discriminator, 0, 255);
                span[offset++] = (byte)(m.IsCustomMap ? 1 : 0);
                span[offset++] = (byte)mapNames[i].Length;
                mapNames[i].CopyTo(span[offset..]);
                offset += mapNames[i].Length;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)warps.Count);
            offset += 2;

            for (int i = 0; i < warps.Count; i++)
            {
                var w = warps[i];
                var gate = w.Gate!;
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), (short)w.Index);
                offset += 2;
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), gate.Map!.Number);
                offset += 2;
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), (short)w.LevelRequirement);
                offset += 2;
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), w.Costs);
                offset += 4;
                span[offset++] = gate.X1;
                span[offset++] = gate.Y1;
                span[offset++] = (byte)warpNames[i].Length;
                warpNames[i].CopyTo(span[offset..]);
                offset += warpNames[i].Length;
            }

            return totalLength;
        }).ConfigureAwait(false);
    }

    private static byte[] TruncateUtf8(string s, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes)
        {
            return bytes;
        }

        // Trim from the end without splitting a multi-byte sequence.
        int cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
        {
            cut--;
        }

        var result = new byte[cut];
        Buffer.BlockCopy(bytes, 0, result, 0, cut);
        return result;
    }
}
