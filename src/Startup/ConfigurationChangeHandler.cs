// <copyright file="ConfigurationChangeHandler.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Startup;

using Microsoft.Extensions.DependencyInjection;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// An implementation of <see cref="IConfigurationChangePublisher"/> which directly handles the changes
/// by updating some components and forwarding the events to the <see cref="IConfigurationChangeMediator"/>.
/// </summary>
public class ConfigurationChangeHandler : IConfigurationChangePublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigurationChangeMediatorListener _changeMediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationChangeHandler" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="changeMediator">The change mediator.</param>
    public ConfigurationChangeHandler(IServiceProvider serviceProvider, IConfigurationChangeMediatorListener changeMediator)
    {
        this._serviceProvider = serviceProvider;
        this._changeMediator = changeMediator;
    }

    /// <inheritdoc />
    public async Task ConfigurationChangedAsync(Type type, Guid id, object configuration)
    {
        // TODO: subscribe these systems to the change mediator
        if (configuration is PlugInConfiguration plugInConfiguration)
        {
            this.OnPlugInConfigurationChanged(id, plugInConfiguration);
        }

        if (configuration is ConnectServerDefinition connectServerDefinition)
        {
            await this.OnConnectServerDefinitionChangedAsync(id, connectServerDefinition).ConfigureAwait(false);
        }

        if (configuration is SystemConfiguration systemConfiguration)
        {
            this.OnSystemConfigurationChanged(id, systemConfiguration);
        }

        if (configuration is MonsterDefinition monsterDefinition)
        {
            this.OnMonsterDefinitionChanged(monsterDefinition);
        }

        await this._changeMediator.HandleConfigurationChangedAsync(type, id, configuration).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles admin-panel saves on a <see cref="MonsterDefinition"/> so the change goes
    /// live without a server restart. Two pieces:
    ///   1. Mutate the static attribute cache in <see cref="MonsterAttributeHolder"/> in
    ///      place — every live monster instance of this definition shares the dict by
    ///      reference, so the next read of <c>Stats.Level</c>, <c>Stats.MaximumHealth</c>,
    ///      damage, defense, etc. returns the new value. Server-side combat formulas pick
    ///      up the change immediately, no respawn required.
    ///   2. Fire-and-forget a broadcast that walks every live game server's maps, locates
    ///      the matching monster instances, and pushes the new level to each observer via
    ///      <see cref="IShowMonsterLevelPlugIn"/> plus an HP-sync packet so the client's
    ///      floating bar (level prefix + fill ratio) updates without waiting for the next
    ///      combat event. Runs on the thread pool — we never block the admin save handler.
    /// </summary>
    private void OnMonsterDefinitionChanged(MonsterDefinition monsterDefinition)
    {
        MonsterAttributeHolder.RefreshAttributeCache(monsterDefinition);

        // Background broadcast — never block the admin save handler.
        _ = Task.Run(async () =>
        {
            try
            {
                await this.BroadcastMonsterDefinitionUpdateAsync(monsterDefinition).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallow — broadcast is best-effort; if anything throws we don't want
                // an unobserved task exception. Server-side cache mutation already
                // succeeded above, so combat behavior is correct regardless.
            }
        });
    }

    private async Task BroadcastMonsterDefinitionUpdateAsync(MonsterDefinition monsterDefinition)
    {
        var gameServers = this._serviceProvider.GetService<IDictionary<int, IGameServer>>();
        if (gameServers is null)
        {
            return;
        }

        foreach (var gameServer in gameServers.Values)
        {
            if (gameServer is not IGameServerContextProvider provider)
            {
                continue;
            }

            var context = provider.Context;
            var maps = await context.GetMapsAsync().ConfigureAwait(false);

            foreach (var map in maps)
            {
                foreach (var locateable in map.GetAllLocateables())
                {
                    if (locateable is not Monster monster)
                    {
                        continue;
                    }

                    if (!ReferenceEquals(monster.Definition, monsterDefinition))
                    {
                        continue;
                    }

                    await this.NotifyObserversForMonsterAsync(monster).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task NotifyObserversForMonsterAsync(Monster monster)
    {
        // Push the new server-truth level so the floating bar's "(level) Name"
        // prefix refreshes immediately.
        var level = monster.Attributes[Stats.Level];
        var ushortLevel = level > 0 && !float.IsNaN(level) ? (ushort)level : (ushort)0;

        await monster.ForEachWorldObserverAsync<IShowMonsterLevelPlugIn>(
            p => p.ShowMonsterLevelAsync(monster, ushortLevel),
            includeThis: true).ConfigureAwait(false);

        // HP-sync (ObjectHitExtended with damage = 0) re-anchors the bar fill
        // ratio so a MaximumHealth change shows on the client immediately —
        // same packet NewNpcsInScopePlugIn uses for HP sync on scope-in.
        var maxHp = monster.Attributes[Stats.MaximumHealth];
        if (maxHp > 0 && !float.IsNaN(maxHp))
        {
            await monster.ForEachWorldObserverAsync<IShowHitPlugIn>(
                p => p.ShowHitAsync(monster, new HitInfo(0, 0, DamageAttributes.Undefined)),
                includeThis: true).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ConfigurationAddedAsync(Type type, Guid id, object configuration)
    {
        await this._changeMediator.HandleConfigurationAddedAsync(type, id, configuration).ConfigureAwait(false);

        if (type.IsAssignableTo(typeof(PlugInConfiguration)) && this._serviceProvider.GetService<PlugInManager>() is { } plugInManager)
        {
            // todo: find out what to do, because usually, plugin configs are not added during runtime.
        }
    }

    /// <inheritdoc />
    public async Task ConfigurationRemovedAsync(Type type, Guid id)
    {
        if (type.IsAssignableTo(typeof(PlugInConfiguration)) && this._serviceProvider.GetService<PlugInManager>() is { } plugInManager)
        {
            plugInManager.DeactivatePlugIn(id);
        }

        await this._changeMediator.HandleConfigurationRemovedAsync(type, id).ConfigureAwait(false);
    }

    private void OnSystemConfigurationChanged(Guid id, SystemConfiguration systemConfiguration)
    {
        if (this._serviceProvider.GetService<IIpAddressResolver>() is not ConfigurableIpResolver ipAddressResolver)
        {
            return;
        }

        ipAddressResolver.Configure(systemConfiguration.IpResolver, systemConfiguration.IpResolverParameter);
    }

    private async ValueTask OnConnectServerDefinitionChangedAsync(Guid id, ConnectServerDefinition connectServerDefinition)
    {
        if (this._serviceProvider.GetService<ConnectServerContainer>() is not { } connectServerContainer)
        {
            return;
        }

        foreach (var connectServer in connectServerContainer)
        {
            if (connectServer.ServerState == ServerState.Started)
            {
                await connectServer.ShutdownAsync().ConfigureAwait(false);

                //// todo: is applying new settings required?
                await connectServer.StartAsync().ConfigureAwait(false);
            }
        }
    }

    private void OnPlugInConfigurationChanged(Guid id, PlugInConfiguration plugInConfiguration)
    {
        if (this._serviceProvider.GetService<PlugInManager>() is not { } plugInManager)
        {
            return;
        }

        var typeId = plugInConfiguration.TypeId;
        var currentlyActive = plugInManager.IsPlugInActive(typeId);
        if (currentlyActive && !plugInConfiguration.IsActive)
        {
            plugInManager.DeactivatePlugIn(typeId);
        }
        else if (!currentlyActive && plugInConfiguration.IsActive)
        {
            plugInManager.ActivatePlugIn(typeId);
        }
        else
        {
            plugInManager.ConfigurePlugIn(typeId, plugInConfiguration);
        }
    }
}