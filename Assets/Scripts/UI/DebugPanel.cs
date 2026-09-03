using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Which list the browser is showing, and therefore what its one action button does.
///
/// Appended to, never reordered - this is written into the browse dropdown's saved index.
/// </summary>
public enum DebugMode
{
    Cards = 0,
    Equipment = 1,
    Enemies = 2,
    Status = 3,
    Totems = 4,
}

/// <summary>
/// The in-battle debug tools: browse cards, equipment, enemies, statuses or totems in one grid and act
/// on whichever you pick.
///
/// Opened with the backquote key, not from the pause menu, and only while GameSettings.DebugRunEnabled
/// is on. Hidden in Awake regardless of authored state, same panel contract as PauseSettingsPanel.
///
/// Unlike every other panel here it does NOT freeze time. The pause menu stops the world because that
/// is what pausing means; these are tools for watching the world run, and half of what you want to see
/// - a spawned enemy taking its turn, a status ticking down - only happens while it does.
///
/// One grid, one tile pool, one detail pane and ONE action button serve every mode. The button's label
/// is the mode's whole vocabulary: Grant Card, Give Equipment, Take Equipment, Spawn Enemy, Apply
/// Status, Summon Totem.
///
/// Descriptions are not written here. A status reads its explanation from the Glossary, the same asset
/// the tooltips use, and a summon reads its own stats off the prefab the card actually plays - so a
/// debug description cannot drift from what the game tells the player.
/// </summary>
public class DebugPanel : Singleton<DebugPanel>
{
    [SerializeField] private GameObject root;

    [SerializeField] private Button backButton;

    [Header("Libraries")]
    [SerializeField] private CardLibrary cardLibrary;

    [SerializeField] private EquipmentLibrary equipmentLibrary;

    [SerializeField] private EnemyRegistry enemyRegistry;

    [Tooltip("Where a status's explanation comes from - the same asset the tooltips read, so the two "
             + "cannot disagree.")]
    [SerializeField] private Glossary glossary;

    [Header("Target")]
    [SerializeField] private TMP_Dropdown heroDropdown;

    [Tooltip("Which list the grid shows. A dropdown rather than a button per mode - at five modes the "
             + "buttons were most of the column.")]
    [SerializeField] private TMP_Dropdown browseDropdown;

    [Header("Character actions")]
    [SerializeField] private Button refillEnergyButton;

    [SerializeField] private Button healButton;

    [SerializeField] private Button drawCardButton;

    [SerializeField] private Button discardHandButton;

    [Tooltip("How many stacks Apply Status grants. A constant rather than an input field - a number box "
             + "is a lot of UI for a value that is almost always 1 or 2.")]
    [SerializeField] private int statusStacks = 2;

    [Header("Browser")]
    [SerializeField] private DebugTile tileTemplate;

    [SerializeField] private RectTransform tileGridContent;

    [Tooltip("Class and rarity narrow the card list. Made non-interactable rather than hidden in the "
             + "other modes, so the grid below them never changes size.")]
    [SerializeField] private TMP_Dropdown classFilter;

    [SerializeField] private TMP_Dropdown rarityFilter;

    [SerializeField] private TMP_Text classFilterLabel;

    [SerializeField] private TMP_Text rarityFilterLabel;

    [Header("Detail")]
    [SerializeField] private TMP_Text detailName;

    [SerializeField] private TMP_Text detailMeta;

    [SerializeField] private TMP_Text detailBody;

    [SerializeField] private Image detailArt;

    [SerializeField] private Button actionButton;

    [SerializeField] private TMP_Text actionLabel;

    private DebugMode mode = DebugMode.Cards;

    /// Living player-controlled heroes in roster order, index-aligned with heroDropdown.
    private readonly List<Character> heroes = new();

    /// What the grid is showing. Only the active mode's list is populated.
    private readonly List<CardData> visibleCards = new();

    private readonly List<EquipmentData> visibleEquipment = new();

    private readonly List<GameObject> visibleEnemies = new();

    private readonly List<StatusType> visibleStatuses = new();

    /// Summon effects gathered off the card library, deduplicated by the prefab they place.
    private readonly List<SummonEffect> visibleTotems = new();

    /// Pooled tiles, grown on demand and hidden rather than destroyed.
    private readonly List<DebugTile> tiles = new();

    private readonly List<CharacterClass> classOptions = new();

    private readonly List<int> rarityOptions = new();

    /// Index into whichever list the active mode uses, or -1.
    private int selected = -1;

    public bool IsOpen { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        if (root != null) { root.SetActive(false); }

        if (backButton != null) { backButton.onClick.AddListener(Close); }

        if (refillEnergyButton != null) { refillEnergyButton.onClick.AddListener(RefillEnergy); }

        if (healButton != null) { healButton.onClick.AddListener(HealToFull); }

        if (drawCardButton != null) { drawCardButton.onClick.AddListener(DrawCard); }

        if (discardHandButton != null) { discardHandButton.onClick.AddListener(DiscardHand); }

        if (actionButton != null) { actionButton.onClick.AddListener(PrimaryAction); }

        if (browseDropdown != null) { browseDropdown.onValueChanged.AddListener(OnBrowseChanged); }

        // The template is a styled tile that must never be visible itself - it exists to be cloned.
        if (tileTemplate != null) { tileTemplate.gameObject.SetActive(false); }

        if (classFilter != null) { classFilter.onValueChanged.AddListener(_ => Refresh()); }

        if (rarityFilter != null) { rarityFilter.onValueChanged.AddListener(_ => Refresh()); }

        // Equipment badges and the enemy list both describe the selected hero.
        if (heroDropdown != null) { heroDropdown.onValueChanged.AddListener(_ => Refresh()); }
    }

    /// <summary>
    /// Backquote toggles the panel, gated on the same setting as the pause menu's debug hint - a normal
    /// game must never be one keypress away from spawning bosses.
    /// </summary>
    private void Update()
    {
        if (Keyboard.current == null) { return; }

        if (!Keyboard.current.backquoteKey.wasPressedThisFrame) { return; }

        if (IsOpen) { Close(); return; }

        if (!GameSettings.DebugRunEnabled) { return; }

        Show();
    }

    public void Show()
    {
        if (IsOpen) { return; }

        IsOpen = true;

        if (root != null) { root.SetActive(true); }

        RebuildHeroes();
        BuildBrowseOptions();
        BuildCardFilters();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) { return; }

        IsOpen = false;

        if (root != null) { root.SetActive(false); }
    }

    private void OnBrowseChanged(int index)
    {
        mode = (DebugMode)index;

        // Selection is an index into a list that just changed underneath it.
        selected = -1;

        Refresh();
    }

    /// <summary>
    /// Rebuilds everything that depends on the mode or the selected hero.
    ///
    /// The card filters are DISABLED rather than hidden outside card mode. Hiding them collapsed two
    /// rows and moved the grid up, so the browser was visibly a different size per mode - which is the
    /// one thing it must not be.
    /// </summary>
    private void Refresh()
    {
        RebuildVisible();
        RebuildTiles();
        ShowDetail();

        bool cards = mode == DebugMode.Cards;

        if (classFilter != null) { classFilter.interactable = cards; }

        if (rarityFilter != null) { rarityFilter.interactable = cards; }

        if (classFilterLabel != null)
        {
            classFilterLabel.color = cards ? PanelPalette.Ink : PanelPalette.LabelDisabled;
        }

        if (rarityFilterLabel != null)
        {
            rarityFilterLabel.color = cards ? PanelPalette.Ink : PanelPalette.LabelDisabled;
        }
    }

    /// The hero every action targets, or null when the party is empty.
    private Character SelectedHero()
    {
        if (heroDropdown == null || heroes.Count == 0) { return null; }

        return heroes[Mathf.Clamp(heroDropdown.value, 0, heroes.Count - 1)];
    }

    /// How many entries the active mode has.
    private int VisibleCount() => mode switch
    {
        DebugMode.Equipment => visibleEquipment.Count,
        DebugMode.Enemies => visibleEnemies.Count,
        DebugMode.Status => visibleStatuses.Count,
        DebugMode.Totems => visibleTotems.Count,
        _ => visibleCards.Count,
    };

    /// <summary>
    /// Fills the active mode's list from its source.
    ///
    /// Cards walk CardLibrary.Cards directly rather than calling Offerable: that filters to one exact
    /// rarity and drops everything marked excludeFromRewards or NotOffered, which is right for a reward
    /// screen and wrong here - the cards most worth testing are often the ones a reward never offers.
    /// </summary>
    private void RebuildVisible()
    {
        visibleCards.Clear();
        visibleEquipment.Clear();
        visibleEnemies.Clear();
        visibleStatuses.Clear();
        visibleTotems.Clear();

        switch (mode)
        {
            case DebugMode.Equipment:
                if (equipmentLibrary == null) { break; }

                foreach (EquipmentData item in equipmentLibrary.Items)
                {
                    if (item != null) { visibleEquipment.Add(item); }
                }

                break;

            case DebugMode.Enemies:
                if (enemyRegistry == null) { break; }

                foreach (EnemyRegistryEntry entry in enemyRegistry.Entries)
                {
                    if (entry.prefab != null) { visibleEnemies.Add(entry.prefab); }
                }

                break;

            case DebugMode.Status:
                BuildStatusList();
                break;

            case DebugMode.Totems:
                BuildTotemList();
                break;

            default:
                BuildCardList();
                break;
        }
    }

    private void BuildCardList()
    {
        if (cardLibrary == null) { return; }

        CharacterClass wantedClass = classFilter == null || classOptions.Count == 0
            ? CharacterClass.Any
            : classOptions[Mathf.Clamp(classFilter.value, 0, classOptions.Count - 1)];

        int wantedRarity = rarityFilter == null || rarityOptions.Count == 0
            ? -1
            : rarityOptions[Mathf.Clamp(rarityFilter.value, 0, rarityOptions.Count - 1)];

        foreach (CardData card in cardLibrary.Cards)
        {
            if (card == null) { continue; }

            // Any means "no restriction", so an unrestricted card passes a class filter - it really
            // can be given to that hero.
            if (wantedClass != CharacterClass.Any
                && card.requiredClass != CharacterClass.Any
                && (card.requiredClass & wantedClass) == 0)
            {
                continue;
            }

            if (wantedRarity >= 0 && (int)card.rarity != wantedRarity) { continue; }

            visibleCards.Add(card);
        }
    }

    /// <summary>
    /// The StatusTypes that can actually be granted from a type-and-stacks pair.
    ///
    /// StatusEffect.Create returns null for Taunt, GainMultiplier, Potency and TurnTick - all aura-only,
    /// or carrying a reference this signature has nowhere to put - and AddStatus early-returns on null,
    /// so listing them would produce tiles that silently do nothing. None is not a status, and Summoned
    /// kills whatever carries it.
    /// </summary>
    private void BuildStatusList()
    {
        foreach (StatusType type in System.Enum.GetValues(typeof(StatusType)))
        {
            if (type == StatusType.None || type == StatusType.Summoned) { continue; }

            if (type == StatusType.Taunt || type == StatusType.GainMultiplier) { continue; }

            if (type == StatusType.Potency || type == StatusType.TurnTick) { continue; }

            visibleStatuses.Add(type);
        }
    }

    /// <summary>
    /// Every distinct summon any card in the library places - totems, allies, walls.
    ///
    /// Gathered off the cards rather than from a registry because there is no registry of summons: the
    /// prefab lives on a SummonEffect, and the effect assets are shared between cards, so the same
    /// totem appears through several of them. Deduplicated by the prefab actually placed, which is the
    /// thing being summoned - two effects differing only in lifetime are one totem.
    /// </summary>
    private void BuildTotemList()
    {
        if (cardLibrary == null) { return; }

        HashSet<GameObject> seen = new();

        foreach (CardData card in cardLibrary.Cards)
        {
            if (card == null) { continue; }

            foreach (CardEffectEntry entry in card.effectEntries) { Consider(entry.effect, seen); }

            // The pre-entries list, still populated on older card assets.
            if (card.effects == null) { continue; }

            foreach (CardEffect effect in card.effects) { Consider(effect, seen); }
        }
    }

    private void Consider(CardEffect effect, HashSet<GameObject> seen)
    {
        if (effect is not SummonEffect summon) { return; }

        if (summon.SummonedObject == null) { return; }

        if (!seen.Add(summon.SummonedObject)) { return; }

        visibleTotems.Add(summon);
    }

    /// <summary>
    /// Binds one pooled tile per visible entry and hides the surplus.
    ///
    /// The badge is the one thing that differs per mode: a card's energy cost, an equipment's carried
    /// count, an enemy's or totem's max health.
    /// </summary>
    private void RebuildTiles()
    {
        int count = VisibleCount();

        EnsureTiles(count);

        Character hero = SelectedHero();

        for (int i = 0; i < tiles.Count; i++)
        {
            bool used = i < count;

            tiles[i].gameObject.SetActive(used);

            if (!used) { continue; }

            switch (mode)
            {
                case DebugMode.Equipment:
                    EquipmentData item = visibleEquipment[i];
                    int carried = CarriedCount(hero, item);

                    // Duplicates stack - Equip runs an item's Project and Apply again rather than
                    // ignoring the second copy - so the count is real information.
                    tiles[i].Bind(i, item.icon, item.equipmentName,
                        carried > 0 ? $"x{carried}" : string.Empty, Select);
                    break;

                case DebugMode.Enemies:
                    tiles[i].Bind(i, null, visibleEnemies[i].name, HealthBadge(visibleEnemies[i]), Select);
                    break;

                case DebugMode.Status:
                    tiles[i].Bind(i, null, visibleStatuses[i].ToString(), string.Empty, Select);
                    break;

                case DebugMode.Totems:
                    GameObject summon = visibleTotems[i].SummonedObject;
                    tiles[i].Bind(i, null, summon.name, HealthBadge(summon), Select);
                    break;

                default:
                    CardData card = visibleCards[i];
                    tiles[i].Bind(i, card.image, card.cardName, card.cost.ToString(), Select);
                    break;
            }

            tiles[i].SetSelected(i == selected);
        }
    }

    private static string HealthBadge(GameObject prefab)
    {
        Character character = prefab.GetComponent<Character>();

        return character != null ? character.MaxHealth.ToString() : string.Empty;
    }

    /// Grows the pool, cloning the template. Never shrinks it - the surplus is deactivated above, which
    /// is what makes switching modes back and forth free after the first pass.
    private void EnsureTiles(int count)
    {
        if (tileTemplate == null || tileGridContent == null) { return; }

        while (tiles.Count < count)
        {
            DebugTile tile = Instantiate(tileTemplate, tileGridContent);
            tile.gameObject.SetActive(true);
            tiles.Add(tile);
        }
    }

    private void Select(int index)
    {
        selected = index;

        for (int i = 0; i < tiles.Count; i++) { tiles[i].SetSelected(i == selected); }

        ShowDetail();
    }

    /// Writes the selected entry into the detail pane and relabels the one action button.
    private void ShowDetail()
    {
        bool has = selected >= 0 && selected < VisibleCount();

        if (actionButton != null) { actionButton.interactable = has; }

        if (!has)
        {
            SetDetail(null, string.Empty, string.Empty, "select something on the left", "-");
            return;
        }

        Character hero = SelectedHero();

        switch (mode)
        {
            case DebugMode.Equipment: DetailForEquipment(hero); break;
            case DebugMode.Enemies: DetailForCharacter(visibleEnemies[selected], "Spawn Enemy"); break;
            case DebugMode.Status: DetailForStatus(hero); break;
            case DebugMode.Totems: DetailForTotem(); break;
            default: DetailForCard(); break;
        }
    }

    private void DetailForEquipment(Character hero)
    {
        EquipmentData item = visibleEquipment[selected];
        int carried = CarriedCount(hero, item);

        SetDetail(item.icon, item.equipmentName,
            $"{item.rarity}  -  {ClassLabel(item.requiredClass)}",
            carried > 0 ? $"{item.description}\n\ncarried x{carried}" : item.description,
            carried > 0 ? "Take Equipment" : "Give Equipment");
    }

    /// <summary>
    /// A card, plus - when it summons something - that summon's own description underneath.
    ///
    /// The summon half comes from the Glossary through the same SummonContent the tooltip uses, so the
    /// totem's stats are read off the prefab the card really places rather than retyped here.
    /// </summary>
    private void DetailForCard()
    {
        CardData card = visibleCards[selected];

        string keywords = string.Empty;

        foreach (CardKeywordEntry entry in card.keywords)
        {
            keywords += keywords.Length == 0 ? entry.type.ToString() : $", {entry.type}";
        }

        string body = keywords.Length == 0 ? card.description : $"{card.description}\n\n{keywords}";

        SummonEffect summon = FindSummon(card);

        if (summon != null && glossary != null)
        {
            string summonText = Flatten(glossary.SummonContent(summon.SummonedObject, summon.LifetimeTurns));

            if (!string.IsNullOrWhiteSpace(summonText)) { body += $"\n\n{summonText}"; }
        }

        SetDetail(card.image, $"{card.cardName}   [{card.cost}]",
            $"{card.rarity}  -  {ClassLabel(card.requiredClass)}", body, "Grant Card");
    }

    private static SummonEffect FindSummon(CardData card)
    {
        foreach (CardEffectEntry entry in card.effectEntries)
        {
            if (entry.effect is SummonEffect summon && summon.SummonedObject != null) { return summon; }
        }

        if (card.effects == null) { return null; }

        foreach (CardEffect effect in card.effects)
        {
            if (effect is SummonEffect summon && summon.SummonedObject != null) { return summon; }
        }

        return null;
    }

    private void DetailForTotem()
    {
        SummonEffect summon = visibleTotems[selected];

        string extra = glossary != null
            ? Flatten(glossary.SummonContent(summon.SummonedObject, summon.LifetimeTurns))
            : null;

        DetailForCharacter(summon.SummonedObject, "Summon Totem", extra);
    }

    /// Shared by enemies and totems - both are a prefab whose Character carries the numbers.
    private void DetailForCharacter(GameObject prefab, string action, string extra = null)
    {
        Character character = prefab.GetComponent<Character>();

        if (character == null)
        {
            SetDetail(null, prefab.name, "no Character component", string.Empty, action);
            return;
        }

        string body = $"Health {character.MaxHealth}\nAction points {character.ActionPoints}\n"
                      + $"Energy {character.MaxEnergy}\nPower {character.PowerLevel}";

        if (!string.IsNullOrWhiteSpace(extra)) { body += $"\n\n{extra}"; }

        SetDetail(null, character.DisplayName,
            character.IsBoss ? "Boss" : character.BattleRole.ToString(), body, action);
    }

    /// <summary>
    /// A status and what it does, read from the Glossary rather than described here.
    ///
    /// Passes the hero's live status when it has one, so the numbers in the text are the ones actually
    /// on that character - the same thing a status chip's tooltip shows.
    /// </summary>
    private void DetailForStatus(Character hero)
    {
        StatusType type = visibleStatuses[selected];

        string body = "no glossary entry";

        if (glossary != null)
        {
            Status live = hero != null ? hero.FindStatus(type) : null;

            body = Flatten(glossary.StatusContent(type, statusStacks, live));
        }

        int stacks = hero != null ? hero.StatusStacks(type) : 0;

        SetDetail(null, type.ToString(),
            stacks > 0 ? $"carried x{stacks}" : $"grants {statusStacks}", body, "Apply Status");
    }

    /// <summary>
    /// Turns a TooltipContent into plain text for this pane.
    ///
    /// The tooltip renders these entries as styled rows; here they are one block, so only the Term and
    /// Stat kinds carry anything worth showing - a Header repeats the title above and a Figure is a
    /// picture with no text at all.
    /// </summary>
    private static string Flatten(TooltipContent content)
    {
        if (content == null || content.IsEmpty) { return string.Empty; }

        string text = string.Empty;

        foreach (TooltipEntry entry in content.Entries)
        {
            string line = entry.kind switch
            {
                TooltipEntryKind.Term => string.IsNullOrEmpty(entry.title)
                    ? entry.body
                    : $"{entry.title}: {entry.body}",
                TooltipEntryKind.Stat => $"{entry.title}: {entry.body}",
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(line)) { continue; }

            text += text.Length == 0 ? line : $"\n{line}";
        }

        return text;
    }

    private void SetDetail(Sprite icon, string title, string meta, string body, string action)
    {
        if (detailArt != null)
        {
            detailArt.sprite = icon;

            // SetActive, not enabled. Disabling the Image hides the picture but leaves its
            // LayoutElement reserving 150px, which pushed the name into the middle of the pane
            // and shoved the body text out of the bottom - and enemies, statuses and totems
            // never have art, so that gap was there every time. A deactivated object is ignored
            // by the layout group entirely, so the slot collapses and the name starts at the top.
            detailArt.gameObject.SetActive(icon != null);
        }

        if (detailName != null) { detailName.text = title; }

        if (detailMeta != null) { detailMeta.text = meta; }

        if (detailBody != null) { detailBody.text = body; }

        if (actionLabel != null) { actionLabel.text = action; }
    }

    private static string ClassLabel(CharacterClass value) =>
        value == CharacterClass.Any ? "Any class" : value.ToString();

    /// <summary>
    /// What the one action button does, per mode.
    /// </summary>
    private void PrimaryAction()
    {
        if (selected < 0 || selected >= VisibleCount()) { return; }

        switch (mode)
        {
            case DebugMode.Equipment: ToggleEquipment(); break;
            case DebugMode.Enemies: ArmSpawn(visibleEnemies[selected], asSummon: false); break;
            case DebugMode.Status: ApplyStatus(); break;
            case DebugMode.Totems: ArmSpawn(visibleTotems[selected].SummonedObject, asSummon: true); break;
            default: GrantCard(); break;
        }
    }

    /// <summary>
    /// Grants the selected card to the selected hero, into hand and into the run record.
    ///
    /// Both halves matter: AddCardToHand makes it playable now, RecordRunCard is what stops SpawnParty
    /// rebuilding the deck without it at the next level.
    /// </summary>
    private void GrantCard()
    {
        Character hero = SelectedHero();

        if (hero == null) { return; }

        CardData card = visibleCards[selected];

        hero.AddCardToHand(card);

        if (BattleManager.Instance != null) { BattleManager.Instance.RecordRunCard(hero, card); }
    }

    /// <summary>
    /// Gives the selected item, or takes one copy back if the hero already carries it.
    ///
    /// One button rather than two, because the detail pane already says which way it will go. Both
    /// directions touch the run record as well as the live Character - without that half a removal is
    /// undone by the next level load, because SpawnParty rebuilds the party from the record.
    /// </summary>
    private void ToggleEquipment()
    {
        Character hero = SelectedHero();

        if (hero == null) { return; }

        EquipmentData item = visibleEquipment[selected];

        if (CarriedCount(hero, item) > 0)
        {
            hero.Unequip(item);

            if (BattleManager.Instance != null) { BattleManager.Instance.RemoveRunEquipment(hero, item); }
        }
        else
        {
            hero.Equip(item);

            if (BattleManager.Instance != null) { BattleManager.Instance.RecordRunEquipment(hero, item); }
        }

        // The tile badge and the button label both describe what the hero carries, which just changed.
        RebuildTiles();
        ShowDetail();
    }

    private static int CarriedCount(Character hero, EquipmentData item)
    {
        if (hero == null || item == null) { return 0; }

        int count = 0;

        foreach (EquipmentData carried in hero.Equipment)
        {
            if (carried == item) { count++; }
        }

        return count;
    }

    private void ApplyStatus()
    {
        Character hero = SelectedHero();

        if (hero == null) { return; }

        hero.AddStatus(visibleStatuses[selected], statusStacks);

        // The meta line reports how many stacks the hero now carries.
        ShowDetail();
    }

    /// <summary>
    /// Arms the next tile click to place `prefab`, then gets out of the way.
    ///
    /// Closes the panel so the board is visible and clickable. The arming lives on BattleManager because
    /// OnTileClicked is the one door for tile clicks and it decides what a click meant - a panel
    /// intercepting clicks itself would be a second thing deciding that.
    ///
    /// An enemy and a totem take different paths on the other side: an enemy is spawned onto the nearest
    /// free tile, a totem is summoned onto the clicked one through the same GridTile.SummonObject a
    /// Summon card uses - and, unlike the card, costing nothing.
    /// </summary>
    private void ArmSpawn(GameObject prefab, bool asSummon)
    {
        if (BattleManager.Instance == null || prefab == null) { return; }

        if (asSummon) { BattleManager.Instance.ArmDebugSummon(prefab); }
        else { BattleManager.Instance.ArmDebugSpawn(prefab); }

        Close();

        if (AimHintLabel.Instance != null)
        {
            AimHintLabel.Instance.Show($"click a tile to place {prefab.name}");
        }
    }

    /// <summary>
    /// Rebuilds the hero list on every open rather than caching it: heroes die mid-battle and the whole
    /// party is rebuilt on a level load, so a list captured once goes stale with nothing to say so.
    ///
    /// Same filter PartyPortraitPanel uses - living and player-controlled, in roster order - so the two
    /// screens never disagree about who is in the party.
    /// </summary>
    private void RebuildHeroes()
    {
        heroes.Clear();

        if (BattleManager.Instance != null)
        {
            foreach (Character character in BattleManager.Instance.Characters)
            {
                if (character == null || !character.IsPlayerControlled || character.IsDead) { continue; }

                heroes.Add(character);
            }
        }

        List<string> labels = new();

        foreach (Character hero in heroes) { labels.Add(hero.DisplayName); }

        if (labels.Count == 0) { labels.Add("(no living heroes)"); }

        FillDropdown(heroDropdown, labels);
    }

    private void BuildBrowseOptions()
    {
        if (browseDropdown == null) { return; }

        List<string> labels = new() { "Cards", "Equipment", "Enemies", "Status", "Totems" };

        browseDropdown.ClearOptions();
        browseDropdown.AddOptions(labels);
        browseDropdown.SetValueWithoutNotify((int)mode);
        browseDropdown.RefreshShownValue();
    }

    private void BuildCardFilters()
    {
        classOptions.Clear();
        classOptions.Add(CharacterClass.Any);
        classOptions.Add(CharacterClass.Knight);
        classOptions.Add(CharacterClass.Mage);
        classOptions.Add(CharacterClass.Rogue);
        classOptions.Add(CharacterClass.Cleric);

        FillDropdown(classFilter, classOptions.ConvertAll(value =>
            value == CharacterClass.Any ? "All classes" : value.ToString()));

        rarityOptions.Clear();
        rarityOptions.Add(-1);

        foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity))) { rarityOptions.Add((int)rarity); }

        FillDropdown(rarityFilter, rarityOptions.ConvertAll(value =>
            value < 0 ? "All rarities" : ((Rarity)value).ToString()));
    }

    /// Replaces a dropdown's options, keeping the current index where it still exists.
    private static void FillDropdown(TMP_Dropdown dropdown, List<string> labels)
    {
        if (dropdown == null || labels.Count == 0) { return; }

        int previous = dropdown.value;

        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(Mathf.Clamp(previous, 0, labels.Count - 1));
        dropdown.RefreshShownValue();
    }

    private void RefillEnergy()
    {
        Character hero = SelectedHero();

        if (hero != null) { hero.ResetEnergy(); }
    }

    private void HealToFull()
    {
        Character hero = SelectedHero();

        if (hero != null) { hero.Heal(hero.MaxHealth); }
    }

    private void DrawCard()
    {
        Character hero = SelectedHero();

        if (hero != null) { hero.DrawCard(); }
    }

    private void DiscardHand()
    {
        Character hero = SelectedHero();

        if (hero != null) { hero.DiscardHand(); }
    }
}
