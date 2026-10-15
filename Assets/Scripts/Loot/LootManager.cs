using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Tooltip("The pool an equipment offer draws from - a Legendary drop, or a level clear's second panel.")]
    [SerializeField] private EquipmentLibrary equipmentLibrary;

    [Tooltip("One button per entry on a mid-battle pickup's reward panel. A second skip option later "
             + "is a new SkipReward asset added here, nothing else changes.")]
    [SerializeField] private List<SkipReward> skipRewards = new();

    [Tooltip("One button per entry on a level clear's card panel - Skip and Remove a Card. Kept "
             + "separate from skipRewards above: Heal makes no sense once the battle is already won, "
             + "and Remove a Card needs the RewardContext.record a mid-battle pickup never sets.")]
    [FormerlySerializedAs("levelClearSkipRewards")]
    [SerializeField] private List<SkipReward> levelClearCardSkips = new();

    [Tooltip("One button per entry on a level clear's equipment panel - Skip and Upgrade a Card.")]
    [SerializeField] private List<SkipReward> levelClearEquipmentSkips = new();

    [SerializeField] private RewardPanel panel;

    [Tooltip("Used when neither the dropping character, the level's drop table, nor a level's clear "
             + "reward table is assigned.")]
    [SerializeField] private LootTable fallbackTable;

    private readonly Queue<Pickup> pending = new();

    private bool resolving;

    /// True when nothing is queued and no panel is being shown - what BattleManager's turn loop waits
    /// on before letting the round roll over. See LootManager-idle waits in EnemyResolve/RunBattle.
    ///
    /// Flips false the moment a pickup is queued, not when the panel actually opens - Drain below still
    /// has to wait out ActionManager.IsIdle first. A caller that needs to know the panel itself is up
    /// (the tutorial's own explanation of it) wants Panel.IsShowing instead.
    public bool IsIdle => !resolving && pending.Count == 0;

    /// Exposed for the tutorial, which needs the panel itself - not just IsIdle - to anchor its
    /// explanation and to know when it is actually on screen. Read-only, same contract as every other
    /// tutorial exposure in this codebase (PartyPortraitPanel.PortraitRectFor, SelectedCharacterPanel.
    /// StatusRowRect): a caller may read it, never drive the panel through it.
    public RewardPanel Panel => panel;

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

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }

            if (panel != null)
            {
                RewardContext context = new() { character = pickup.picker };

                // Legendary is the equipment tier - every item is Legendary - and Uncommon and Rare are
                // cards. A thin equipment pool must never leave the player facing an empty panel - the
                // Legendary branch only runs
                // when BuildEquipmentOffer actually found something, and falls through to the card
                // offer otherwise (which widens down to Rare, since no card is Legendary).
                List<EquipmentData> equipmentCandidates = pickup.tier == Rarity.Legendary
                    ? BuildEquipmentOffer(pickup.picker, table)
                    : null;

                if (equipmentCandidates != null && equipmentCandidates.Count > 0)
                {
                    yield return StartCoroutine(RunEquipmentOffer(equipmentCandidates, skipRewards, context, RewardTitle(context, null)));

                    if (panel.ChosenEquipment != null)
                    {
                        pickup.picker.Equip(panel.ChosenEquipment);

                        if (BattleManager.Instance != null)
                        {
                            BattleManager.Instance.RecordRunEquipment(pickup.picker, panel.ChosenEquipment);
                        }
                    }
                }
                else
                {
                    List<CardData> candidates = BuildOffer(pickup.tier, pickup.picker, table);

                    yield return StartCoroutine(RunOffer(candidates, skipRewards, context, RewardTitle(context, null)));

                    if (panel.ChosenCard != null)
                    {
                        pickup.picker.AddCardToHand(panel.ChosenCard);

                        if (BattleManager.Instance != null)
                        {
                            BattleManager.Instance.RecordRunCard(pickup.picker, panel.ChosenCard);
                        }
                    }
                }
            }

            if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
        }

        resolving = false;
    }

    /// <summary>
    /// Offers `hero` their share of a level-clear reward: a card panel (Skip / Remove a card), then an
    /// equipment panel (Skip / Upgrade a card). The card panel rolls its tier on `table`; the equipment
    /// panel needs no roll, every item being Legendary - see BuildEquipmentOffer. Both are
    /// granted straight into the PartyMember record rather than the hero - the battle is over, and the
    /// next level's Character is rebuilt from that record. Called once per living hero from
    /// BattleManager, in sequence, so only one panel is ever up at a time.
    ///
    /// The equipment half is skipped silently when nothing is offerable (every item the hero may hold
    /// already held) - an empty panel would be a choice with nothing in it.
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

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(true); }

        RewardContext context = new() { character = hero, record = record };

        List<CardData> cards = BuildOffer(resolvedTable.Roll(), hero, resolvedTable);

        yield return StartCoroutine(RunOffer(cards, levelClearCardSkips, context, RewardTitle(context, "cards")));

        if (panel.ChosenCard != null && BattleManager.Instance != null)
        {
            BattleManager.Instance.RecordRunCard(hero, panel.ChosenCard);
        }

        List<EquipmentData> items = BuildEquipmentOffer(hero, resolvedTable);

        if (items.Count > 0)
        {
            context.Reoffer = false;

            yield return StartCoroutine(RunEquipmentOffer(items, levelClearEquipmentSkips, context, RewardTitle(context, "equipment")));

            // No Equip() call here, unlike the mid-battle pickup path - `hero` will not exist next
            // level, only its PartyMember record does. See RecordRunCard's identical asymmetry.
            if (panel.ChosenEquipment != null && BattleManager.Instance != null)
            {
                BattleManager.Instance.RecordRunEquipment(hero, panel.ChosenEquipment);
            }
        }

        if (BattleManager.Instance != null) { BattleManager.Instance.SetInputLocked(false); }
    }

    /// "Knight's equipment", or "Knight's reward" for a mid-battle pickup (`kind` null). Kept short: at
    /// the title's 96pt, "Knight's equipment reward" already runs into the turn counter.
    private static string RewardTitle(RewardContext context, string kind)
    {
        if (context.character == null) { return null; }

        return $"{context.character.DisplayName}'s {kind ?? "reward"}";
    }

    /// <summary>
    /// Shows the panel and waits for a resolution, re-showing the same candidates if the resolution was
    /// a skip that backed out of itself (RewardContext.Reoffer) rather than one that actually resolved
    /// anything. Leaves panel.ChosenCard/ChosenSkip for the caller to read afterward - a card choice
    /// needs no per-occasion branching in here, only what it is granted into differs, and that is the
    /// caller's business (AddCardToHand for a pickup, nothing but the run record for a level clear).
    /// </summary>
    private IEnumerator RunOffer(List<CardData> candidates, List<SkipReward> skips, RewardContext context, string title)
    {
        do
        {
            context.Reoffer = false;

            panel.Show(candidates, skips, title, context.character);

            yield return new WaitUntil(() => panel.Resolved);

            if (panel.ChosenSkip != null) { yield return panel.ChosenSkip.Grant(context); }
        }
        while (context.Reoffer);
    }

    /// <summary>
    /// Shows the equipment panel and waits for a resolution - RunOffer's equipment counterpart, same
    /// reoffer-on-cancelled-skip loop. Kept as a separate method rather than a generic over
    /// Show/ShowEquipment: the two candidate types (CardData/EquipmentData) share no useful base beyond
    /// object, and the loop itself is twelve lines - not worth a delegate parameter to deduplicate.
    /// </summary>
    private IEnumerator RunEquipmentOffer(List<EquipmentData> candidates, List<SkipReward> skips, RewardContext context, string title)
    {
        do
        {
            context.Reoffer = false;

            panel.ShowEquipment(candidates, context.character, skips, title);

            yield return new WaitUntil(() => panel.Resolved);

            if (panel.ChosenSkip != null) { yield return panel.ChosenSkip.Grant(context); }
        }
        while (context.Reoffer);
    }

    /// <summary>
    /// `table.EquipmentChoiceCount` distinct items `picker` may hold and does not already, drawn
    /// uniformly from the whole pool - the equipment counterpart to BuildOffer. No tier: every item is
    /// Legendary (see EquipmentLibrary.Offerable), so the roll that led here only decided that equipment
    /// was offered at all. No guarantees or tag weighting either: those are authored on LootTable for
    /// the card pool specifically. An item already equipped is left out, since taking a second copy of
    /// it would be a dead choice. Returns however many it actually found rather than padding - Drain
    /// treats an empty result as "offer cards instead", OfferLevelClear as "skip the equipment panel".
    /// </summary>
    public List<EquipmentData> BuildEquipmentOffer(Character picker, LootTable table)
    {
        List<EquipmentData> found = new();

        if (equipmentLibrary == null || table == null) { return found; }

        int count = table.EquipmentChoiceCount;

        foreach (EquipmentData item in equipmentLibrary.Offerable(picker))
        {
            if (picker != null && Contains(picker.Equipment, item)) { continue; }
            if (!found.Contains(item)) { found.Add(item); }
        }

        Shuffle(found);

        if (found.Count > count) { found.RemoveRange(count, found.Count - count); }

        return found;
    }

    private static bool Contains(IReadOnlyList<EquipmentData> items, EquipmentData item)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == item) { return true; }
        }

        return false;
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

            // Listed twice means offered twice - LootTable.guaranteedCards' own tooltip already promises
            // "each one takes a slot out of choiceCount", and a table that wants three of the same card
            // is the whole reason to say so. Deliberately not gated on seen.Add: that would silently
            // collapse the repeat into one slot and quietly fill the rest at random.
            //
            // Still recorded in `seen`, so the random fill below cannot offer a fourth copy of something
            // already guaranteed.
            seen.Add(card);
            seeded.Add(card);
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
                index = GameDice.Range(0, remaining.Count);
            }
            else
            {
                int roll = GameDice.Range(0, totalWeight);
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
            int j = GameDice.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
