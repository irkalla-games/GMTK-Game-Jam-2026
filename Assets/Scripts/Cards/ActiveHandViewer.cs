using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;
using DG.Tweening;

/// <summary>
/// Everything about cards on screen: which hand is shown, the viewers for it, where they sit along
/// the spline, and the enlarged preview on hover.
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

    [Tooltip("Prefab every card in hand is built from.")]
    [SerializeField] private CardViewer cardPrefab;

    [Tooltip("The enlarged copy shown while hovering a card. Always in the scene, just hidden.")]
    [SerializeField] private CardViewer largeCardViewer;

    [SerializeField] private float layoutDuration = 0.15f;

    [Tooltip("Where a discarded card's viewer flies to before it is destroyed.")]
    [SerializeField] private Transform discardAnchor;

    [SerializeField] private float discardDuration = 0.15f;

    private readonly List<CardViewer> cardsInHand = new();

    /// Whose hand is on screen. Held so the CardDrawn subscription can be moved off it on a switch.
    private Character shown;

    public bool Contains(CardViewer cardViewer) => cardsInHand.Contains(cardViewer);

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

        if (battle.ActiveCharacter != null) { ShowHandFor(battle.ActiveCharacter); }
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
        }

        shown = character;

        if (shown != null)
        {
            shown.CardDrawn += OnCardDrawn;
            shown.CardDiscarded += OnCardDiscarded;
        }

        ClearHand();

        if (shown == null) { return; }

        foreach (Card card in shown.Hand) { AddToHand(card); }
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

    /// Re-runs the layout without changing the hand - used when a card's selected state changes.
    public IEnumerator Relayout()
    {
        yield return UpdateCardPosition(layoutDuration);
    }

    public void ShowLargeCard(Card card, Vector3 position)
    {
        largeCardViewer.gameObject.SetActive(true);
        largeCardViewer.Setup(card);
        largeCardViewer.transform.position = position;
    }

    public void HideLargeCard()
    {
        largeCardViewer.gameObject.SetActive(false);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (shown != null)
        {
            shown.CardDrawn -= OnCardDrawn;
            shown.CardDiscarded -= OnCardDiscarded;
        }

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.ActiveCharacterChanged -= ShowHandFor;
        }
    }

    /// Only ever fires for `shown` - ShowHandFor moves the subscription rather than filtering here.
    private void OnCardDrawn(Character character, Card card)
    {
        AddToHand(card);
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

    private IEnumerator DiscardCardViewer(CardViewer cardViewer)
    {
        cardViewer.BeginPlay();

        Vector3 target = discardAnchor != null ? discardAnchor.position : cardViewer.transform.position;
        cardViewer.transform.DOMove(target, discardDuration);
        cardViewer.transform.DOScale(Vector3.zero, discardDuration);

        yield return RemoveCard(cardViewer);
        Destroy(cardViewer.gameObject);
    }

    private void AddToHand(Card card)
    {
        // Bail rather than throw. This runs off Character.CardDrawn, which is raised from inside
        // DrawCard - so an exception here does not just skip one card, it unwinds through DrawCards
        // and TurnStart and kills the whole battle coroutine. A missing prefab should cost you the
        // card art, not the game. Start has already logged what is missing.
        if (cardPrefab == null) { return; }

        CardViewer cardViewer = Instantiate(cardPrefab, transform.position, Quaternion.identity);
        cardViewer.Setup(card);
        cardViewer.transform.localScale = Vector3.zero;
        cardViewer.transform.DOScale(Vector3.one, layoutDuration);

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
            // during a selection would pull the selected card back down.
            Vector3 selectOffset = cardsInHand[i].isSelected ? Vector3.up * selectRaise : Vector3.zero;
            cardsInHand[i].transform.DOMove(splinePosition + transform.position + .01f * i * Vector3.back + selectOffset, duration);
            cardsInHand[i].transform.DORotate(rotation.eulerAngles, duration);
        }
        yield return new WaitForSeconds(duration);
    }
}
