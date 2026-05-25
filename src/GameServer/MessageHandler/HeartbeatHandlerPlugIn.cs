// <copyright file="HeartbeatHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameServer.RemoteView;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// BloodlustMU client→server heartbeat. The client sends a tiny ping every ~10s
/// so it can detect a frozen/paused server that the TCP layer can't see (a
/// docker-paused container still has a healthy kernel stack — keepalive ACKs
/// reply, no Disconnected event fires). The server simply echoes the sequence
/// back so the client can measure latency and decide the link is dead if no
/// pong arrives within its timeout window.
/// </summary>
/// <remarks>
/// Ping packet layout (6 bytes, client → server):
/// [0] 0xC1                packet type
/// [1] 0x06                length
/// [2] 0xE7                opcode (HeartbeatHandlerPlugIn.Key)
/// [3] 0x01                sub-op: ping (reserved for future expansion)
/// [4–5] sequence          big-endian ushort, echoed verbatim in the pong
///
/// Pong packet layout (6 bytes, server → client) — sent via the existing
/// custom 0xAB extension container as sub-op 0x07. See ConnectionExtensions
/// below; the client routes 0xAB through ReceivePrimeStatus which already
/// dispatches by sub-op.
/// </remarks>
[PlugIn]
[Display(Name = "Heartbeat Handler", Description = "Replies to BloodlustMU client heartbeat pings so the client can detect a frozen server.")]
[Guid("F0E5A1B2-3C4D-4E5F-9081-A1B2C3D4E5F6")]
internal class HeartbeatHandlerPlugIn : IPacketHandlerPlugIn
{
    private const byte PingOpcode = 0xE7;
    private const byte ExtensionOpcode = 0xAB;
    private const byte SubOpPong = 0x07;
    private const int PongLength = 6;

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => PingOpcode;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < 6 || player is not RemotePlayer remotePlayer || remotePlayer.Connection is not { } connection)
        {
            return;
        }

        // The client's sequence/timestamp tag lives at bytes 4-5.
        var span = packet.Span;
        var seqHi = span[4];
        var seqLo = span[5];

        await connection.SendAsync(() =>
        {
            var output = connection.Output.GetSpan(PongLength)[..PongLength];
            output[0] = 0xC1;
            output[1] = PongLength;
            output[2] = ExtensionOpcode;
            output[3] = SubOpPong;
            output[4] = seqHi;
            output[5] = seqLo;
            return PongLength;
        }).ConfigureAwait(false);
    }
}
