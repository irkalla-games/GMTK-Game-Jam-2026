using UnityEngine;

/// <summary>
/// Which of a character's three numeric events a damage popup is reporting - purely a colour and a
/// sign, nothing else varies by kind.
/// </summary>
public enum DamageNumberKind
{
    Attack,
    Unblockable,
    Heal,
}

/// <summary>
/// Pops a FloatingText over a tile whenever a character is hit, poisoned or healed - and the general
/// door for anything else that wants to pop a label later ("Blocked!", a status name).
///
/// Hooks every character itself, via BattleManager.CharacterJoined/CharacterLeft, rather than a
/// component sitting on every character prefab. That used to be a DamageNumberViewer added by hand to
/// each of PlayerKnight/PlayerMage/SkeletonWarrior/EnemyRanger/Totem - one more thing to remember on
/// every new character, and the failure mode was silent. BattleManager.Subscribe is already the single
/// choke point every character passes through (scene-placed, spawned or summoned alike), so this rides
/// the event it raises instead.
///
/// Anchored to the tile a character stands on rather than measuring that character's own renderers -
/// every tile is the same size, so a fixed offset from tile centre places a label consistently no
/// matter which prefab happens to be standing there, with nothing to cache and nothing to keep in
/// sync as a puppet animates its child sprites.
/// </summary>
public class FloatingTextManager : Singleton<FloatingTextManager>
{
    [SerializeField] private FloatingText prefab;

    [Tooltip("World-space offset from a tile's centre a label spawns at.")]
    [SerializeField] private Vector3 tileOffset = new(0f, 1.2f, 0f);

    [Tooltip("World-unit cap height used when a caller does not ask for one.")]
    [SerializeField] private float defaultHeight = 0.35f;

    [SerializeField] private Color attackColor = new(0.85f, 0.15f, 0.1f);

    [SerializeField] private Color unblockableColor = new(0.5f, 0.75f, 0.15f);

    [SerializeField] private Color healColor = new(0.25f, 0.9f, 0.35f);

    [Tooltip("World-unit cap height of a damage number when the hit barely registers.")]
    [SerializeField] private float minHeight = 0.25f;

    [Tooltip("World-unit cap height of a damage number once healthShare reaches shareAtMaxSize.")]
    [SerializeField] private float maxHeight = 0.5f;

    [Tooltip("Fraction of the target's max health a hit needs to remove to reach maxHeight. 0.5 means "
             + "a hit for half your max health is already at full size.")]
    [SerializeField] private float shareAtMaxSize = 0.5f;

    private void Start()
    {
        if (BattleManager.Instance == null) { return; }

        // Belt and braces. Unity runs every Awake before any Start but leaves Start order between
        // components undefined, so BattleManager.Start either already ran (its roster is full and
        // CharacterJoined will raise nothing more for those) or has not run yet (the roster is empty
        // and CharacterJoined delivers all of them as they arrive). Hook's own -= before += keeps a
        // character reached by both paths subscribed exactly once.
        //
        // Null-checked the same way BattleManager.Start's own walk of this list is: `characters` is
        // authored in the Inspector and normally left empty (SpawnParty/SpawnEnemies populate it
        // instead), but an unfilled slot serializes as a null entry rather than no entry at all.
        foreach (Character character in BattleManager.Instance.Characters)
        {
            if (character != null) { Hook(character); }
        }

        BattleManager.Instance.CharacterJoined += Hook;
        BattleManager.Instance.CharacterLeft += Unhook;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (BattleManager.Instance == null) { return; }

        BattleManager.Instance.CharacterJoined -= Hook;
        BattleManager.Instance.CharacterLeft -= Unhook;
    }

    private void Hook(Character character)
    {
        Unhook(character);

        // DamageRegistered, not Damaged - Damaged skips a fully mitigated hit so CharacterAnimator's
        // flinch does not play on it, but the popup wants to say "this landed for nothing" (Shield
        // absorbing it, Parry negating it) rather than staying silent. See its doc on Character.
        character.DamageRegistered += OnDamageRegistered;
        character.DamagedUnblockable += OnDamagedUnblockable;
        character.Healed += OnHealed;
    }

    private void Unhook(Character character)
    {
        character.DamageRegistered -= OnDamageRegistered;
        character.DamagedUnblockable -= OnDamagedUnblockable;
        character.Healed -= OnHealed;
    }

    private void OnDamageRegistered(Character character, int amount) => ShowNumber(character, amount, DamageNumberKind.Attack);

    private void OnDamagedUnblockable(Character character, int amount) => ShowNumber(character, amount, DamageNumberKind.Unblockable);

    private void OnHealed(Character character, int amount) => ShowNumber(character, amount, DamageNumberKind.Heal);

    private void ShowNumber(Character target, int amount, DamageNumberKind kind)
    {
        float share = amount / Mathf.Max(1f, target.MaxHealth);
        float t = Mathf.InverseLerp(0f, shareAtMaxSize, share);
        float height = Mathf.Lerp(minHeight, maxHeight, t);

        string body = kind == DamageNumberKind.Heal ? $"+{amount}" : amount.ToString();
        Color color = kind switch
        {
            DamageNumberKind.Unblockable => unblockableColor,
            DamageNumberKind.Heal => healColor,
            _ => attackColor,
        };

        Show(target, body, color, height);
    }

    /// A "0" over an empty tile - an attack landed on nobody, most often because the target dodged away
    /// earlier in the same resolution. GridTile.DealDamage is the only caller: there's no Character
    /// there to raise DamageRegistered from, which is the event every other damage popup rides on.
    public void ShowMiss(GridTile tile) => Show(tile, "0", attackColor, minHeight);

    /// <summary>
    /// Pops an arbitrary label over a character's tile - the general door damage numbers are the first
    /// caller of, not the only one. `height` at 0 means "unset" and falls back to defaultHeight, the
    /// same convention Projectile.scale and CueBinding.duration use: a label authored at nothing would
    /// be as invisible as a shot that arrived instantly.
    /// </summary>
    public void Show(Character target, string body, Color color, float height = 0f)
    {
        if (target == null) { return; }

        // A body that has not been placed on the board yet has no Tile - fall back to wherever it
        // actually is rather than dropping the label.
        Vector3 position = target.Tile != null ? target.Tile.transform.position : target.transform.position;

        Show(position, body, color, height);
    }

    /// The tile-anchored overload proper - Show(Character, ...) above just resolves down to this.
    public void Show(GridTile tile, string body, Color color, float height = 0f)
    {
        if (tile == null) { return; }

        Show(tile.transform.position, body, color, height);
    }

    private void Show(Vector3 tileWorldPosition, string body, Color color, float height)
    {
        if (prefab == null) { return; }

        if (height <= 0f) { height = defaultHeight; }

        FloatingText label = Instantiate(prefab);
        label.Play(body, color, tileWorldPosition + tileOffset, height);
    }
}
