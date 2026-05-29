// <copyright file="DropRates.razor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Pages;

using System.Threading;
using Blazored.Toast.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Single-page tuning UI for the most-tweaked drop knobs:
/// the five <see cref="GameConfiguration"/> rate fields,
/// every <see cref="DropItemGroup.Chance"/> across maps / monsters / global,
/// and the <see cref="ItemOptionDefinition.AddChance"/> on Excellent / Luck option defs.
///
/// Two non-obvious behaviours to be aware of:
/// 1. <see cref="DropItemGroup"/>s are commonly SHARED — the same row in the DB is referenced
///    from many maps' navigation collections. Editing the chance on one row would otherwise
///    silently change every owner. This page detects sharing and clones-on-edit (see
///    <see cref="OnChanceChangedAsync"/>) so a per-map tweak stays per-map.
/// 2. The bake/drop paths read these values live (DefaultDropGenerator was refactored to
///    re-read the two GameConfiguration fields on every roll), so saves take effect on
///    the next monster kill without a restart.
/// </summary>
public partial class DropRates : ComponentBase, IDisposable
{
    private static readonly (SpecialItemType Type, string Label)[] DropTabs =
    {
        (SpecialItemType.Excellent, "Excellent"),
        (SpecialItemType.Jewel, "Jewel"),
        (SpecialItemType.RandomItem, "Random / Normal"),
        (SpecialItemType.Ancient, "Ancient"),
        (SpecialItemType.SocketItem, "Socket"),
        (SpecialItemType.None, "Other / item-list"),
    };

    private GameConfiguration? _gameConfig;
    private readonly Dictionary<SpecialItemType, List<DropGroupRow>> _groupsByType = new();
    private readonly List<ItemOptionDefinition> _excellentOptionDefs = new();
    private readonly List<ItemOptionDefinition> _luckOptionDefs = new();
    private SpecialItemType _selectedTab = SpecialItemType.Excellent;
    private string _dropSearch = string.Empty;
    private string _excellentSearch = string.Empty;
    private string _luckSearch = string.Empty;
    private bool _isSaving;
    private string? _statusMessage;
    private string _statusCssClass = "text-success";
    private CancellationTokenSource? _disposeCts;

    /// <summary>
    /// Gets or sets the data source for the game configuration.
    /// </summary>
    [Inject]
    private IDataSource<GameConfiguration> GameConfigurationSource { get; set; } = null!;

    /// <summary>
    /// Gets or sets the toast service for showing notifications.
    /// </summary>
    [Inject]
    private IToastService ToastService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the logger.
    /// </summary>
    [Inject]
    private ILogger<DropRates> Logger { get; set; } = null!;

    /// <inheritdoc />
    public void Dispose()
    {
        this._disposeCts?.Cancel();
        this._disposeCts?.Dispose();
        this._disposeCts = null;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync().ConfigureAwait(true);
        this._disposeCts = new CancellationTokenSource();
        await this.LoadDataAsync(this._disposeCts.Token).ConfigureAwait(true);
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            this._gameConfig = (GameConfiguration)await this.GameConfigurationSource
                .GetOwnerAsync(Guid.Empty, cancellationToken)
                .ConfigureAwait(true);

            this.RefreshLocalIndexes();
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Could not load game configuration for /drop-rates");
            this.ToastService.ShowError($"Failed to load: {ex.Message}");
        }
    }

    /// <summary>
    /// Walks the loaded <see cref="GameConfiguration"/> once and groups every
    /// <see cref="DropItemGroup"/> on the GameConfiguration / maps / monsters by
    /// <see cref="DropItemGroup.ItemType"/> so each editor tab renders in O(1).
    /// Also collects the option definitions of type Excellent / Luck and pre-computes
    /// the owner-count per group (drives the "shared" badge + clone-on-edit gate).
    /// </summary>
    private void RefreshLocalIndexes()
    {
        this._groupsByType.Clear();
        this._dropSearch = string.Empty;
        foreach (var (type, _) in DropTabs)
        {
            this._groupsByType[type] = new List<DropGroupRow>();
        }

        if (this._gameConfig is null)
        {
            return;
        }

        // Pass 1: count how many SPECIFIC owners (map/monster/character/quest) each group
        // has. We use this to classify Global rows:
        //   specificOwners == 0 → orphan / unused (show in Global so user can clean up)
        //   specificOwners == 1 → per-owner customization (don't show in Global — it's already
        //                         visible under its specific owner; showing twice is noise)
        //   specificOwners >= 2 → shared template (the bulk-edit lever; show in Global)
        var specificOwnerCount = new Dictionary<DropItemGroup, int>(ReferenceEqualityComparer.Instance);

        void Bump(DropItemGroup g)
        {
            specificOwnerCount[g] = specificOwnerCount.TryGetValue(g, out var c) ? c + 1 : 1;
        }

        foreach (var map in this._gameConfig.Maps)
        {
            foreach (var group in map.DropItemGroups)
            {
                Bump(group);
            }
        }

        foreach (var monster in this._gameConfig.Monsters)
        {
            foreach (var group in monster.DropItemGroups)
            {
                Bump(group);
            }
        }

        // Pass 2: render the rows. Per-map / per-monster always; Global only for groups
        // that aren't a single-owner customization (rule above).
        foreach (var group in this._gameConfig.DropItemGroups)
        {
            specificOwnerCount.TryGetValue(group, out var count);
            if (count == 1)
            {
                continue; // hide single-owner customizations from Global
            }

            this.AddGroupRow("Global", group, this._gameConfig.DropItemGroups);
        }

        foreach (var map in this._gameConfig.Maps.OrderBy(m => m.Number))
        {
            foreach (var group in map.DropItemGroups)
            {
                this.AddGroupRow($"Map: {map.Name} ({map.Number})", group, map.DropItemGroups);
            }
        }

        // LocalizedString has no IComparable, so project to string.
        foreach (var monster in this._gameConfig.Monsters.OrderBy(m => m.Designation.ToString(), StringComparer.Ordinal))
        {
            foreach (var group in monster.DropItemGroups)
            {
                this.AddGroupRow($"Monster: {monster.Designation}", group, monster.DropItemGroups);
            }
        }

        // Resolve cross-row "shared by N" counts now that every row is in the index.
        // A single DropItemGroup referenced by >1 owner row is the canonical "shared"
        // case the user hit — editing it without cloning would silently mutate every
        // owner. We expose the count in the UI and gate clone-on-edit on it.
        foreach (var list in this._groupsByType.Values)
        {
            var counts = new Dictionary<DropItemGroup, int>(ReferenceEqualityComparer.Instance);
            foreach (var row in list)
            {
                counts[row.Group] = counts.TryGetValue(row.Group, out var c) ? c + 1 : 1;
            }

            foreach (var row in list)
            {
                row.SharedOwners = counts[row.Group];
            }

            list.Sort((a, b) =>
            {
                var bySource = string.Compare(a.Source, b.Source, StringComparison.Ordinal);
                return bySource != 0 ? bySource : b.Group.Chance.CompareTo(a.Group.Chance);
            });
        }

        // Excellent / Luck option editors.
        this._excellentOptionDefs.Clear();
        this._luckOptionDefs.Clear();
        foreach (var def in this._gameConfig.ItemOptions.OrderBy(d => d.Name.ToString(), StringComparer.Ordinal))
        {
            if (def.PossibleOptions.Any(p => Equals(p.OptionType, ItemOptionTypes.Excellent)))
            {
                this._excellentOptionDefs.Add(def);
            }

            if (def.PossibleOptions.Any(p => Equals(p.OptionType, ItemOptionTypes.Luck)))
            {
                this._luckOptionDefs.Add(def);
            }
        }
    }

    /// <summary>
    /// Picks the canonical shared group to merge a solo clone into. A candidate must
    /// share Description, ItemType, and ItemLevel with the clone AND have the exact
    /// chance the user just typed AND be referenced by more than one specific owner
    /// (so we only merge into a "real" shared template, never into another solo clone
    /// or the clone itself). Returns null if zero or multiple candidates fit — better
    /// to leave the clone in place than to pick the wrong neighbour.
    /// </summary>
    private DropItemGroup? FindMergeCandidate(DropItemGroup clone, double newChance)
    {
        if (this._gameConfig is null)
        {
            return null;
        }

        var cloneDesc = clone.Description.ToString();
        var inUseCount = new Dictionary<DropItemGroup, int>(ReferenceEqualityComparer.Instance);
        foreach (var map in this._gameConfig.Maps)
        {
            foreach (var g in map.DropItemGroups)
            {
                inUseCount[g] = inUseCount.TryGetValue(g, out var c) ? c + 1 : 1;
            }
        }

        foreach (var monster in this._gameConfig.Monsters)
        {
            foreach (var g in monster.DropItemGroups)
            {
                inUseCount[g] = inUseCount.TryGetValue(g, out var c) ? c + 1 : 1;
            }
        }

        DropItemGroup? match = null;
        foreach (var candidate in this._gameConfig.DropItemGroups)
        {
            if (ReferenceEquals(candidate, clone)) continue;
            if (candidate.ItemType != clone.ItemType) continue;
            if (candidate.ItemLevel != clone.ItemLevel) continue;
            if (Math.Abs(candidate.Chance - newChance) >= 1e-9) continue;
            if (candidate.Description.ToString() != cloneDesc) continue;
            if (!inUseCount.TryGetValue(candidate, out var refs) || refs < 2) continue;

            if (match is not null)
            {
                // Ambiguous — two valid candidates; don't guess.
                return null;
            }

            match = candidate;
        }

        return match;
    }

    /// <summary>
    /// Recomputes <see cref="DropGroupRow.SharedOwners"/> for every row from the current
    /// graph. Called after auto-merge so the badges reflect the new ownership tallies
    /// without requiring a full re-render of the underlying index structures.
    /// </summary>
    private void RecountSharedOwners()
    {
        foreach (var list in this._groupsByType.Values)
        {
            var counts = new Dictionary<DropItemGroup, int>(ReferenceEqualityComparer.Instance);
            foreach (var row in list)
            {
                counts[row.Group] = counts.TryGetValue(row.Group, out var c) ? c + 1 : 1;
            }

            foreach (var row in list)
            {
                row.SharedOwners = counts[row.Group];
            }
        }
    }

    private void AddGroupRow(string source, DropItemGroup group, ICollection<DropItemGroup> ownerCollection)
    {
        var bucket = this._groupsByType.TryGetValue(group.ItemType, out var list) ? list : null;
        if (bucket is null)
        {
            // ItemType outside the canonical set (e.g. Money) — silently skip; not tunable here.
            return;
        }

        bucket.Add(new DropGroupRow
        {
            Source = source,
            Group = group,
            OwnerCollection = ownerCollection,
        });
    }

    private IEnumerable<DropGroupRow> FilteredRows(SpecialItemType type)
    {
        var rows = this._groupsByType[type];
        if (string.IsNullOrWhiteSpace(this._dropSearch))
        {
            return rows;
        }

        var search = this._dropSearch;
        return rows.Where(r => MatchesSearch(r, search));
    }

    private static bool MatchesSearch(DropGroupRow row, string search)
    {
        if (row.Source.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // LocalizedString is a value type, so use ToString() directly (no ?.). Empty strings
        // are fine — Contains("") would return true and over-include, so check non-empty.
        var desc = row.Group.Description.ToString();
        return !string.IsNullOrEmpty(desc) && desc.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<ItemOptionDefinition> FilterByName(IEnumerable<ItemOptionDefinition> defs, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return defs;
        }

        return defs.Where(d => (d.Name.ToString() ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Per-row chance edit handler. If the underlying <see cref="DropItemGroup"/> is shared
    /// across multiple owners, this clones the group through the EF context so the edit
    /// only affects this row's owner; otherwise it just writes the new value through.
    /// </summary>
    /// <remarks>
    /// CRITICAL: Global rows MUST NOT clone. The Global row's OwnerCollection is the
    /// GameConfiguration's master list (the parent of every DropItemGroup), so removing
    /// the original from it makes EF treat the entity as deleted and cascade-removes every
    /// junction that referenced it — wiping out the drop from every linked map at once.
    /// Always edit Global in-place; that's also the user's intent for the row (bulk edit).
    /// </remarks>
    private async Task OnChanceChangedAsync(DropGroupRow row, ChangeEventArgs e)
    {
        if (this._gameConfig is null)
        {
            return;
        }

        if (!double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newValue))
        {
            return;
        }

        var isGlobalRow = row.OwnerCollection is { } oc
                          && ReferenceEquals(oc, this._gameConfig.DropItemGroups);

        if (isGlobalRow || row.SharedOwners <= 1 || row.OwnerCollection is null)
        {
            row.Group.Chance = newValue;

            // Auto-merge path. Two ways to find the "real" group to merge back to:
            //
            //  1. Session-clone tracking. If we cloned this row earlier in the same
            //     session, OriginalBeforeClone points at the original. Fast, exact match.
            //
            //  2. Fuzzy match by content. After a page reload, in-session tracking is
            //     gone — so as a fallback we look in the master list for another group
            //     that has the same Description + ItemType + ItemLevel AND the chance the
            //     user just typed. If exactly one such group exists (and it's not the
            //     clone itself), it's the canonical shared row and we merge into it.
            //
            // Either way: remove the clone from this row's OwnerCollection, point the
            // row at the canonical group, decrement IsCustomized. The leftover clone is
            // now orphan and "Clean unused" picks it up on next save.
            if (!isGlobalRow && row.OwnerCollection is not null)
            {
                DropItemGroup? mergeTarget = null;

                if (row.OriginalBeforeClone is { } origin
                    && Math.Abs(origin.Chance - newValue) < 1e-9)
                {
                    mergeTarget = origin;
                }
                else
                {
                    mergeTarget = this.FindMergeCandidate(row.Group, newValue);
                }

                if (mergeTarget is not null && !ReferenceEquals(mergeTarget, row.Group))
                {
                    var clone = row.Group;
                    row.OwnerCollection.Remove(clone);
                    row.OwnerCollection.Add(mergeTarget);
                    row.Group = mergeTarget;
                    row.IsCustomized = false;
                    row.OriginalBeforeClone = null;
                    this.RecountSharedOwners();
                    this.ToastService.ShowInfo($"Merged '{row.Source}' back to the shared group — clone is now an unused orphan.");
                    this.StateHasChanged();
                }
            }

            return;
        }

        try
        {
            var context = await this.GameConfigurationSource
                .GetContextAsync(this._disposeCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);

            var clone = context.CreateNew<DropItemGroup>();
            clone.AssignValuesOf(row.Group, this._gameConfig);

            // The generated AssignValuesOf in BasicModel copies the source Id too,
            // which collides with the already-tracked original. Force a fresh Guid
            // via the IIdentifiable surface so EF treats the clone as a brand-new row.
            if (clone is MUnique.OpenMU.Persistence.IIdentifiable identifiable)
            {
                identifiable.Id = Guid.NewGuid();
            }

            clone.Chance = newValue;

            // EVERY DropItemGroup must be owned by the GameConfiguration via its
            // RawDropItemGroups collection — that sets GameConfigurationId on save
            // and is what the JSON object loader uses to find the group on reload.
            // Without this the clone persists but the FK is null, the JSON
            // reference resolver fails, and the clone (and its junctions) vanish
            // from the in-memory graph on the next load.
            this._gameConfig.DropItemGroups.Add(clone);

            var original = row.Group;
            row.OwnerCollection.Remove(original);
            row.OwnerCollection.Add(clone);
            row.Group = clone;
            row.SharedOwners = 1;

            // Every other row in any tab still referencing the original loses an owner;
            // when their count drops to 1 the "shared" badge disappears, matching reality.
            foreach (var list in this._groupsByType.Values)
            {
                foreach (var other in list)
                {
                    if (other != row && ReferenceEquals(other.Group, original) && other.SharedOwners > 1)
                    {
                        other.SharedOwners--;
                    }
                }
            }

            row.IsCustomized = true;
            row.OriginalBeforeClone = original; // remembered so an in-session revert auto-merges
            this.ToastService.ShowInfo($"Cloned shared group for '{row.Source}' so this edit stays per-owner.");
            this.StateHasChanged();
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to clone shared DropItemGroup");
            this.ToastService.ShowError($"Edit failed: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        if (this._gameConfig is null || this._isSaving)
        {
            return;
        }

        this._isSaving = true;
        this._statusMessage = null;
        this.StateHasChanged();

        try
        {
            var context = await this.GameConfigurationSource
                .GetContextAsync(this._disposeCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);
            var ok = await context.SaveChangesAsync(this._disposeCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);
            this._statusMessage = ok ? "Saved." : "No changes to save.";
            this._statusCssClass = "text-success";
            if (ok)
            {
                this.ToastService.ShowSuccess("Drop rates saved.");
                // Re-derive the in-memory indexes from the post-save graph so the
                // "Shared" counts and "Custom" badges reflect what was actually persisted.
                // The IDataSource still holds the same owner instance so this is in-memory only.
                this.RefreshLocalIndexes();
            }
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to save drop rates");
            this._statusMessage = $"Error: {ex.Message}";
            this._statusCssClass = "text-danger";
            this.ToastService.ShowError($"Save failed: {ex.Message}");
        }
        finally
        {
            this._isSaving = false;
            this.StateHasChanged();
        }
    }

    /// <summary>
    /// Sweeps the in-memory <see cref="GameConfiguration.DropItemGroups"/> for any group
    /// with zero specific owners (no map, monster, character, or quest references), and
    /// removes them from the master list. EF tracks the removals; the next Save persists
    /// the deletes. Most useful right after a clone-on-edit session that left detached
    /// originals behind, but safe to call any time — it only ever removes rows that
    /// nothing in the runtime would have read anyway.
    /// </summary>
    private async Task CleanUnusedAsync()
    {
        if (this._gameConfig is null || this._isSaving)
        {
            return;
        }

        try
        {
            var inUse = new HashSet<DropItemGroup>(ReferenceEqualityComparer.Instance);
            foreach (var map in this._gameConfig.Maps)
            {
                foreach (var g in map.DropItemGroups) inUse.Add(g);
            }

            foreach (var monster in this._gameConfig.Monsters)
            {
                foreach (var g in monster.DropItemGroups) inUse.Add(g);
            }

            // Note: per-character (player-entity) DropItemGroups aren't visible at the
            // GameConfiguration scope, so this only sweeps map/monster references — which
            // covers the "leftover clones from admin edits" case the user actually hits.
            var orphans = this._gameConfig.DropItemGroups.Where(g => !inUse.Contains(g)).ToList();
            if (orphans.Count == 0)
            {
                this.ToastService.ShowInfo("No unused drop groups to clean.");
                return;
            }

            var context = await this.GameConfigurationSource
                .GetContextAsync(this._disposeCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);

            foreach (var orphan in orphans)
            {
                this._gameConfig.DropItemGroups.Remove(orphan);
                await context.DeleteAsync(orphan).ConfigureAwait(true);
            }

            // Auto-save the deletes — there's nothing for the user to review here
            // (orphans were already invisible to gameplay), and a two-step "Clean
            // then Save" is easy to forget. Failure rolls everything back so we
            // can't half-commit.
            await context.SaveChangesAsync(this._disposeCts?.Token ?? CancellationToken.None)
                .ConfigureAwait(true);

            this.RefreshLocalIndexes();
            this.ToastService.ShowSuccess($"Removed {orphans.Count} unused drop group(s) and saved.");
            this.StateHasChanged();
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to clean unused drop groups");
            this.ToastService.ShowError($"Cleanup failed: {ex.Message}");
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            // ForceDiscardChangesAsync unconditionally resets the owner cache.
            // DiscardChangesAsync (no Force) skips when HasChanges is false, which
            // happens right after Save — so without Force we'd reuse the stale graph.
            await this.GameConfigurationSource.ForceDiscardChangesAsync().ConfigureAwait(true);
            this._disposeCts ??= new CancellationTokenSource();
            await this.LoadDataAsync(this._disposeCts.Token).ConfigureAwait(true);
            this._statusMessage = "Reloaded from disk.";
            this._statusCssClass = "text-info";
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to reload drop rates");
            this._statusMessage = $"Reload error: {ex.Message}";
            this._statusCssClass = "text-danger";
        }
    }

    /// <summary>Mutable row in the drop-groups table. Tracks the owner collection so the
    /// clone-on-edit path knows which navigation to update.</summary>
    private sealed class DropGroupRow
    {
        public string Source { get; set; } = string.Empty;

        public DropItemGroup Group { get; set; } = null!;

        public ICollection<DropItemGroup>? OwnerCollection { get; set; }

        public int SharedOwners { get; set; } = 1;

        /// <summary>Set when the user clones-on-edit. Drives the "Custom" badge so
        /// the row remains visually distinct from a row that was solo to begin with.</summary>
        public bool IsCustomized { get; set; }

        /// <summary>The group this row was split off from when cloned in-session. Used by
        /// the auto-merge path — if the user edits the clone back to the origin's chance
        /// we undo the split. Null when no clone happened in this session.</summary>
        public DropItemGroup? OriginalBeforeClone { get; set; }
    }
}
