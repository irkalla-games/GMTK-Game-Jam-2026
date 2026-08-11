using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns an occasion into a reward choice: a mid-battle pickup, or a hero's share of a level-clear
/// reward. Both funnel through the same pool (CardLibrary), the same weighted sampler, and the same
/// RewardPanel - only the LootTable, the skip options offered, and what a chosen card is granted into
/// differ between them.
///
/// The pickup half is deliberately a pending queue drained on ActionManager.IsIdle, not a GameAction
/// queued alongside everything else a card does. AddAction runs the first action synchronously up to
/// its first yield (see the gotcha documented on ActionManager.IsIdle), so a loot action queued from
/// inside MoveAction.Execute would resolve *before* the rest of the card that moved the character there
/// - a card that damages and moves in one play could open a reward panel for a character about to die,
/// or interrupt its own resolution to wait on the player. Draining separately, once the whole card and
/// any other in-flight actions have settled, avoids both.
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

    [Tooltip("One button per entry on a mid-battle pickup's reward panel. A second skip option later "
             + "is a new SkipReward asset added here, nothing else changes.")]
    [SerializeField] private List<SkipReward> skipRewards = new();

    [Tooltip("One button per entry on the level-clear reward panel - Skip and Remove a Card by "
             + "default. Kept separate from skipRewards above: the two occasions offer genuinely "
             + "different things, Heal makes no sense once the battle is already won, and Remove a "
             + "Card needs the RewardContext.record a mid-battle pickup never sets.")]
    [SerializeField] private List<SkipReward> levelClearSkipRewards = new();

    [SerializeField] private RewardPanel panel;

    [Tooltip("Used when neither the dropping character, the level's drop table, nor a level's clear "
             + "reward table is assigned.")]
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

            List<CardData> candidates = BuildOffer(pickup.tier, pickup.picker, table);

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }

            if (panel != null)
            {
                RewardContext context = new() { character = pickup.picker };

                yield return StartCoroutine(RunOffer(candidates, skipRewards, context));

                if (panel.ChosenCard != null)
                {
                    pickup.picker.AddCardToHand(panel.ChosenCard);

                    if (BattleManager.Instance != null)
                    {
                        BattleManager.Instance.RecordRunCard(pickup.picker, panel.ChosenCard);
                    }
                }
            }

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
        }

        resolving = false;
    }

    /// <summary>
    /// Offers `hero` their share of a level-clear reward, granted straight into their PartyMember
    /// record rather than their hand - the battle is over, there is no hand left to add to by the time
    /// the next level's Character exists. Called once per living hero from BattleManager, in sequence,
    /// so only one panel is ever up at a time.
    /// </summary>
    public IEnumerator OfferLevelClear(Character hero, PartyMember record, LootTable table)
    {
        if (hero == null || record == null || panel == null) { yield break; }

        LootTable resolvedTable = table != null ? table : fallbackTable;

        if (resolvedTable == null)
        {
            Debug.LogWarning($"LootManager: no clear-reward LootTable for {hero.name} and no fallback "
                             + "is assigned - reward skipped");
            yield break;
        }

        Rarity tier = resolvedTable.Roll();
        List<CardData> candidates = BuildOffer(tier, hero, resolvedTable);

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }

        RewardContext context = new() { character = hero, record = record };

        yield return StartCoroutine(RunOffer(candidates, levelClearSkipRewards, context));

        if (panel.ChosenCard != null && BattleManager.Instance != null)
        {
            BattleManager.Instance.RecordRunCard(hero, panel.ChosenCard);
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
    }

    /// <summary>
    /// Shows the panel and waits for a resolution, re-showing the same candidates if the resolution was
    /// a skip that backed out of itself (RewardContext.Reoffer) rather than one that actually resolved
    /// anything. Leaves panel.ChosenCard/ChosenSkip for the caller to read afterward - a card choice
    /// needs no per-occasion branching in here, only what it is granted into differs, and that is the
    /// caller's business (AddCardToHand for a pickup, nothing but the run record for a level clear).
    /// </summary>
    private IEnumerator RunOffer(List<CardData> candidates, List<SkipReward> skips, RewardContext context)
    {
        do
        {
            context.Reoffer = false;

            string title = context.character != null ? $"{context.character.DisplayName}'s reward" : null;
            panel.Show(candidates, skips, title);

            yield return new WaitUntil(() => panel.Resolved);

            if (panel.ChosenSkip != null) { yield return panel.ChosenSkip.Grant(context); }
        }
        while (context.Reoffer);
    }

    /// <summary>
    /// `table.ChoiceCount` distinct cards `picker` may hold, weighted by `table`, minus whatever
    /// `table.GuaranteedCards` already fills. Guarantees are seeded first (see LootTable.GuaranteedCards
    /// and Excludes for the precedence between the two), then the remaining slots widen down through
    /// lower tiers when the pool at `tier` is thin - a Rare drop for a class with only one Rare card
    /// still offers three choices rather than one, borrowing from Uncommon and then Common. The tag
    /// weighting carries through the widening, so a poison table still favours poison among whatever it
    /// borrowed. The result is shuffled so a guaranteed card does not always land leftmost.
    /// </summary>
    public List<CardData> BuildOffer(Rarity tier, Character picker, LootTable table)
    {
        List<CardData> seeded = new();

        if (library == null || table == null) { return seeded; }

        HashSet<CardData> seen = new();

        foreach (CardData card in table.GuaranteedCards)
        {
            if (card == null) { continue; }

            if (table.Excludes(card))
            {
                Debug.LogWarning($"{table.name}: {card.cardName} is in both guaranteedCards and "
                                 + "excludedCards - excluded wins, dropped from the guarantee.");
                continue;
            }

            if (!card.CanBeUsedBy(picker)) { continue; }

            if (seen.Add(card)) { seeded.Add(card); }
        }

        if (seeded.Count > table.ChoiceCount)
        {
            Debug.LogWarning($"{table.name}: {seeded.Count} guaranteed cards exceed choiceCount "
                             + $"{table.ChoiceCount} - only the first {table.ChoiceCount} are offered.");
            seeded.RemoveRange(table.ChoiceCount, seeded.Count - table.ChoiceCount);
        }

        int remaining = table.ChoiceCount - seeded.Count;

        if (remaining > 0)
        {
            List<CardData> found = new();
            Rarity current = tier;

            // At most four tiers exist (Common..Legendary), so four attempts always bottoms out at
            // Common.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                foreach (CardData card in library.Offerable(current, picker))
                {
                    if (table.Excludes(card)) { continue; }
                    if (seen.Add(card)) { found.Add(card); }
                }

                if (found.Count >= remaining || current == Rarity.Common) { break; }

                current = current.Lower();
            }

            if (found.Count < remaining)
            {
                Debug.LogWarning($"LootManager: only {found.Count} card(s) offerable to {picker.name} "
                                 + $"at {tier} or below (wanted {remaining}) - the pool is thin, author "
                                 + "more cards at this rarity/class or widen it further.");
            }

            seeded.AddRange(WeightedSample(found, table, remaining));
        }

        Shuffle(seeded);

        return seeded;
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

    /// Fisher-Yates - used to keep guaranteed cards from always sitting leftmost on the panel.
    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
