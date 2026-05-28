// <copyright file="MapPackagesController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.API;

using Microsoft.AspNetCore.Mvc;
using MUnique.OpenMU.CustomMaps;
using MUnique.OpenMU.Web.AdminPanel.Services;

/// <summary>Exposes map package download endpoints. Import happens through the Blazor page.</summary>
[Route("api/maps/packages")]
public class MapPackagesController : Controller
{
    private readonly MapPackageExportService _exportService;

    /// <summary>Initializes a new instance of the <see cref="MapPackagesController"/> class.</summary>
    public MapPackagesController(MapPackageExportService exportService)
    {
        this._exportService = exportService;
    }

    /// <summary>Streams a <c>.bmap</c> file for the requested map number.</summary>
    /// <param name="mapNumber">The map number.</param>
    /// <param name="discriminator">Optional discriminator (defaults to 0).</param>
    [HttpGet("{mapNumber:int}")]
    public async Task<IActionResult> Export(int mapNumber, [FromQuery] int discriminator = 0)
    {
        try
        {
            var bytes = await this._exportService.ExportAsync((short)mapNumber, discriminator).ConfigureAwait(false);
            var safeName = $"map-{mapNumber}{(discriminator > 0 ? $"-d{discriminator}" : string.Empty)}{MapPackageFormat.DefaultExtension}";
            return this.File(bytes, "application/zip", safeName);
        }
        catch (InvalidOperationException ex)
        {
            return this.NotFound(ex.Message);
        }
    }
}
