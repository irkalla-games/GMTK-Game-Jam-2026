using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The scripted first level: dims everything but the one thing to click, says what it is, and waits.
///
/// **One coroutine, one line per beat.** The sequence lives in Run() below, in reading order, built from
/// primitives that each take their copy as the first two arguments so a step cannot be written without
/// it. The primitives are named for *interactions* - SelectCard, AimCard, Activate, EndTurn, Select,
/// Hover, Read - and never for mechanics, which is what keeps this extensible: teaching a new card is
/// two lines here and no new primitive, because "click a card, click a tile" is already one of the seven
/// ways a player can touch this game.
///
/// **Each primitive makes one declaration and derives three things from it** - the spotlight's holes,
/// TutorialGate's permit, and the popup's anchor. Never three separate statements. This is the rule
/// GridManager.ShowPlayableTiles already relies on, where the tile highlight is built from the very same
/// Card.Refusal call the click asks - which is what stops the highlight from ever promising a tile that
/// a click would then refuse.
///
/// Waits are WaitUntil, never WaitForSeconds - the reason BattleManager.WaitForAcknowledgement gives
/// (a modal can zero Time.timeScale, and a scaled wait behind one never elapses). Every wait also drops
/// out on Skip, so the sequence unwinds from wherever it had got to.
///
/// Copy lives in consts rather than serialized fields, for the reason BattleManager's own tutorial text
/// already documents: a field added to a component the scene already holds deserializes to empty, not to
/// its initialiser, so the text would silently vanish until someone retyped it in the Inspector.
/// </summary>
public class TutorialDirector : Singleton<TutorialDirector>
{
    [Header("Cards the script names")]
    [Tooltip("Must match the CardData in the tutorial decks - the director finds the live Card in a "
             + "hand by comparing against these.")]
    [SerializeField] private CardData fireball;

    [SerializeField] private CardData shield;

    [SerializeField] private CardData slash;

    [SerializeField] private CardData teleport;

    [SerializeField] private CardData sapTotem;

    [Header("Scene anchors (wired by Tools > Tutorial > Wire Tutorial Overlay)")]
    [Tooltip("The End Turn button's rect.")]
    [SerializeField] private RectTransform endTurnAnchor;

    [Tooltip("The discard pile counter, lit while explaining that the hand clears each turn.")]
    [SerializeField] private RectTransform discardPileAnchor;

    [Tooltip("The row of hero portraits. Which portrait is whose is asked live - the row is rebuilt "
             + "whenever the roster changes.")]
    [SerializeField] private PartyPortraitPanel portraitPanel;

    [Tooltip("The enemy-side SelectedCharacterPanel - the one whose Audience is NotPlayerControlled. "
             + "Its status row is lit for the 'hover a status' beat.")]
    [SerializeField] private SelectedCharacterPanel enemyPanel;

    /// What the player may touch right now. Owned here, asked by the input doors through the statics
    /// below - see TutorialGate.
    private readonly TutorialGate gate = new();

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Held between the player pressing End Turn and the enemy actually acting, so the two beats that
    /// explain what is about to happen land in that gap. BattleManager.RunBattle waits on this right
    /// before EnemyResolve - the same shape as the LootIdle and ActiveHandViewer.Busy waits already
    /// either side of it.
    /// </summary>
    public bool HoldingRound { get; private set; }

    // ---- The doors ------------------------------------------------------------------------------
    // Null whenever no tutorial is driving, so a call site is one `?? ` away from its normal behaviour
    // and the null-checking lives here rather than at each of the four.

    public static string RefuseCard(Card card) => Active()?.gate.CardRefusal(card);

    public static string RefuseTile(GridTile tile) => Active()?.gate.TileRefusal(tile);

    public static string RefuseActivate(Character hero) => Active()?.gate.ActivateRefusal(hero);

    public static string RefuseEndTurn() => Active()?.gate.EndTurnRefusal();

    /// <summary>
    /// The `?.` on the four callers above is safe, unlike `?.` on a UnityEngine.Object generally: this
    /// returns the *literal* null from the ternary, and the aliveness test is `Instance != null`, which
    /// is Unity's overloaded == and does see a destroyed singleton. Returning `Instance` unconditionally
    /// and letting `?.` do the checking is what would be broken - see TutorialWiring.Ensure.
    /// </summary>
    private static TutorialDirector Active() =>
        Instance != null && Instance.IsRunning ? Instance : null;

    // ---- Copy -----------------------------------------------------------------------------------

    private const string WelcomeTitle = "Survive the Countdown";
    private const string WelcomeBody =
        "Your party is holding out until the door opens. Last until the turn counter reaches 0 and you "
        + "make it out alive.";

    private const string PlayCardTitle = "Play a Card";
    private const string PlayCardBody =
        "To use a card, select a card from your hand using left click.";

    private const string AimFireballTitle = "Pick a Target";
    private const string AimFireballBody =
        "The highlighted tiles are the ones your selected card can reach. Left click the Ranger to hit "
        + "it with Fireball.";

    private const string SwitchHeroTitle = "Switch Hero";
    private const string SwitchHeroBody =
        "Each hero has their own deck and their own energy. Press 1 to take control of the Knight.";

    private const string ShieldTitle = "Defend Yourself";
    private const string ShieldBody =
        "Shield soaks up damage before it reaches your health. Select Shield with left click.";

    private const string ShieldAimTitle = "Cast It on Yourself";
    private const string ShieldAimBody =
        "Some cards target you rather than an enemy. Left click the Knight to give him the shield.";

    private const string EndTurnTitle = "End Your Turn";
    private const string EndTurnBody =
        "You have spent your energy. Click End Turn to let the enemy act.";

    private const string DiscardTitle = "Your Hand Resets";
    private const string DiscardBody =
        "Anything you did not play is discarded when your turn ends, and you draw a fresh hand next turn.";

    private const string IntentTitle = "Read the Intent";
    private const string IntentBody =
        "The icon above an enemy shows what it is about to do. The Ranger is calling for reinforcements.";

    private const string SlashTitle = "Finish It";
    private const string SlashBody =
        "The skeleton only has 1 health. Select Slash with left click.";

    private const string SlashAimTitle = "Cut It Down";
    private const string SlashAimBody = "Left click the skeleton to attack it.";

    private const string LootTitle = "Enemies Drop Loot";
    private const string LootBody =
        "Beaten enemies leave something behind. Step a hero onto it to claim it.";

    private const string BackToMageTitle = "Back to the Mage";
    private const string BackToMageBody = "Press 2 to take control of the Mage.";

    private const string TeleportTitle = "Move Across the Board";
    private const string TeleportBody =
        "Teleport puts you on any free tile in range. Select it with left click.";

    private const string TeleportAimTitle = "Claim the Drop";
    private const string TeleportAimBody =
        "Left click the tile with the drop on it to teleport onto the loot.";

    private const string TotemTitle = "Place a Totem";
    private const string TotemBody =
        "A totem stays on the board and affects anyone standing near it. Select Sap Totem.";

    private const string TotemAimTitle = "Put It Next to the Ranger";
    private const string TotemAimBody =
        "Left click a free tile next to the Ranger so its aura reaches him.";

    private const string InspectTitle = "Inspect an Enemy";
    private const string InspectBody =
        "Left click any character to see their health and what is affecting them.";

    private const string StatusTitle = "Read a Status";
    private const string StatusBody =
        "Hover a status icon with your mouse to read exactly what it is doing.";

    private const string SapTitle = "Sap Weakens Attacks";
    private const string SapBody =
        "The Sap totem is cutting the damage off the Ranger's arrows for as long as he stands near it. "
        + "Survive one more turn.";

    private const string RideOutTitle = "Ride It Out";
    private const string RideOutBody =
        "Click End Turn. The counter reaches 0 and the level is yours.";

    // ---- Lifecycle ------------------------------------------------------------------------------

    /// <summary>
    /// Starts the script. Called from BattleManager.Start, after the party and enemies are on the board
    /// and the opening active character has been chosen, but before RunBattle - so the first thing the
    /// player ever sees is already dimmed.
    ///
    /// Returns false when it could not start, so the caller can fall back to the old single modal rather
    /// than leaving a first-time player with no explanation at all.
    /// </summary>
    public bool Begin()
    {
        if (IsRunning) { return true; }

        if (TutorialPopup.Instance == null || TutorialSpotlight.Instance == null)
        {
            Debug.LogError($"{name}: no TutorialPopup or TutorialSpotlight in the scene - run "
                           + "Tools > Tutorial > 2 - Wire Tutorial Overlay. Falling back to the old "
                           + "How to Play prompt.");
            return false;
        }

        IsRunning = true;
        gate.PermitNothing();
        TutorialPopup.Instance.BeginTutorial();

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced += OnTurnAdvanced; }

        StartCoroutine(Run());

        return true;
    }

    /// <summary>
    /// Tears the tutorial down, whether it finished or was skipped. Everything the director holds over
    /// the game is released here in one place, so there is no path that leaves the board dimmed or the
    /// gate refusing clicks - which would be an unrecoverable lock rather than a cosmetic bug.
    /// </summary>
    private void End()
    {
        IsRunning = false;
        HoldingRound = false;
        gate.PermitNothing();

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= OnTurnAdvanced; }

        if (TutorialSpotlight.Instance != null) { TutorialSpotlight.Instance.Clear(); }
        if (TutorialPopup.Instance != null) { TutorialPopup.Instance.Hide(); }

        // Whether it was walked or skipped, they have seen it. Persisted in PlayerPrefs, so the next
        // Play goes straight to character select.
        GameSettings.TutorialEnabled = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (BattleManager.Instance != null) { BattleManager.Instance.TurnAdvanced -= OnTurnAdvanced; }
    }

    /// <summary>
    /// Squeezes turn 1 down to a single energy, so the opening two beats are each the only thing the
    /// player can afford as well as the only thing they are allowed.
    ///
    /// Through SpendEnergy rather than by authoring maxEnergy down on the tutorial prefabs: the script
    /// needs the normal 3 back on turn 2 for the Mage to Teleport *and* place the totem, and TurnStart's
    /// own ResetEnergy already restores it with no second poke from here. Nothing authored is mutated.
    /// </summary>
    private void OnTurnAdvanced()
    {
        BattleManager battle = BattleManager.Instance;

        if (!IsRunning || battle == null || battle.TurnsElapsed != 1) { return; }

        foreach (Character hero in battle.Characters)
        {
            if (hero == null || hero.IsDead || !hero.IsPlayerControlled) { continue; }

            hero.SpendEnergy(hero.Energy - 1);
        }
    }

    // ---- The sequence ---------------------------------------------------------------------------

    private IEnumerator Run()
    {
        BattleManager battle = BattleManager.Instance;

        // The board is built by BattleManager.Start before Begin is called, so everyone is already
        // placed - but the first hand is dealt by TurnStart, which has not run yet.
        Character knight = PartyMemberAt(0);
        Character mage = PartyMemberAt(1);
        Character ranger = FirstEnemy();

        if (knight == null || mage == null || ranger == null)
        {
            Debug.LogError($"{name}: the tutorial level needs two heroes and an enemy - found "
                           + $"knight={knight}, mage={mage}, ranger={ranger}. Skipping the tutorial.");
            End();
            yield break;
        }

        // The opening hand has to exist before a card can be pointed at, and the phase has to be the
        // player's before a click means anything.
        yield return new WaitUntil(() => battle.Phase == BattlePhase.PlayerActing);

        // Slot 0 is the Knight so that "press 1" means the Knight, but the script opens as the Mage -
        // BattleManager.Start picks the first playable character, which is the other one.
        battle.SetActiveCharacter(mage);

        yield return Read(WelcomeTitle, WelcomeBody);

        yield return SelectCard(PlayCardTitle, PlayCardBody, mage, fireball, OnTile(ranger));
        yield return AimCard(AimFireballTitle, AimFireballBody, mage, fireball, OnTile(ranger));

        yield return Activate(SwitchHeroTitle, SwitchHeroBody, knight);

        yield return SelectCard(ShieldTitle, ShieldBody, knight, shield, OnTile(knight));
        yield return AimCard(ShieldAimTitle, ShieldAimBody, knight, shield, OnTile(knight));

        yield return EndTurn(EndTurnTitle, EndTurnBody, holdRound: true);

        // Both of these land in the gap RunBattle now leaves before EnemyResolve - see HoldingRound.
        yield return Read(DiscardTitle, DiscardBody, UiAnchor(discardPileAnchor, TooltipSide.Above));
        yield return Read(IntentTitle, IntentBody, WorldAnchor(ranger, TooltipSide.Right));

        // Releases the round. The enemy acts, the counter rolls, and the next turn begins.
        int heldOnTurn = battle.TurnsElapsed;

        HoldingRound = false;

        // Both halves matter. The phase is *still* PlayerActing the moment the hold lifts - RunBattle
        // has not reached EnemyResolve yet - so waiting on the phase alone would fall straight through
        // and go looking for a skeleton that has not been summoned. TurnsElapsed only moves in
        // TurnStart, which is on the far side of the enemy's whole turn.
        yield return new WaitUntil(() =>
            Skipped || (battle.TurnsElapsed > heldOnTurn && battle.Phase == BattlePhase.PlayerActing));

        if (Skipped) { End(); yield break; }

        Character skeleton = FirstEnemyOtherThan(ranger);

        if (skeleton == null)
        {
            Debug.LogWarning($"{name}: no reinforcement arrived on turn 2 - check the Ranger's summon "
                             + "card and the tutorial level's board. Skipping to the end of the script.");
        }
        else
        {
            yield return SelectCard(SlashTitle, SlashBody, knight, slash, OnTile(skeleton));
            yield return AimCard(SlashAimTitle, SlashAimBody, knight, slash, OnTile(skeleton));

            yield return Read(LootTitle, LootBody, DropAnchor());
        }

        yield return Activate(BackToMageTitle, BackToMageBody, mage);

        yield return SelectCard(TeleportTitle, TeleportBody, mage, teleport, HasDrop);
        yield return AimCard(TeleportAimTitle, TeleportAimBody, mage, teleport, HasDrop);

        // Picking the card up opens a reward panel, which owns the screen until it is resolved. Nothing
        // the tutorial shows may sit on top of it.
        yield return new WaitUntil(() => Skipped || LootManager.Instance == null || LootManager.Instance.IsIdle);
        if (Skipped) { End(); yield break; }

        yield return SelectCard(TotemTitle, TotemBody, mage, sapTotem, FreeTileBeside(ranger));
        yield return AimCard(TotemAimTitle, TotemAimBody, mage, sapTotem, FreeTileBeside(ranger));

        yield return Select(InspectTitle, InspectBody, ranger);

        yield return Hover(StatusTitle, StatusBody, UiAnchor(StatusRow(), TooltipSide.Left));

        yield return Read(SapTitle, SapBody);

        yield return EndTurn(RideOutTitle, RideOutBody, holdRound: false);

        End();
    }

    // ---- Primitives -----------------------------------------------------------------------------

    /// Nothing on the board to do - Continue is the only way on. `at` null puts the box centre screen.
    private IEnumerator Read(string title, string body, TooltipAnchor? at = null)
    {
        gate.PermitNothing();

        yield return Beat(title, body, at, showContinue: true, holes: Holes(at), done: null);
    }

    /// <summary>
    /// Arms a card. Permits the card *and* its eventual targets, not just the card - see TutorialGate
    /// for why splitting those across two permits would soft-lock a player who deselected mid-aim.
    /// </summary>
    private IEnumerator SelectCard(
        string title, string body, Character actor, CardData data, Predicate<GridTile> targets)
    {
        Card card = InHand(actor, data);

        if (card == null)
        {
            Debug.LogWarning($"{name}: {actor.name} has no {(data != null ? data.name : "card")} in hand "
                             + "- skipping that beat.");
            yield break;
        }

        gate.Permit(allowedCard: card, allowedTiles: Legal(actor, card, targets));

        TooltipAnchor? at = CardAnchor(card);

        yield return Beat(title, body, at, showContinue: false, holes: Holes(at),
            done: () => CardPlayManager.Instance != null && CardPlayManager.Instance.SelectedCard == card);
    }

    /// The second half of a play: same permit, different copy, and it ends when the card actually
    /// leaves the hand. The tile permit is what guarantees it left onto the right tile.
    private IEnumerator AimCard(
        string title, string body, Character actor, CardData data, Predicate<GridTile> targets)
    {
        Card card = InHand(actor, data);

        if (card == null) { yield break; }

        Predicate<GridTile> legal = Legal(actor, card, targets);

        gate.Permit(allowedCard: card, allowedTiles: legal);

        // Anchored to the card, which is where the player is looking - the lit tiles are the board's
        // own in-range highlight, which the spotlight also leaves visible.
        TooltipAnchor? at = CardAnchor(card);

        yield return Beat(title, body, at, showContinue: false, holes: TargetHoles(at, legal),
            done: () => !HandHolds(actor, card));
    }

    /// <summary>
    /// The step's own intent, narrowed to what the card would actually accept.
    ///
    /// Without this the tutorial could light - and permit - a tile the card then refuses on range or
    /// occupancy, and the player would click a lit tile, watch it shake, and have no way forward. That
    /// is precisely the failure GridManager.ShowPlayableTiles avoids by building its highlight from the
    /// same Card.Refusal the click asks, and the tutorial has to honour the same rule: what is lit is
    /// what is clickable, always.
    /// </summary>
    private static Predicate<GridTile> Legal(Character actor, Card card, Predicate<GridTile> targets)
    {
        return tile => tile != null
                    && (targets == null || targets(tile))
                    && card.Refusal(actor, tile) == null;
    }

    private IEnumerator Activate(string title, string body, Character hero)
    {
        gate.Permit(allowedActivate: hero);

        TooltipAnchor? at = UiAnchor(
            portraitPanel != null ? portraitPanel.PortraitRectFor(hero) : null, TooltipSide.Above);

        yield return Beat(title, body, at, showContinue: false, holes: Holes(at),
            done: () => BattleManager.Instance != null && BattleManager.Instance.ActiveCharacter == hero);
    }

    private IEnumerator EndTurn(string title, string body, bool holdRound)
    {
        gate.Permit(allowEndTurn: true);

        // Set before the click, not after it, so there is no frame in which RunBattle could reach its
        // wait and sail past a hold that had not been raised yet.
        HoldingRound = holdRound;

        TooltipAnchor? at = UiAnchor(endTurnAnchor, TooltipSide.Left);

        // EndTurnRequested, not a phase change: RunBattle stays in PlayerActing until EnemyResolve
        // actually starts, and when holdRound is true this very step is what is stopping it getting
        // there - so waiting on the phase would deadlock against its own hold.
        yield return Beat(title, body, at, showContinue: false, holes: Holes(at),
            done: () => BattleManager.Instance != null
                     && (BattleManager.Instance.EndTurnRequested
                      || BattleManager.Instance.Phase != BattlePhase.PlayerActing));
    }

    /// Clicking a character to read it, rather than to play anything at it.
    private IEnumerator Select(string title, string body, Character who)
    {
        gate.Permit(allowedTiles: OnTile(who));

        TooltipAnchor? at = WorldAnchor(who, TooltipSide.Right);

        yield return Beat(title, body, at, showContinue: false, holes: Holes(at),
            done: () => BattleManager.Instance != null && BattleManager.Instance.SelectedCharacter == who);
    }

    /// <summary>
    /// Waits for the cursor to rest on something rather than for a click. Continue is offered as well,
    /// so a player who cannot find the hover is never stuck on it - the one beat where the lesson is
    /// worth teaching but not worth trapping anyone over.
    /// </summary>
    private IEnumerator Hover(string title, string body, TooltipAnchor? at)
    {
        gate.PermitNothing();

        yield return Beat(title, body, at, showContinue: true, holes: Holes(at),
            done: () => TooltipManager.Instance != null && TooltipManager.Instance.IsShowing);
    }

    /// <summary>
    /// The one place a step actually runs: light the holes, put the box up, and wait for whichever of
    /// `done`, Continue or Skip arrives first.
    ///
    /// Every exit path goes through here, which is what makes "the tutorial can always be escaped" true
    /// by construction rather than by remembering to check Skip in twenty places.
    /// </summary>
    private IEnumerator Beat(
        string title, string body, TooltipAnchor? at, bool showContinue, TooltipAnchor[] holes, Func<bool> done)
    {
        TutorialSpotlight.Instance.Show(holes);
        TutorialPopup.Instance.Show(title, body, at, showContinue);

        yield return new WaitUntil(() =>
            Skipped
            || (done != null && done())
            || (showContinue && TutorialPopup.Instance.ConsumeContinue()));

        TutorialPopup.Instance.Hide();
        TutorialSpotlight.Instance.Clear();
    }

    private bool Skipped => TutorialPopup.Instance != null && TutorialPopup.Instance.SkipRequested;

    // ---- Anchors and predicates -------------------------------------------------------------------

    private static TooltipAnchor[] Holes(TooltipAnchor? at) =>
        at.HasValue ? new[] { at.Value } : Array.Empty<TooltipAnchor>();

    /// The card plus every tile it may legally be aimed at, so the board's own in-range highlight stays
    /// readable through the dim instead of being blacked out with everything else.
    private static TooltipAnchor[] TargetHoles(TooltipAnchor? card, Predicate<GridTile> targets)
    {
        List<TooltipAnchor> holes = new();

        if (card.HasValue) { holes.Add(card.Value); }

        if (GridManager.Instance != null && targets != null)
        {
            foreach (GridTile tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || !targets(tile)) { continue; }

                Collider2D collider = tile.GetComponent<Collider2D>();

                if (collider != null) { holes.Add(TooltipAnchor.Of(collider)); }
            }
        }

        return holes.ToArray();
    }

    private static TooltipAnchor? CardAnchor(Card card)
    {
        ActiveHandViewer hand = ActiveHandViewer.Instance;

        if (hand == null) { return null; }

        CardViewer viewer = hand.ViewerFor(card);

        if (viewer == null || viewer.Hitbox == null) { return null; }

        return TooltipAnchor.Of(viewer.Hitbox, TooltipSide.Above);
    }

    /// Characters have no colliders of their own - the tile under one is what you click, so the tile is
    /// what gets lit.
    private static TooltipAnchor? WorldAnchor(Character who, TooltipSide side)
    {
        if (who == null || who.Tile == null) { return null; }

        Collider2D collider = who.Tile.GetComponent<Collider2D>();

        return collider != null ? TooltipAnchor.Of(collider, side) : null;
    }

    private static TooltipAnchor? UiAnchor(RectTransform rect, TooltipSide side)
    {
        if (rect == null) { return null; }

        Canvas canvas = rect.GetComponentInParent<Canvas>();

        return canvas != null ? TooltipAnchor.Of(rect, canvas, side) : null;
    }

    private RectTransform StatusRow() => enemyPanel != null ? enemyPanel.StatusRowRect : null;

    private static TooltipAnchor? DropAnchor()
    {
        if (GridManager.Instance == null) { return null; }

        foreach (GridTile tile in GridManager.Instance.AllTiles)
        {
            if (tile == null || tile.Items.Count == 0) { continue; }

            Collider2D collider = tile.GetComponent<Collider2D>();

            if (collider != null) { return TooltipAnchor.Of(collider, TooltipSide.Right); }
        }

        return null;
    }

    /// Captures the character, not their tile - a predicate built from a tile would go stale the moment
    /// anything moved.
    private static Predicate<GridTile> OnTile(Character who) => tile => who != null && tile == who.Tile;

    private static bool HasDrop(GridTile tile) => tile != null && tile.Items.Count > 0;

    private static Predicate<GridTile> FreeTileBeside(Character who)
    {
        return tile =>
        {
            if (who == null || who.Tile == null || tile == null || tile.Occupant != null) { return false; }

            Vector2Int delta = tile.Coordinates - who.Tile.Coordinates;

            // Chebyshev 1 - the eight surrounding tiles, so a diagonal placement counts as "next to".
            return Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) == 1;
        };
    }

    // ---- Roster lookups ---------------------------------------------------------------------------

    /// Party order is spawn order, which is the order RunData authored them in - so slot 0 is the Knight
    /// and slot 1 is the Mage, matching the digit keys PartyPortraitPanel binds.
    private static Character PartyMemberAt(int index)
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return null; }

        int seen = 0;

        foreach (Character character in battle.Characters)
        {
            if (character == null || !character.IsPlayerControlled || character.IsDead) { continue; }

            if (seen++ == index) { return character; }
        }

        return null;
    }

    private static Character FirstEnemy() => FirstEnemyOtherThan(null);

    private static Character FirstEnemyOtherThan(Character exclude)
    {
        BattleManager battle = BattleManager.Instance;

        if (battle == null) { return null; }

        foreach (Character character in battle.Characters)
        {
            if (character == null || character.IsDead || character.IsPlayerControlled) { continue; }
            if (character == exclude) { continue; }

            return character;
        }

        return null;
    }

    private static Card InHand(Character who, CardData data)
    {
        if (who == null || data == null) { return null; }

        foreach (Card card in who.Hand)
        {
            if (card != null && card.Data == data) { return card; }
        }

        return null;
    }

    /// Hand is an IReadOnlyList, which has no Contains of its own - and reaching for LINQ's would run
    /// inside a per-frame WaitUntil predicate.
    private static bool HandHolds(Character who, Card card)
    {
        if (who == null) { return false; }

        foreach (Card held in who.Hand)
        {
            if (held == card) { return true; }
        }

        return false;
    }
}
