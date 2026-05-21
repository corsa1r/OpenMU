// <copyright file="DpsConnectionExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Network.Packets.ServerToClient;

using System.Buffers.Binary;
using MUnique.OpenMU.Network;

/// <summary>
/// Extension method for sending a DPS update packet to the client.
/// Packet layout: C1 09 0A [ObjectId:2 LE] [Dps:4 LE]
/// </summary>
public static class DpsConnectionExtensions
{
    private const byte PacketCode = 0x0A;
    private const int PacketLength = 9;

    /// <summary>
    /// Sends the current DPS value for an object to this connection.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="objectId">The ID of the player whose DPS is being reported.</param>
    /// <param name="dps">The current DPS value.</param>
    public static async ValueTask SendDpsUpdateAsync(this IConnection? connection, ushort objectId, uint dps)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = PacketCode;
            BinaryPrimitives.WriteUInt16LittleEndian(span[3..], objectId);
            BinaryPrimitives.WriteUInt32LittleEndian(span[5..], dps);
            return PacketLength;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }
}
