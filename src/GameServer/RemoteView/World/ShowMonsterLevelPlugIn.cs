// <copyright file="ShowMonsterLevelPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends a custom C1 0xAB sub-op 0x06 packet that tells the client a target's
/// current level. The client uses this for the floating health bar prefix
/// "(level) Name" — replacing the previous reliance on client-side Monster.txt
/// data which can drift from server config.
/// </summary>
/// <remarks>
/// Packet layout — Monster Level (8 bytes):
/// [0]    0xC1          packet type
/// [1]    0x08          total length
/// [2]    0xAB          operation: bloodlust extension
/// [3]    0x06          sub-operation: monster level
/// [4–5]  targetId      big-endian ushort
/// [6–7]  level         big-endian ushort
/// </remarks>
[PlugIn]
[Display(Name = "Show Monster Level", Description = "Pushes a monster's current level to the client for floating-bar display.")]
[Guid("DCB9F3F4-0E2A-4F0E-8FDF-B5C3F4A2A0AA")]
public sealed class ShowMonsterLevelPlugIn : IShowMonsterLevelPlugIn
{
    private const byte ExtensionOperation = 0xAB;
    private const byte SubOpMonsterLevel = 0x06;
    private const int PacketLength = 8;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="ShowMonsterLevelPlugIn"/> class.</summary>
    public ShowMonsterLevelPlugIn(RemotePlayer player) => _player = player;

    /// <inheritdoc/>
    public async ValueTask ShowMonsterLevelAsync(IAttackable target, ushort level)
    {
        if (_player.Connection is not { } connection)
        {
            return;
        }

        var targetId = target.GetId(_player);

        await connection.SendAsync(() =>
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = ExtensionOperation;
            span[3] = SubOpMonsterLevel;
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], targetId);
            BinaryPrimitives.WriteUInt16BigEndian(span[6..], level);
            return PacketLength;
        }).ConfigureAwait(false);
    }
}
