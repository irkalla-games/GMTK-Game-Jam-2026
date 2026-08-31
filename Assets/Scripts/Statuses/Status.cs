/// <summary>
/// Anything that modifies a character by taking part in the combat rules. The base of two halves:
///
///   StatusEffect  the character carries it. Merges when re-applied, spends real charges, and ages
///                 itself if ageing is what its counter means.
///   Aura          a Totem projects it while the character stands in range. Rebuilt per query, so
///                 nothing it does to its own counter survives.
///
/// Both have exactly the same capabilities - every hook below is available to either, so a curse
/// cloud around a totem can poison you the same way a poison dart can. The only difference is
/// ownership and lifetime, which is why the split is here rather than a bool on one class.
///
/// A status is a *behaviour*, not a record. Everything a status does to the game lives in its own
/// subclass, reached through the hooks below. The alternative - a plain record plus a switch inside
/// Character - is what this replaced: TakeDamage hard-coded the Parry/Block/Shield order,
/// ComputeOutgoingDamage hard-coded Strength and Double Attack, and every new status meant editing
/// Character again.
///
/// Three kinds of member, and the difference matters:
///
///   OnX        notifications. Something has happened; react to it.
///   XRefusal   gates. Asked *before*, repeatedly, and answered null-or-reason like every other
///              Refusal in this codebase.
///   query      answers a question about what should happen, rather than permitting or reacting to
///              it. ForcedQuarry replaces a decision outright ("go after this instead"); Hides removes
///              a candidate from one before it is made ("do not consider this carrier at all"). Both
///              are asked by TargetSelector.TryPick.
///
/// Hooks run in the order Character.ActiveStatuses returns them: a stable sort by Order, and for any
/// two statuses sharing the same Order (the overwhelming majority - see below), that means auras first
/// and then the character's own in the order they were gained, exactly as if Order did not exist. Only
/// a status that overrides Order moves itself out of that FIFO pack; everything else is untouched by
/// its presence. See DodgeStatus, VulnerableStatus, ShieldStatus and ParryStatus for the incoming chain
/// (Dodge -> Vulnerable -> Block -> Shield -> Parry) and DoubleNextAttackStatus for the outgoing one
/// (Double Attack before Strength).
///
/// **One number, and the status decides what spends it.** There is a single counter, `stacks`, and
/// what it counts is the subclass's business - magnitude, charges, or remaining turns. Nothing outside
/// a status ages it. That is what collapsed the old stacks/turnsRemaining pair: almost every status
/// left one of the two permanently inert, and the blanket "decrement everybody's duration" loop that
/// used to live in Character.OnTurnEnd was the last combat rule outside a subclass.
///
/// Two shapes, and every status is exactly one of them:
///
///   charge-spent  the event the status reacts to spends one.
///                            Block, Parry, Dodge, Double Attack, Double Shield, Shield, Strength.
///   self-ticking  its own OnTurnStart/OnTurnEnd spends it, by decrementing (Poison, Frozen, Rooted,
///                 Taunt, Vulnerable) or by zeroing outright (Weaken, Shield).
///
/// Shield is deliberately in two of those: its pool is drained by damage *and* wiped at turn start.
/// Poison is what proves the merge works - its counter is damage and duration at once, so it deals
/// what it says and then decays, which needs no second field.
/// </summary>
public abstract class Status
{
    /// Which modifier this is. A property rather than a field so an Aura can answer for the effect it
    /// is projecting instead of keeping a second copy that could drift.
    public abstract StatusType type { get; }

    /// <summary>
    /// The one number. What it counts depends on the subclass: magnitude for Weaken, remaining charges
    /// for the ones an event spends (Strength included - see StrengthStatus.AmountPerHit for the fixed
    /// amount each charge adds), remaining turns for the ones that age, and both at once for Poison.
    ///
    /// Still called `stacks` rather than something neutral like `counter` because Character.StatusStacks
    /// and the Glossary's "{stacks}" token are both built on the name, and every authored tooltip body
    /// in the Glossary asset spells it - renaming would mean re-authoring all of them for no
    /// behavioural gain.
    /// </summary>
    public abstract int stacks { get; set; }

    /// Nothing left to hold: a status is done when its counter runs out, whatever the counter meant.
    public bool IsExpired => stacks <= 0;

    /// <summary>
    /// Where this status sorts among the others active on one character - lower runs first. The default
    /// is 0, which every status not listed above still is; a stable sort means every default-0 status
    /// keeps resolving in plain FIFO order relative to every other default-0 status; only the handful
    /// with an opinion move relative to that pack.
    ///
    /// Not a fixed priority enum, because most statuses have no stake in when they run - Poison, Frozen,
    /// Taunt and the rest do not interact with each other's hooks at all, so pinning them would just be
    /// noise. Override this only where a specific ordering is load-bearing, and say why - see
    /// DodgeStatus.Order for the pattern.
    /// </summary>
    public virtual int Order => 0;

    /// <summary>
    /// How hard this particular application hits, for the statuses whose effect has a size as well as a
    /// clock - Weaken's damage reduction, Vulnerable's damage increase. 0 for everything else, whose
    /// effect is either binary (Frozen) or read straight off `stacks` (Poison, Shield).
    ///
    /// The second number the one-counter design deliberately did without, and it is here now for one
    /// reason: equipment retunes it. A Weaken the mage applies wearing a potency ring is a *stronger*
    /// Weaken, not a longer one, so the size has to travel with the application rather than being a
    /// constant every carrier agrees on. Statuses carrying this merge only with one of matching size -
    /// see StatusEffect.MergesWith - because folding a long weak one into a short strong one would have
    /// to pick a single number for something that is genuinely two applications.
    ///
    /// Ranked, not summed: Character.FindStatus hands back the biggest, the damage pipeline applies only
    /// that one (see DamageInfo.weakenAmount), and only that one spends a turn, so the rest queue behind
    /// it intact - see WeakenStatus.OnTurnEnd.
    /// </summary>
    public virtual int Amount => 0;

    /// <summary>
    /// Whether something else is holding this status up rather than the character carrying it - a
    /// Totem's aura. A projected status has no clock of its own worth showing (it lasts exactly as long
    /// as you stand in range), which is what the status row reads to leave its badge blank.
    ///
    /// On Status rather than a `is Aura` test at each call site so the question has one answer. Note an
    /// equipment modifier's projection answers false: those are plain StatusEffects appended by
    /// EquipmentModifier.Project, and equipment already has its own UI rather than a status chip.
    /// </summary>
    public virtual bool IsProjected => false;

    /// <summary>
    /// How much this status adds to the *size* of a `type` its carrier is about to apply to somebody
    /// else. The applier-side counterpart to PotencyStatus, which boosts a status the carrier already
    /// holds: this one is asked of whoever is swinging, before the status they grant is even built.
    ///
    /// Asked by Character.AppliedPotency, which StatusAction consults for a card and Totem.Project for
    /// an aura - so a mage's Weaken ring deepens both the Weaken they play and the Weaken their Sap
    /// Totem projects, the totem having inherited the bonus at summon time.
    /// </summary>
    public virtual int AppliedPotency(StatusType type) => 0;

    /// <summary>
    /// Why this status stops its carrier doing anything at all, or null if it does not object. Frozen.
    ///
    /// A gate, not a notification, which is why it is not one of the OnX hooks. Those fire when
    /// something has already happened; this is asked *before*, over and over - on every click, by the
    /// turn loop each frame, and by each enemy before it acts. It also has to answer correctly the
    /// instant it is applied: freezing an enemy during PlayerActing must deny the action it takes in
    /// EnemyResolve that same round, which a turn-start hook could not do because it already ran.
    /// </summary>
    public virtual string ActRefusal(Character carrier) => null;

    /// <summary>
    /// Why this status stops its carrier stepping onto `destination`, or null if it does not object.
    /// Rooted. The other gate, same shape as ActRefusal.
    ///
    /// A refusal rather than an "OnMove" notification because the answer is needed *before* the move:
    /// GridManager.MoveRefusal is what MoveEffect.Refusal asks, which is what gates the click above the
    /// commit point and what builds the tile highlight. A hook that only fired after the fact could not
    /// refuse anything.
    /// </summary>
    public virtual string MoveRefusal(Character carrier, GridTile destination) => null;

    /// <summary>
    /// Who this status forces its carrier to go after, overriding whatever its TargetingPattern would
    /// have named, or null if it does not redirect. Taunt.
    ///
    /// A third kind of member, alongside the OnX notifications and the XRefusal gates: a *query*, and
    /// the only one of the three that answers "instead of what you were going to do, do this". It is
    /// asked by TargetSelector.TryPick, which is the single place both the attack's victim and the
    /// movement quarry are chosen - so one answer here redirects the swing and the walk together.
    ///
    /// Returning a character the caller cannot use is fine and is the whole design: an attack asks with
    /// only the enemies it can legally hit as candidates, so a forced quarry that is out of reach comes
    /// back as "no attack this action point" rather than as a fallback to the pattern.
    ///
    /// On Status rather than on StatusEffect so an Aura can redirect too - a totem projecting a taunt
    /// cloud needs no further plumbing.
    /// </summary>
    public virtual Character ForcedQuarry(Character carrier) => null;

    /// The carrier is about to deal damage. Return the hit as this status leaves it - DamageInfo is
    /// immutable, so a status that has nothing to say returns what it was given.
    public virtual DamageInfo OnDealDamage(DamageInfo info) => info;

    /// The carrier is being hit. Return it reduced, or negated outright - see DamageInfo.
    public virtual DamageInfo OnTakeDamage(DamageInfo info) => info;

    /// The carrier is about to gain shield. Return it doubled, reduced, or untouched - see ShieldInfo.
    /// Only reaches statuses on the carrier receiving the shield; there is no "dealing" side to this
    /// one the way OnDealDamage has an attacker, since nobody swings to grant shield.
    public virtual ShieldInfo OnGainShield(ShieldInfo info) => info;

    /// <summary>
    /// The carrier is about to gain a status - any status, including this one's own type. Return it
    /// scaled or untouched - see StatusGainInfo. Run from Character.AddStatus before the incoming
    /// StatusEffect is merged or appended, so a totem's GainMultiplierStatus can double the stacks a
    /// Strength card is about to land without StatusAction or ApplyStatusEffect knowing anything
    /// changed.
    ///
    /// Like OnGainShield, only reaches statuses on the carrier receiving the status - there is no
    /// "applying" side, since AddStatus has no attacker, only a target.
    /// </summary>
    public virtual StatusGainInfo OnGainStatus(StatusGainInfo info) => info;

    /// <summary>
    /// The carrier has just landed a hit. A notification, and the counterpart to OnDealDamage: that
    /// one runs before the swing with no victim decided yet (an AoE resolves it once for every tile
    /// it is about to hit), this one runs once per victim, after the hit has connected, and knows who
    /// took it. `info` carries attacker, target and the amount that survived mitigation.
    ///
    /// Fired from Character.TakeDamage, the one place that knows a blow actually connected and what
    /// the target's own statuses left of it. A rider that itself deals damage would recurse back
    /// through there - grant statuses and resources here, not further hits.
    /// </summary>
    public virtual void OnDamageDealt(DamageInfo info) { }

    /// <summary>
    /// The carrier has just summoned something - `summon` is the freshly spawned Character, already
    /// placed on the board. A notification, the same shape as OnDamageDealt: fired once per summon,
    /// after SummonAction has already spawned and placed it, so a status here may safely read or adjust
    /// the summon's own stats (a totem-boosting relic raising its max health, say) without racing
    /// anything that still expects an unplaced Character.
    ///
    /// Fired from SummonAction.Execute over the summoner's ActiveStatuses - not the summon's own, since
    /// the summon has no statuses of its own yet at the moment it needs adjusting.
    /// </summary>
    public virtual void OnSummoned(Character summoner, Character summon) { }

    /// <summary>
    /// Whether this status hides its carrier from enemy target selection. Stealth.
    ///
    /// The second query alongside ForcedQuarry: that one replaces the answer to "who does this
    /// priority point at", this one removes a candidate from the question before it is asked. Asked by
    /// TargetSelector.TryPick, which is the single place both the attack's victim and the movement
    /// quarry are chosen - so one answer here withdraws the carrier from both the swing and the walk
    /// at once.
    /// </summary>
    public virtual bool Hides(Character carrier) => false;

    /// The top of a round, before anybody acts. Shield wipes itself here.
    public virtual void OnTurnStart(Character carrier) { }

    /// <summary>
    /// The end of the carrier's own phase. Poison bites here, and every status that ages does its own
    /// ageing here - there is no separate pass afterwards.
    ///
    /// A status that ages does both jobs in this one method, in that order: do the thing, then spend
    /// the counter. Poison deals `stacks` and *then* decrements, which is what gives a status with one
    /// turn left its last tick. Character.PruneExpired drops whatever hit zero.
    /// </summary>
    public virtual void OnTurnEnd(Character carrier) { }

    /// <summary>
    /// What a UI shows for this status, without its duration - the caller appends that, so every
    /// status displays it the same way.
    ///
    /// Virtual because the numbers do not all mean the same thing: Block has two of them, and a binary
    /// status like Frozen has none worth showing.
    /// </summary>
    public virtual string Describe() => $"{type} x{stacks}";

    /// <summary>
    /// The same job as Describe(), but filling numbers into a sentence somebody else authored - the
    /// tooltip's "Negates the damage for {stacks} hits and reflects it back".
    ///
    /// Virtual for the same reason Describe() is, and it is the whole reason the caller does not just
    /// run string.Format itself: only BlockStatus knows it has a second number, and only it should have
    /// to. A status with nothing extra to say fills {stacks} and hands the rest back untouched.
    ///
    /// Tokens it does not recognise are left in place on purpose - Glossary fills whatever survives
    /// from the authored defaults, so a card describing a status the character does not carry still
    /// reads as a sentence.
    /// </summary>
    public virtual string Describe(string template)
    {
        return template == null ? null : template.Replace(Glossary.StacksToken, stacks.ToString());
    }
}
