// <copyright file="EditMap.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Pages;

using System.Reflection;
using System.Threading;
using Blazored.Modal.Services;
using Blazored.Toast.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Web.AdminPanel.Properties;
using MUnique.OpenMU.Web.Shared;
using MUnique.OpenMU.Web.Shared.Components;
using MUnique.OpenMU.Web.Shared.Components.MapEditor;

/// <summary>
/// A page, which shows an <see cref="MapEditor"/> for all <see cref="GameConfiguration.Maps"/>.
/// </summary>
[Route("/map-editor")]
[Route("/map-editor/{SelectedMapId:guid}")]
public sealed class EditMap : ComponentBase, IDisposable
{
    private List<GameMapDefinition>? _maps;
    private CancellationTokenSource? _disposeCts;
    private IContext? _context;
    private IDisposable? _navigationLockDisposable;

    /// <summary>
    /// Gets or sets the selected map identifier.
    /// </summary>
    [Parameter]
    public Guid SelectedMapId { get; set; }

    /// <summary>
    /// Gets or sets the modal service.
    /// </summary>
    [Inject]
    private IModalService ModalService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the toast service.
    /// </summary>
    [Inject]
    private IToastService ToastService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the game configuration source.
    /// </summary>
    [Inject]
    private IDataSource<GameConfiguration> GameConfigurationSource { get; set; } = null!;

    /// <summary>
    /// Gets or sets the running game servers. Used to hot-reload the
    /// edited map's <see cref="GameMap"/> instance after a save so spawn
    /// changes take effect immediately without a server restart.
    /// </summary>
    [Inject]
    private IDictionary<int, IGameServer> GameServers { get; set; } = null!;

    /// <summary>
    /// Gets or sets the logger.
    /// </summary>
    [Inject]
    private ILogger<EditMap> Logger { get; set; } = null!;

    /// <summary>
    /// Gets or sets the navigation manager.
    /// </summary>
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    /// <summary>
    /// Gets or sets the JavaScript runtime.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <inheritdoc />
    public void Dispose()
    {
        this._disposeCts?.Cancel();
        this._disposeCts?.Dispose();
        this._disposeCts = null;

        this._navigationLockDisposable?.Dispose();
        this._navigationLockDisposable = null;
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (this._maps is null)
        {
            return;
        }

        builder.OpenComponent<Breadcrumb>(0);
        builder.AddAttribute(1, nameof(Breadcrumb.Caption), Resources.MapEditor);
        builder.CloseComponent();

        builder.OpenComponent<CascadingValue<IContext>>(10);
        builder.AddAttribute(11, nameof(CascadingValue<IContext>.Value), this._context);
        builder.AddAttribute(12, nameof(CascadingValue<IContext>.IsFixed), false);
        builder.AddAttribute(13, nameof(CascadingValue<IContext>.ChildContent), (RenderFragment)this.BuildMapEditorFragment);
        builder.CloseComponent();
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        await (this._disposeCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        this._disposeCts?.Dispose();
        this._disposeCts = new CancellationTokenSource();

        this._context = await this.GameConfigurationSource
            .GetContextAsync(this._disposeCts.Token)
            .ConfigureAwait(false);

        await base.OnParametersSetAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender).ConfigureAwait(false);
        if (this._maps is null)
        {
            this._disposeCts ??= new CancellationTokenSource();
            var cts = this._disposeCts.Token;
            _ = Task.Run(() => this.LoadDataAsync(cts), cts);
        }
    }

    /// <inheritdoc />
    protected override Task OnInitializedAsync()
    {
        this._navigationLockDisposable = this.NavigationManager.RegisterLocationChangingHandler(this.OnBeforeInternalNavigationAsync);
        return base.OnInitializedAsync();
    }

    private async ValueTask OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        if (!await this.AllowChangeAsync().ConfigureAwait(false))
        {
            context.PreventNavigation();
        }
    }

    private async Task OnSelectedMapChangingAsync(MapChangingArgs eventArgs)
    {
        eventArgs.Cancel = !await this.AllowChangeAsync().ConfigureAwait(true);
        if (!eventArgs.Cancel)
        {
            this.SelectedMapId = eventArgs.NextMap;
        }
    }

    private async ValueTask<bool> AllowChangeAsync()
    {
        var cancellationToken = this._disposeCts?.Token ?? default;
        var persistenceContext = await this.GameConfigurationSource
            .GetContextAsync(cancellationToken)
            .ConfigureAwait(true);

        if (persistenceContext?.HasChanges is not true)
        {
            return true;
        }

        var isConfirmed = await this.JavaScript
            .InvokeAsync<bool>("window.confirm", cancellationToken, Resources.UnsavedChangesQuestion)
            .ConfigureAwait(true);

        if (!isConfirmed)
        {
            return false;
        }

        await this.GameConfigurationSource.DiscardChangesAsync().ConfigureAwait(true);
        this._maps = null;

        this._context = await this.GameConfigurationSource
            .GetContextAsync(cancellationToken)
            .ConfigureAwait(true);

        return true;
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        IDisposable? modal = null;
        var showModalTask = this.InvokeAsync(() => modal = this.ModalService.ShowLoadingIndicator());

        try
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                var gameConfig = await this.GameConfigurationSource
                    .GetOwnerAsync(Guid.Empty, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    this._maps = gameConfig.Maps.OrderBy(c => c.Number).ToList();
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(
                        ex,
                        "Could not load game maps: {Message}{NewLine}{StackTrace}",
                        ex.Message,
                        Environment.NewLine,
                        ex.StackTrace);

                    await this.ModalService
                        .ShowMessageAsync(Resources.Error, Resources.CouldNotLoadMapDataCheckTheLogs)
                        .ConfigureAwait(false);
                }

                await showModalTask.ConfigureAwait(false);
                modal?.Dispose();
                await this.InvokeAsync(this.StateHasChanged).ConfigureAwait(false);
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ObjectDisposedException)
        {
            // See ObjectDisposedException.
        }
        catch (ObjectDisposedException)
        {
            // Happens when the user navigated away. The persistence layer does not
            // yet have a cancellation token based async API so we swallow this.
        }
    }

    private async Task SaveChangesAsync()
    {
        try
        {
            var context = await this.GameConfigurationSource.GetContextAsync().ConfigureAwait(true);
            var success = await context.SaveChangesAsync().ConfigureAwait(true);
            var text = success ? Resources.SavedChanges : Resources.NoChangesToSave;
            this.ToastService.ShowSuccess(text);

            if (success)
            {
                await this.ReloadEditedMapOnServersAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Error during saving");
            this.ToastService.ShowError(string.Format(Resources.UnexpectedErrorCheckLogs, ex.Message));
        }
    }

    /// <summary>
    /// Drops the cached <see cref="GameMap"/> instance for the currently-edited
    /// map on every running game server. Without this, the in-memory map still
    /// references the pre-save <see cref="GameMapDefinition.MonsterSpawns"/>
    /// collection so old monsters keep respawning even after the admin panel
    /// shows the changes — same gap that the map-package import already fills.
    /// Best-effort: errors are logged but never surface to the user.
    /// </summary>
    private async Task ReloadEditedMapOnServersAsync()
    {
        var editedMap = this._maps?.FirstOrDefault(m => m.GetId() == this.SelectedMapId);
        if (editedMap is null)
        {
            return;
        }

        var mapId = (ushort)editedMap.Number;
        foreach (var server in this.GameServers.Values)
        {
            if (server is IGameServerContextProvider provider)
            {
                try
                {
                    await provider.Context.ReloadMapAsync(mapId).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    this.Logger.LogWarning(ex, "Failed to reload map {MapId} on server {ServerId} after edit", mapId, server.Id);
                }
            }
        }
    }

    private void BuildMapEditorFragment(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MapEditor>(15);
        builder.AddAttribute(16, nameof(MapEditor.Maps), this._maps);
        builder.AddAttribute(17, nameof(MapEditor.SelectedMapId), this.SelectedMapId);
        builder.AddAttribute(18, nameof(MapEditor.OnValidSubmit), EventCallback.Factory.Create(this, this.SaveChangesAsync));
        builder.AddAttribute(
            19,
            nameof(MapEditor.SelectedMapChanging),
            EventCallback.Factory.Create<MapChangingArgs>(this, this.OnSelectedMapChangingAsync));
        builder.CloseComponent();
    }
}