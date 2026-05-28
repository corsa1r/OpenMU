// <copyright file="IShowCustomMapManifestPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// View plug-in which pushes the server-authoritative map manifest + warp list to the client
/// right after world entry, replacing the client's static <c>MoveReq_*.bmd</c> file.
/// </summary>
/// <remarks>
/// The packet carries two arrays:
/// 1. Map manifest — one entry per <c>GameMapDefinition</c> the client may visit. The
///    <c>IsCustomMap</c> flag tells the client to load the map's terrain/objects from
///    <c>Data\World\Custom\WorldN+1\</c> instead of the classic <c>Data\WorldN+1\</c>.
/// 2. Warp list — one entry per <c>WarpInfo</c>. Lets the client render its Move List
///    UI without ever consulting the on-disk BMD.
/// </remarks>
public interface IShowCustomMapManifestPlugIn : IViewPlugIn
{
    /// <summary>Sends the manifest + warp list to the client.</summary>
    ValueTask ShowAsync();
}
