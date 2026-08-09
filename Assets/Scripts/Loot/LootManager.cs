using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a pickup into a reward choice, one at a time, well after the card that caused it has finished
/// resolving.
///
/// Deliberately a pending queue drained on ActionManager.IsIdle, not a GameAction queued alongside
/// everything else a card does. AddAction runs the first action synchronously up to its first yield
/// (see the gotcha documented on ActionManager.IsIdle), so a loot action queued from inside
/// MoveAction.Execute would resolve *before* the rest of the card that moved the character there - a
/// card that damages and moves in one play could open a reward panel for a character about to die, or
/// interrupt its own resolution to wait on the player. Draining separately, once the whole card and any
/// other in-flight actions have settled, avoids both.
///
/// The picker is also passed in explicitly by GridTile.TryPickUpItem rather than read back off an
/// ActionContext, which matters for a future "damage and move the target" card: ctx.source there would
/// be the caster, not whoever actually landed on the loot.
/// </summary>
public class LootManager : Singleton<LootManager>
{
    private struct Pickup
    {
        public Character picker;
        public Rarity tier;
        public LootTable table;
    }

    [SerializeField] private CardLibrary library;

    [Tooltip("One button per entry on the reward panel. A second skip option later is a new SkipReward "
             + "asset added here, nothing else changes.")]
    [SerializeField] private List<SkipReward> skipRewards = new();

    [SerializeField] private RewardPanel panel;

    [Tooltip("Used when neither the dropping character nor the level has a LootTable assigned.")]
    [SerializeField] private LootTable fallbackTable;

    private readonly Queue<Pickup> pending = new();

    private bool resolving;

    /// True when nothing is queued and no panel is being shown - what BattleManager's turn loop waits
    /// on before letting the round roll over. See LootManager-idle waits in EnemyResolve/RunBattle.
    public bool IsIdle => !resolving && pending.Count == 0;

    /// Queues a reward offer for `picker`. Safe to call from inside GridTile.TryPickUpItem, itself
    /// called synchronously from GridManager.MoveCharacter - this only enqueues; Drain is what waits
    /// for the move (and the rest of its card) to actually finish before opening anything.
    public void QueuePickup(Character picker, Rarity tier, LootTable table)
    {
        if (picker == null) { return; }

        pending.Enqueue(new Pickup { picker = picker, tier = tier, table = table });

        if (!resolving) { StartCoroutine(Drain()); }
    }

    private IEnumerator Drain()
    {
        resolving = true;

        while (pending.Count > 0)
        {
            yield return new WaitUntil(() => ActionManager.Instance == null || ActionManager.Instance.IsIdle);

            Pickup pickup = pending.Dequeue();

            // The card that put them here may also have killed them, or a second pickup in the same
            // batch may have already resolved and removed them from the board. Either way, nobody to
            // show a panel to.
            if (pickup.picker == null || pickup.picker.IsDead) { continue; }

            LootTable table = pickup.table != null ? pickup.table : fallbackTable;

            if (table == null)
            {
                Debug.LogWarning($"LootManager: no LootTable reached {pickup.picker.name}'s pickup and "
                                 + "no fallback is assigned - reward skipped");
                continue;
            }

            List<CardData> candidates = BuildCandidates(pickup.tier, pickup.picker, table);

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }

            if (panel != null)
            {
                panel.Show(candidates, skipRewards);

                yield return new WaitUntil(() => panel.Resolved);

                if (panel.ChosenCard != null)
                {
                    pickup.picker.AddCardToHand(panel.ChosenCard);

                    if (BattleManager.Instance != null)
                    {
                        BattleManager.Instance.RecordRunCard(pickup.picker, panel.ChosenCard);
                    }
                }
                else if (panel.ChosenSkip != null)
                {
                    panel.ChosenSkip.Grant(pickup.picker);
                }
            }

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
        }

        resolving = false;
    }

    /// <summary>
    /// `table.ChoiceCount` distinct cards `picker` may hold, weighted by `table`. Widens down through
    /// lower tiers when the pool at `tier` is thin - a Rare drop for a class with only one Rare card
    /// still offers three choices rather than one, borrowing from Uncommon and then Common. The tag
    /// weighting carries through the widening, so a poison table still favours poison among whatever it
    /// borrowed.
    /// </summary>
    private List<CardData> BuildCandidates(Rarity tier, Character picker, LootTable table)
    {
        List<CardData> found = new();

        if (library == null) { return found; }

        HashSet<CardData> seen = new();
        Rarity current = tier;

        // At most four tiers exist (Common..Legendary), so four attempts always bottoms out at Common.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            foreach (CardData card in library.Offerable(current, picker))
            {
                if (seen.Add(card)) { found.Add(card); }
            }

            if (found.Count >= table.ChoiceCount || current == Rarity.Common) { break; }

            current = current.Lower();
        }

        if (found.Count < table.ChoiceCount)
        {
            Debug.LogWarning($"LootManager: only {found.Count} card(s) offerable to {picker.name} at "
                             + $"{tier} or below (wanted {table.ChoiceCount}) - the pool is thin, "
                             + "author more cards at this rarity/class or widen it further.");
        }

        return WeightedSample(found, table, table.ChoiceCount);
    }

    /// Picks up to `count` distinct cards from `pool` without replacement, each draw weighted by
    /// `table.WeightOf` - the mechanism a poison skeleton's tagWeights actually bias through.
    private static List<CardData> WeightedSample(List<CardData> pool, LootTable table, int count)
    {
        List<CardData> remaining = new(pool);
        List<CardData> result = new();

        while (result.Count < count && remaining.Count > 0)
        {
            int totalWeight = 0;

            foreach (CardData card in remaining) { totalWeight += Mathf.Max(0, table.WeightOf(card)); }

            int index;

            if (totalWeight <= 0)
            {
                // Every remaining candidate weighed to zero (an authored cardWeights of 0, say) -
                // uniform pick rather than offering nothing.
                index = Random.Range(0, remaining.Count);
            }
            else
            {
                int roll = Random.Range(0, totalWeight);
                index = remaining.Count - 1;

                for (int i = 0; i < remaining.Count; i++)
                {
                    int weight = Mathf.Max(0, table.WeightOf(remaining[i]));

                    if (roll < weight) { index = i; break; }

                    roll -= weight;
                }
            }

            result.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return result;
    }
}
