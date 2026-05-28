// <copyright file="MapPackages.razor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Pages;

using System.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MUnique.OpenMU.CustomMaps;
using MUnique.OpenMU.Web.AdminPanel.Services;

/// <summary>
/// Page for importing <c>.bmap</c> map packages and listing installed custom maps.
/// </summary>
public partial class MapPackages : ComponentBase
{
    private IBrowserFile? _chosenFile;
    private string? _chosenFileName;
    private long _chosenFileSize;
    private bool _isImporting;
    private MapPackageImportResult? _lastResult;
    private List<short> _installedMaps = new();

    /// <summary>Gets or sets the import service.</summary>
    [Inject]
    public MapPackageImportService ImportService { get; set; } = null!;

    /// <summary>Gets or sets the asset store (for listing installed maps).</summary>
    [Inject]
    public MapAssetStore AssetStore { get; set; } = null!;

    /// <inheritdoc/>
    protected override Task OnInitializedAsync()
    {
        this.RefreshInstalledMaps();
        return base.OnInitializedAsync();
    }

    private void OnFileChosen(InputFileChangeEventArgs args)
    {
        this._chosenFile = args.File;
        this._chosenFileName = args.File?.Name;
        this._chosenFileSize = args.File?.Size ?? 0;
        this._lastResult = null;
    }

    private async Task OnImportClickAsync()
    {
        if (this._chosenFile is null)
        {
            return;
        }

        this._isImporting = true;
        this._lastResult = null;
        try
        {
            // Stream the upload into a MemoryStream so the reader can seek freely.
            // Cap matches the manifest+assets total budget conservatively at 64 MB.
            const long uploadCap = 64L * 1024L * 1024L;
            await using var sourceStream = this._chosenFile.OpenReadStream(maxAllowedSize: uploadCap);
            using var buffer = new MemoryStream();
            await sourceStream.CopyToAsync(buffer).ConfigureAwait(true);
            buffer.Position = 0;

            this._lastResult = await this.ImportService.ImportAsync(buffer).ConfigureAwait(true);
            this.RefreshInstalledMaps();
        }
        catch (Exception ex)
        {
            this._lastResult = MapPackageImportResult.Failed($"Unexpected error: {ex.Message}");
        }
        finally
        {
            this._isImporting = false;
        }
    }

    private void RefreshInstalledMaps()
    {
        this._installedMaps = this.AssetStore.ListInstalledMapNumbers().OrderBy(n => n).ToList();
    }
}
