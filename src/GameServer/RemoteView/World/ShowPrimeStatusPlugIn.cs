// <copyright file="ShowPrimeStatusPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends custom prime-mark status packets (C1 0xAB) to the client so it can
/// display / remove the elemental icon above the target's health bar.
/// </summary>
/// <remarks>
/// Packet layout — Prime Applied (12 bytes):
/// [0]    0xC1          packet type
/// [1]    0x0C          total length
/// [2]    0xAB          operation: prime status
/// [3]    0x01          sub-operation: applied
/// [4–5]  targetId      big-endian ushort
/// [6]    element       1 byte (matches <see cref="SkillComboElement"/> ordinal)
/// [7]    0x00          reserved
/// [8–11] durationMs    big-endian int32
///
/// Packet layout — Prime Cleared (7 bytes):
/// [0]    0xC1
/// [1]    0x07
/// [2]    0xAB
/// [3]    0x02          sub-operation: cleared
/// [4–5]  targetId      big-endian ushort
/// [6]    element       1 byte
/// </remarks>
[PlugIn]
[Display(Name = "Show Prime Status", Description = "Notifies the client when an elemental prime mark is applied or cleared on a target.")]
[Guid("C7D84F21-9B33-4A10-BDE2-1F3C5A6B7D8E")]
public sealed class ShowPrimeStatusPlugIn : IShowPrimeStatusPlugIn
{
    private const byte PrimeStatusOperation = 0xAB;
    private const byte SubOpApplied = 0x01;
    private const byte SubOpCleared = 0x02;
    private const byte SubOpDetonated = 0x03;
    private const int AppliedPacketLength = 12;
    private const int ClearedPacketLength = 7;
    private const int DetonatedPacketLength = 8;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="ShowPrimeStatusPlugIn"/> class.</summary>
    public ShowPrimeStatusPlugIn(RemotePlayer player) => _player = player;

    /// <inheritdoc/>
    public async ValueTask ShowPrimeAppliedAsync(IAttackable target, SkillComboElement element, int durationMs)
    {
        if (_player.Connection is not { } connection)
        {
            return;
        }

        var targetId = target.GetId(_player);

        await connection.SendAsync(() =>
        {
            var span = connection.Output.GetSpan(AppliedPacketLength)[..AppliedPacketLength];
            span[0] = 0xC1;
            span[1] = AppliedPacketLength;
            span[2] = PrimeStatusOperation;
            span[3] = SubOpApplied;
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], targetId);
            span[6] = (byte)element;
            span[7] = 0x00;
            BinaryPrimitives.WriteInt32BigEndian(span[8..], durationMs);
            return AppliedPacketLength;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowPrimeClearedAsync(IAttackable target, SkillComboElement element)
    {
        if (_player.Connection is not { } connection)
        {
            return;
        }

        var targetId = target.GetId(_player);

        await connection.SendAsync(() =>
        {
            var span = connection.Output.GetSpan(ClearedPacketLength)[..ClearedPacketLength];
            span[0] = 0xC1;
            span[1] = ClearedPacketLength;
            span[2] = PrimeStatusOperation;
            span[3] = SubOpCleared;
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], targetId);
            span[6] = (byte)element;
            return ClearedPacketLength;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowDetonationAsync(IAttackable target, SkillComboElement element, double detonationRadius)
    {
        if (_player.Connection is not { } connection)
        {
            return;
        }

        var targetId = target.GetId(_player);
        var radiusByte = (byte)Math.Min(255, Math.Round(detonationRadius));

        await connection.SendAsync(() =>
        {
            var span = connection.Output.GetSpan(DetonatedPacketLength)[..DetonatedPacketLength];
            span[0] = 0xC1;
            span[1] = DetonatedPacketLength;
            span[2] = PrimeStatusOperation;
            span[3] = SubOpDetonated;
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], targetId);
            span[6] = (byte)element;
            span[7] = radiusByte;
            return DetonatedPacketLength;
        }).ConfigureAwait(false);
    }
}
