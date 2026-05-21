// <copyright file="ShowDpsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends a DPS update packet to the observing client.
/// </summary>
[PlugIn]
[Guid("D4E5F6A7-B8C9-0123-DEFA-456789ABCDEF")]
public class ShowDpsPlugIn : IShowDpsPlugIn
{
    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowDpsPlugIn"/> class.
    /// </summary>
    /// <param name="player">The observing player.</param>
    public ShowDpsPlugIn(RemotePlayer player)
    {
        this._player = player;
    }

    /// <inheritdoc/>
    public async ValueTask ShowDpsAsync(IIdentifiable source, uint dps)
    {
        if (this._player.Connection is not { } connection)
        {
            return;
        }

        var objectId = source.GetId(this._player);
        await connection.SendDpsUpdateAsync(objectId, dps).ConfigureAwait(false);
    }
}
