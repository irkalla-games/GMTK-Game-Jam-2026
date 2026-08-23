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

    [Tooltip("The next-wave preview strip - its circles are lit while explaining what it shows. "
             + "Populated off LevelData.Waves with no push from the tutorial; see NextWavePanel.")]
    [SerializeField] private NextWavePanel nextWavePanel;

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

    /// Whether the tutorial is currently driving - the same check Active() makes, exposed for anything
    /// outside this class that needs to gate on the tutorial's presence rather than go through one of
    /// the four door methods above. CardPileHud's discard button and RewardPanel's Heal skip button are
    /// the two current readers.
    public static bool TutorialRunning => Active() != null;

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
        "The icon above an enemy shows what it is about to do. Check it before committing to a plan.";

    private const string SlashTitle = "Finish It";
    private const string SlashBody =
        "The skeleton only has 1 health. Select Slash with left click.";

    private const string SlashAimTitle = "Cut It Down";
    private const string SlashAimBody = "Left click the skeleton to attack it.";

    private const string LootTitle = "Enemies Drop Loot";
    private const string LootBody =
        "Beaten enemies leave something behind. Step a hero onto it to claim it.";

    private const string RewardTitle = "Claim Your Reward";
    private const string RewardBody = "Take any reward to add it to your hand.";

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

    private const string SpawnPreviewTitle = "Reinforcements Incoming";
    private const string SpawnPreviewBody =
        "This shows who's about to join the fight, and when. Keep an eye on it.";

    private const string RideOutTitle = "Ride It Out";
    private const string RideOutBody =
        "Click End Turn. One more wave is coming - you'll need everything you just learned.";

    private const string ManaCostTitle = "Cards Cost Mana";
    private const string ManaCostBody =
        "The number in the corner is what a card costs to cast. It refills every turn.";

    private const string ManaPipTitle = "Your Mana Pool";
    private const string ManaPipBody =
        "These pips are this hero's mana. An empty pip is that much less to spend until your next turn.";

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

        if (TutorialPopup.Instance != null)
        {
            TutorialPopup.Instance.Hide();
            TutorialPopup.Instance.EndTutorial();
        }

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

        // Anchored to the Knight rather than the card (AimCard's default): Shield targets the caster's
        // own tile, which for every other card in this script sits well clear of the box, but here it
        // does not - a box above the card would sit right over the very tile it is asking the player to
        // click. See AimCard's boxAnchor parameter.
        yield return AimCard(ShieldAimTitle, ShieldAimBody, knight, shield, OnTile(knight),
            boxAnchor: WorldAnchor(knight, TooltipSide.Right));

        yield return EndTurn(EndTurnTitle, EndTurnBody, holdRound: true);

        // Both of these land in the gap RunBattle now leaves before EnemyResolve - see HoldingRound.
        yield return Read(DiscardTitle, DiscardBody, UiAnchor(discardPileAnchor, TooltipSide.Above));

        // The health bar and intent icon live on the overhead canvas above the enemy's head, not on the
        // tile beneath it - lighting the tile taught nothing a player couldn't already see. The enemy's
        // own SelectedCharacterPanel is not an option here: it only shows once something is selected, and
        // nothing has been yet - see Select(InspectTitle...) further down.
        CharacterOverheadViewer rangerOverhead = ranger.GetComponent<CharacterOverheadViewer>();
        TooltipAnchor? healthBarAt = UiAnchor(
            rangerOverhead != null ? rangerOverhead.HealthBarRect : null, TooltipSide.Right);
        TooltipAnchor? intentIconAt = UiAnchor(
            rangerOverhead != null ? rangerOverhead.IntentIconRect : null, TooltipSide.Right);

        yield return Read(IntentTitle, IntentBody, intentIconAt ?? healthBarAt,
            holes: Holes(healthBarAt, intentIconAt));

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

        // Turn 2's hand is still being dealt one card at a time - see ActiveHandViewer.dealQueue. Without
        // this, SelectCard's CardAnchor can miss the still-arriving Slash card and fall back to a
        // centre-screen box pointing at nothing.
        yield return new WaitUntil(() =>
            Skipped || ActiveHandViewer.Instance == null || !ActiveHandViewer.Instance.Busy);

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

        // LootManager.IsIdle flips false as soon as the pickup is queued - well before the panel actually
        // opens, since Drain still has to wait out ActionManager.IsIdle first - so it is no good as a
        // "the panel is up" signal; RewardPanel.IsShowing is the real one. No dim: the panel already owns
        // the screen and lays its offered cards out itself, and a full-screen dim behind it would darken
        // cards this beat has no way to carve holes around - the box's own anchor (above the title) is
        // what keeps it clear of them instead.
        yield return new WaitUntil(() => Skipped || RewardPanelShowing());
        if (Skipped) { End(); yield break; }

        // done folds in what used to be a separate LootIdle WaitUntil right after this call, and
        // showContinue: false removes the only other way this beat ended - RewardPanel's own buttons
        // are not gated by TutorialGate, so a reward chosen by clicking it directly used to leave the
        // coroutine parked on a Continue click that was never coming, PermitNothing() still in force and
        // the Sap Totem never reachable.
        yield return Read(RewardTitle, RewardBody, RewardPanelAnchor(), dim: false,
            done: () => LootManager.Instance == null || LootManager.Instance.IsIdle, showContinue: false);

        yield return SelectCard(TotemTitle, TotemBody, mage, sapTotem, FreeTileBeside(ranger));
        yield return AimCard(TotemAimTitle, TotemAimBody, mage, sapTotem, FreeTileBeside(ranger));

        yield return Select(InspectTitle, InspectBody, ranger);

        yield return Hover(StatusTitle, StatusBody, UiAnchor(StatusRow(), TooltipSide.Left));

        yield return Read(SapTitle, SapBody);

        // NextWavePanel has already populated itself off LevelData.Waves by now, with no push from here
        // - see TutorialContentGenerator for the turn-3 wave this is previewing.
        RectTransform[] waveRects = nextWavePanel != null ? nextWavePanel.ActiveCircleRects() : null;

        yield return Read(SpawnPreviewTitle, SpawnPreviewBody, RectsAnchor(waveRects, TooltipSide.Below));

        int turnTwo = battle.TurnsElapsed;

        yield return EndTurn(RideOutTitle, RideOutBody, holdRound: false);

        // Same idiom as the turn 1 -> 2 transition above, minus a HoldingRound release - this EndTurn
        // was called with holdRound: false, so there is nothing held to let go of here.
        yield return new WaitUntil(() =>
            Skipped || (battle.TurnsElapsed > turnTwo && battle.Phase == BattlePhase.PlayerActing));

        if (Skipped) { End(); yield break; }

        // Turn 3's hand is still being dealt one card at a time - see the identical wait after turn 1.
        yield return new WaitUntil(() =>
            Skipped || ActiveHandViewer.Instance == null || !ActiveHandViewer.Instance.Busy);

        if (Skipped) { End(); yield break; }

        // Whoever ended turn 2 active carries into turn 3 - Mage, per the script above - but asked live
        // rather than assumed, so a re-ordering of turn 2 cannot silently point this at nobody.
        Character turnThreeHero = battle.ActiveCharacter != null ? battle.ActiveCharacter : mage;
        Card representative = FirstCardInHand(turnThreeHero);

        yield return Read(ManaCostTitle, ManaCostBody, CardCostAnchor(representative));

        yield return Read(ManaPipTitle, ManaPipBody, PipRowAnchor(turnThreeHero));

        // Every door open until the round itself ends - see FreePlay's own doc comment.
        yield return FreePlay();

        End();
    }

    // ---- Primitives -----------------------------------------------------------------------------

    /// <summary>
    /// Nothing on the board to do - Continue is the only way on, unless `done` supplies another. `at`
    /// null puts the box centre screen; `holes` defaults to just `at` but can be widened - see the
    /// "Read the Intent" beat, which lights both the health bar and the intent icon. `dim` false skips
    /// the spotlight entirely rather than dimming with no holes - see the reward beat, which explains a
    /// panel that already owns the screen and lays itself out; a full dim behind it would darken content
    /// this beat has no anchor to carve a hole around.
    /// </summary>
    private IEnumerator Read(
        string title, string body, TooltipAnchor? at = null, TooltipAnchor[] holes = null, Func<bool> done = null,
        bool dim = true, bool showContinue = true)
    {
        gate.PermitNothing();

        yield return Beat(title, body, at, showContinue: showContinue, holes: holes ?? Holes(at), done: done, dim: dim);
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

    /// <summary>
    /// The second half of a play: same permit, different copy, and it ends when the card actually
    /// leaves the hand. The tile permit is what guarantees it left onto the right tile.
    ///
    /// `boxAnchor` overrides where the box itself sits - the card by default, which is where the player
    /// is looking. The lit holes always include the card regardless, so overriding this never costs the
    /// card its own highlight; it only moves the box clear of a target that would otherwise sit under it -
    /// see the Shield beat, whose target is the caster's own tile.
    /// </summary>
    private IEnumerator AimCard(
        string title, string body, Character actor, CardData data, Predicate<GridTile> targets,
        TooltipAnchor? boxAnchor = null)
    {
        Card card = InHand(actor, data);

        if (card == null) { yield break; }

        Predicate<GridTile> legal = Legal(actor, card, targets);

        gate.Permit(allowedCard: card, allowedTiles: legal);

        // The lit tiles are the board's own in-range highlight, which the spotlight also leaves visible.
        TooltipAnchor? cardAt = CardAnchor(card);
        TooltipAnchor? at = boxAnchor ?? cardAt;

        yield return Beat(title, body, at, showContinue: false, holes: TargetHoles(cardAt, legal),
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
    /// Waits for the cursor to rest on something rather than for a click - Continue is the only way on,
    /// same as Read, so a player who cannot find the hover is never stuck on it.
    ///
    /// Not ended by TooltipManager.IsShowing turning true: that fires the instant the hover starts, before
    /// the tooltip has even faded in, so the beat would tear itself down before there was anything to
    /// read. The whole point of this beat is to let the player read it, so Continue is what ends it.
    /// </summary>
    private IEnumerator Hover(string title, string body, TooltipAnchor? at)
    {
        gate.PermitNothing();

        yield return Beat(title, body, at, showContinue: true, holes: Holes(at), done: null);
    }

    /// <summary>
    /// The free-play tail of turn 3: every door stays open until the round itself ends - End Turn
    /// pressed, or the phase leaves PlayerActing - see TutorialGate's allowAll. No Beat() call and so no
    /// popup: unlike every primitive above, this one has nothing left to say, which is the point of it.
    /// </summary>
    private IEnumerator FreePlay()
    {
        gate.Permit(allowAll: true);

        yield return new WaitUntil(() =>
            Skipped
            || (BattleManager.Instance != null
                && (BattleManager.Instance.EndTurnRequested
                 || BattleManager.Instance.Phase != BattlePhase.PlayerActing)));
    }

    /// <summary>
    /// The one place a step actually runs: light the holes, put the box up, and wait for whichever of
    /// `done`, Continue or Skip arrives first.
    ///
    /// Every exit path goes through here, which is what makes "the tutorial can always be escaped" true
    /// by construction rather than by remembering to check Skip in twenty places.
    ///
    /// `dim` false leaves TutorialSpotlight untouched instead of calling Show with no effect - the reward
    /// beat is the one caller that passes it, since Show(holes) with holes it cannot supply would dim
    /// content it has no way to protect. Untouched, not forced clear, because the beat before this one
    /// already tore its own spotlight down through this same method.
    /// </summary>
    private IEnumerator Beat(
        string title, string body, TooltipAnchor? at, bool showContinue, TooltipAnchor[] holes, Func<bool> done,
        bool dim = true)
    {
        if (dim) { TutorialSpotlight.Instance.Show(holes); }

        TutorialPopup.Instance.Show(title, body, at, showContinue);

        yield return new WaitUntil(() =>
            Skipped
            || (done != null && done())
            || (showContinue && TutorialPopup.Instance.ConsumeContinue()));

        TutorialPopup.Instance.Hide();

        if (dim) { TutorialSpotlight.Instance.Clear(); }
    }

    private bool Skipped => TutorialPopup.Instance != null && TutorialPopup.Instance.SkipRequested;

    // ---- Anchors and predicates -------------------------------------------------------------------

    private static TooltipAnchor[] Holes(TooltipAnchor? at) =>
        at.HasValue ? new[] { at.Value } : Array.Empty<TooltipAnchor>();

    /// Two independent holes rather than one - see the "Read the Intent" beat, which lights the health
    /// bar and the intent icon at once rather than picking only one to point the box at.
    private static TooltipAnchor[] Holes(TooltipAnchor? a, TooltipAnchor? b)
    {
        List<TooltipAnchor> holes = new();

        if (a.HasValue) { holes.Add(a.Value); }
        if (b.HasValue) { holes.Add(b.Value); }

        return holes.ToArray();
    }

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

    /// Mirrors CardAnchor, but anchors the cost pip specifically - see the mana-cost beat. The pip has
    /// no collider of its own (only the card's root Hitbox does), so this goes through TooltipAnchor's
    /// Renderer overload instead of the Collider2D one CardAnchor uses.
    private static TooltipAnchor? CardCostAnchor(Card card)
    {
        ActiveHandViewer hand = ActiveHandViewer.Instance;

        if (hand == null) { return null; }

        CardViewer viewer = hand.ViewerFor(card);

        if (viewer == null || viewer.CostRenderer == null) { return null; }

        return TooltipAnchor.Of(viewer.CostRenderer, TooltipSide.Above);
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

    /// UiAnchor's plural - a group of manually-positioned siblings lit as one, used by PipRowAnchor and
    /// the spawn-preview beat. See TooltipAnchor.Of(RectTransform[], ...) for why the group has to be
    /// resolved as a whole rather than through any single member's parent.
    private static TooltipAnchor? RectsAnchor(RectTransform[] rects, TooltipSide side)
    {
        if (rects == null || rects.Length == 0) { return null; }

        RectTransform first = null;

        foreach (RectTransform rect in rects)
        {
            if (rect != null) { first = rect; break; }
        }

        if (first == null) { return null; }

        Canvas canvas = first.GetComponentInParent<Canvas>();

        return canvas != null ? TooltipAnchor.Of(rects, canvas, side) : null;
    }

    private RectTransform StatusRow() => enemyPanel != null ? enemyPanel.StatusRowRect : null;

    /// Highlights this hero's whole mana-pip row for the mana-pip beat - every pip, not the single
    /// representative card CardCostAnchor points at, since "how much mana you have" is the row as a
    /// whole. No `?.` on portraitPanel.PortraitFor's result - it is a UnityEngine.Object, and this
    /// codebase's convention is the two-line null-check form, not `?.`, for exactly that type.
    private TooltipAnchor? PipRowAnchor(Character hero)
    {
        HeroPortrait portrait = portraitPanel != null ? portraitPanel.PortraitFor(hero) : null;
        RectTransform[] pipRects = portrait != null ? portrait.ActivePipRects() : null;

        return RectsAnchor(pipRects, TooltipSide.Above);
    }

    private static bool RewardPanelShowing() =>
        LootManager.Instance != null && LootManager.Instance.Panel != null
        && LootManager.Instance.Panel.IsShowing;

    /// The panel's own root, not its title - see RewardPanel.Rect for why a full-screen anchor is what
    /// actually pins the box against the top edge instead of risking a fit beside the title that reaches
    /// down over the offered cards.
    private static TooltipAnchor? RewardPanelAnchor()
    {
        RewardPanel panel = LootManager.Instance != null ? LootManager.Instance.Panel : null;

        return UiAnchor(panel != null ? panel.Rect : null, TooltipSide.Above);
    }

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

    /// One card from this hero's hand - "a representative example" for the mana-cost and mana-pip
    /// beats, which point at one card rather than walking the whole hand. Null only if the hand is
    /// empty, which the anchor helpers above already handle by falling back to a centre-screen box.
    private static Card FirstCardInHand(Character who)
    {
        if (who == null) { return null; }

        foreach (Card card in who.Hand) { if (card != null) { return card; } }

        return null;
    }
}
