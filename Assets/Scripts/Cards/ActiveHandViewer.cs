using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Everything about cards on screen: which hand is shown, the viewers for it, where they sit along
/// the spline, and how a hovered card raises and enlarges itself.
///
/// This was four classes - HandViewer, GameManager's display half, CreateCardViewer and
/// CardHoverManager. The last two had one and two public methods; a manager that small is not a
/// concept, it is a function that went looking for a file. They all answer the same question - what
/// do the active character's cards look like right now - so they are one thing.
///
/// It follows a character rather than being told what to draw: ShowHandFor re-points the CardDrawn
/// subscription, so a card drawn by whoever is active appears immediately and one drawn by anybody
/// else waits in their hand until you switch to them. Drawing stays the character's business - see
/// Character.DrawCard, which knows nothing about any of this.
/// </summary>
public class ActiveHandViewer : Singleton<ActiveHandViewer>
{
    [SerializeField] private SplineContainer splineContainer;

    [SerializeField] private float selectRaise = 0.75f;

    [SerializeField] private float hoverRaise = 0.6f;

    [Tooltip("How far a hovered card is pushed toward the camera - must clear every other hand " +
        "card's resting depth (see the per-index nudge below) so it always wins render order and clicks, " +
        "but stay well inside the camera's near clip plane (0.3 in this scene, sitting ~0.7 in front of " +
        "the hand at Z 0) or the card is pushed out of view and stops registering the mouse at all. " +
        "Clamped in code below - a mistyped Inspector value here once cost a lot of debugging.")]
    [SerializeField] private float hoverPush = 0.2f;

    [Tooltip("Hard ceiling on hoverPush regardless of what's set above - keeps a mistyped Inspector " +
        "value from ever pushing a card past the camera's near clip plane again.")]
    [SerializeField] private float maxHoverPush = 0.5f;

    [Tooltip("Prefab every card in hand is built from.")]
    [SerializeField] private CardViewer cardPrefab;

    [SerializeField] private float layoutDuration = 0.15f;

    [Tooltip("Where a discarded card's viewer flies to before it is destroyed.")]
    [SerializeField] private Transform discardAnchor;

    [SerializeField] private float discardDuration = 0.15f;

    [Tooltip("Where a drawn card's viewer grows out of, instead of the hand row's own root - the Deck "
        + "icon's transform. Left null, a drawn card spawns exactly where it used to: at this "
        + "component's own position.")]
    [SerializeField] private Transform drawAnchor;

    [Tooltip("Seconds between cards being released from the deal queue - what turns DrawCards' "
        + "one-frame burst into cards visibly arriving one at a time. Independent of layoutDuration: "
        + "a card's own grow-and-slide tween can outlast the gap before the next one starts.")]
    [SerializeField] private float dealInterval = 0.06f;

    private readonly List<CardViewer> cardsInHand = new();

    /// Whose hand is on screen. Held so the CardDrawn subscription can be moved off it on a switch.
    private Character shown;

    /// <summary>
    /// One step of dealing: either one card arriving from the deck, or a reshuffle flourish that has
    /// to finish before the next card is released. A struct rather than two separate queues so the
    /// order between "this reshuffle" and "the card it just unblocked" - PilesReshuffled always fires
    /// before the CardDrawn it enabled, from inside Character.DrawCard - is preserved by construction:
    /// draining one queue in order can never interleave the two differently than they actually happened.
    /// </summary>
    private struct DealStep
    {
        public Card card;
        public int reshuffleCount;
        public bool isReshuffle;
    }

    private readonly Queue<DealStep> dealQueue = new();

    private bool dealQueueRunning;

    /// How many discard flights are currently in the air - see DiscardCardViewer. Paired with
    /// dealQueueRunning in Busy so BattleManager's End Turn wait covers both directions at once.
    private int discardsInFlight;

    /// True while a card is still being dealt out of the deck or flown to the discard pile.
    /// BattleManager waits on this after discarding the hand at End Turn, the same way it already
    /// waits on LootManager.IsIdle - the round must not roll over on top of the animation.
    public bool Busy => dealQueueRunning || discardsInFlight > 0;

    public bool Contains(CardViewer cardViewer) => cardsInHand.Contains(cardViewer);

    /// <summary>
    /// The on-screen viewer showing this card, or null if it is not in the hand on screen - it belongs
    /// to a character who is not active, or it has already been played.
    ///
    /// Exposed for the tutorial, which has to point a spotlight and a popup at one specific card and
    /// only knows the Card. Read-only in the same spirit as Contains: a caller may find a viewer, never
    /// add or remove one.
    /// </summary>
    public CardViewer ViewerFor(Card card)
    {
        if (card == null) { return null; }

        return cardsInHand.Find(cv => cv != null && cv.card == card);
    }

    /// <summary>
    /// Follows the battle rather than being told what to draw. Subscribing *and* reading the current
    /// active character covers both Start orderings: subscribe first and the event arrives, or
    /// subscribe late and ActiveCharacter is already set.
    /// </summary>
    private void Start()
    {
        if (cardPrefab == null) { Debug.LogError($"{name}: cardPrefab is not set - no card can be built"); }
        if (splineContainer == null) { Debug.LogError($"{name}: splineContainer is not set - cards cannot be laid out"); }

        BattleManager battle = BattleManager.Instance;

        if (battle == null)
        {
            Debug.LogError($"{name}: no BattleManager in the scene - the hand cannot follow anybody");
            return;
        }

        battle.ActiveCharacterChanged += ShowHandFor;
        battle.PlayabilityChanged += RefreshPlayability;

        if (battle.ActiveCharacter != null) { ShowHandFor(battle.ActiveCharacter); }
    }

    /// <summary>
    /// Re-colours the row: green frame on what can be played right now, greyed out on what cannot.
    ///
    /// Asks Card.PlayRefusal, the same call CardPlayManager.PlaySelectedOn gates the click on, so a
    /// green card can never turn out to refuse - the same guarantee GridManager.ShowPlayableTiles gets
    /// from sharing Card.Refusal with the click.
    ///
    /// Driven by BattleManager.PlayabilityChanged rather than by this class watching for energy: five
    /// different things move the answer (energy, cooldown, Frozen, the hand, who is active) and the
    /// battle already knows about all of them.
    /// </summary>
    private void RefreshPlayability()
    {
        foreach (CardViewer cardViewer in cardsInHand)
        {
            if (cardViewer == null || cardViewer.card == null) { continue; }

            cardViewer.SetPlayable(cardViewer.card.PlayRefusal(shown) == null);
            cardViewer.RefreshLockCounter();
        }
    }

    /// <summary>
    /// Swaps the row over to this character's cards. Safe to call with the same character twice - it
    /// rebuilds from their hand either way, which is also how a hand that changed off-screen catches
    /// up when you switch back to it.
    /// </summary>
    public void ShowHandFor(Character character)
    {
        if (shown != null)
        {
            shown.CardDrawn -= OnCardDrawn;
            shown.CardDiscarded -= OnCardDiscarded;
            shown.PilesReshuffled -= OnPilesReshuffled;
        }

        shown = character;

        if (shown != null)
        {
            shown.CardDrawn += OnCardDrawn;
            shown.CardDiscarded += OnCardDiscarded;
            shown.PilesReshuffled += OnPilesReshuffled;
        }

        ClearHand();

        // Whatever was mid-flight belonged to whoever was shown before this call - draining it now
        // would add someone else's cards into the row that just replaced theirs.
        dealQueue.Clear();

        if (shown == null) { return; }

        // Instant, not dealt: rebuilding the row on a character switch is not a draw, it is catching
        // up to a hand that already exists - see AddToHand's `instant` parameter.
        foreach (Card card in shown.Hand) { AddToHand(card, instant: true); }
    }

    public IEnumerator AddCard(CardViewer cardViewer)
    {
        cardsInHand.Add(cardViewer);
        yield return UpdateCardPosition(layoutDuration);
    }

    /// Empties the hand and destroys the viewers. The Card objects behind them are owned by the
    /// character, so nothing is lost - switching characters just rebuilds the row from the new hand.
    public void ClearHand()
    {
        foreach (CardViewer cardViewer in cardsInHand)
        {
            if (cardViewer != null) { Destroy(cardViewer.gameObject); }
        }

        cardsInHand.Clear();
    }

    public IEnumerator RemoveCard(CardViewer cardViewer)
    {
        cardsInHand.Remove(cardViewer);
        yield return UpdateCardPosition(layoutDuration);
    }

    /// Re-runs the layout without changing the hand - used whenever a card's selected or hovered
    /// state changes.
    public IEnumerator Relayout()
    {
        yield return UpdateCardPosition(layoutDuration);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (shown != null)
        {
            shown.CardDrawn -= OnCardDrawn;
            shown.CardDiscarded -= OnCardDiscarded;
            shown.PilesReshuffled -= OnPilesReshuffled;
        }

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.ActiveCharacterChanged -= ShowHandFor;
            BattleManager.Instance.PlayabilityChanged -= RefreshPlayability;
        }
    }

    /// Only ever fires for `shown` - ShowHandFor moves the subscription rather than filtering here.
    /// Enqueued rather than added straight away: DrawCards fires this once per card in a single frame,
    /// and the queue is what turns that burst into cards visibly arriving one at a time.
    private void OnCardDrawn(Character character, Card card)
    {
        dealQueue.Enqueue(new DealStep { card = card });
        EnsureDealQueueRunning();
    }

    /// Only ever fires for `shown`, same as OnCardDrawn. Covers both a played card and a whole hand
    /// discarded at turn start - Character raises the same event either way, so there is only one
    /// place that removes a viewer.
    private void OnCardDiscarded(Character character, Card card)
    {
        CardViewer cardViewer = cardsInHand.Find(cv => cv.card == card);
        if (cardViewer == null) { return; }

        StartCoroutine(DiscardCardViewer(cardViewer));
    }

    /// <summary>
    /// Only ever fires for `shown`. Raised from inside Character.DrawCard *before* the card that
    /// triggered the reshuffle is taken off the pile, so this always reaches the queue ahead of the
    /// CardDrawn it enabled - queuing it here, rather than playing it immediately, is what lets the
    /// flourish land in between the cards dealt before it and the ones it just made possible instead
    /// of stepping on whichever card is currently flying in.
    /// </summary>
    private void OnPilesReshuffled(Character character, int count)
    {
        dealQueue.Enqueue(new DealStep { isReshuffle = true, reshuffleCount = count });
        EnsureDealQueueRunning();
    }

    private void EnsureDealQueueRunning()
    {
        if (dealQueueRunning) { return; }

        dealQueueRunning = true;
        StartCoroutine(DrainDealQueue());
    }

    /// Releases one step at a time - a card dealt from the deck, or a reshuffle flourish that blocks
    /// the rest of the queue until it finishes. See DealStep and OnPilesReshuffled for why order here
    /// is never rearranged relative to how Character raised the events.
    private IEnumerator DrainDealQueue()
    {
        while (dealQueue.Count > 0)
        {
            DealStep step = dealQueue.Dequeue();

            if (step.isReshuffle)
            {
                if (CardPileHud.Instance != null) { yield return CardPileHud.Instance.PlayReshuffle(step.reshuffleCount); }
                continue;
            }

            AddToHand(step.card, instant: false);
            yield return new WaitForSeconds(dealInterval);
        }

        dealQueueRunning = false;
    }

    private IEnumerator DiscardCardViewer(CardViewer cardViewer)
    {
        discardsInFlight++;

        try
        {
            cardViewer.BeginPlay();

            // An Innate card never enters the discard pile (Character.Discard) - it just leaves hand
            // and comes straight back next turn via RestoreInnateCards, so flying it to the discard
            // icon would show it going somewhere it never actually goes. It keeps the old shrink-in-
            // place instead.
            bool innate = cardViewer.card != null && cardViewer.card.HasKeyword(CardKeywordType.Innate);
            Vector3 target = !innate && discardAnchor != null ? discardAnchor.position : cardViewer.transform.position;
            cardViewer.PlayDiscard(target, discardDuration);

            yield return RemoveCard(cardViewer);

            // Null-checked with Unity's overloaded == rather than assumed alive: ClearHand can destroy
            // this same viewer out from under a still-running flight (a character switch mid-discard),
            // and the finally below must still run either way.
            if (cardViewer != null) { Destroy(cardViewer.gameObject); }
        }
        finally
        {
            discardsInFlight--;
        }
    }

    /// <summary>
    /// Builds one card's viewer and starts it flying into the hand layout.
    ///
    /// `instant` is true only from ShowHandFor's rebuild: a character switch is not a draw, so the
    /// card appears at this component's own position (the hand row's root) rather than growing out of
    /// the deck icon - see drawAnchor. A queued deal (instant: false) spawns at drawAnchor instead, so
    /// it visibly comes from the pile the player just watched shrink.
    /// </summary>
    private void AddToHand(Card card, bool instant)
    {
        // Bail rather than throw. This runs off Character.CardDrawn (via the deal queue), which is
        // raised from inside DrawCard - so an exception here does not just skip one card, it unwinds
        // through DrawCards and TurnStart and kills the whole battle coroutine. A missing prefab
        // should cost you the card art, not the game. Start has already logged what is missing.
        if (cardPrefab == null) { return; }

        Vector3 spawnPosition = !instant && drawAnchor != null ? drawAnchor.position : transform.position;

        CardViewer cardViewer = Instantiate(cardPrefab, spawnPosition, Quaternion.identity);
        cardViewer.Setup(card);

        // Before it is ever drawn, not on the next PlayabilityChanged. A card dealt onto an empty
        // energy pool would otherwise spend a frame or more looking playable while it grows in - and
        // ShowHandFor rebuilds the whole row on a character switch, which raises nothing by itself.
        cardViewer.SetPlayable(card.PlayRefusal(shown) == null);
        cardViewer.RefreshLockCounter();

        cardViewer.PlaySpawnIn(layoutDuration);

        StartCoroutine(AddCard(cardViewer));
    }

    private IEnumerator UpdateCardPosition(float duration)
    {
        if (cardsInHand.Count == 0) { yield break; }

        float cardSpacing = 1.5f / 10f;
        float firstPosition = 0.5f - (cardsInHand.Count - 1) * cardSpacing / 2;
        Spline spline = splineContainer.Spline;
        for (int i = 0; i < cardsInHand.Count; i++)
        {
            float p = firstPosition + i * cardSpacing;
            Vector3 splinePosition = spline.EvaluatePosition(p);
            Vector3 forward = spline.EvaluateTangent(p);
            Vector3 up = spline.EvaluateUpVector(p);
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(forward, up).normalized, up);
            // The raise has to be applied here rather than tweened separately, or any AddCard/RemoveCard
            // during a selection would pull the selected card back down. Same reasoning covers hover -
            // and the two offsets never both apply, since SetSelected clears isHovered.
            Vector3 selectOffset = cardsInHand[i].isSelected ? Vector3.up * selectRaise : Vector3.zero;
            float clampedHoverPush = Mathf.Min(hoverPush, maxHoverPush);
            Vector3 hoverOffset = cardsInHand[i].isHovered ? Vector3.up * hoverRaise + Vector3.back * clampedHoverPush : Vector3.zero;
            Vector3 targetPosition = splinePosition + transform.position + .01f * i * Vector3.back + selectOffset + hoverOffset;
            cardsInHand[i].SetLayoutTarget(targetPosition, rotation, duration);

            // Paired with the z nudge above rather than replacing it. Sorting decides what you see;
            // z still decides what a click hits, because OnMouseDown picks the nearest collider. Both
            // rise with i so the card that draws on top is the one that takes the click.
            cardsInHand[i].SetHandOrder(i);
        }
        yield return new WaitForSeconds(duration);
    }
}
