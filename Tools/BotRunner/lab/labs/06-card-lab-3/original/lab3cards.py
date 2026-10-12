"""Card lab 3: the adjacency cards as data - text per round from lab3-numbers.json, mirroring CardLab3Builder.cs."""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))

# name: (hero, rarity, cost, kind, defaults, text(n))
# kind: bonus = bigger next to a hero; shared = reaches heroes next to you; move = move into formation;
# discount = cheaper next to a hero.
CARDS = {
    'Shoulder to Shoulder': ('Knight', 'Uncommon', 1, 'shared', {'shield': 6},
                             lambda n: f"You and allies next to you gain {n['shield']} Shield."),
    'Flanking Strike': ('Knight', 'Uncommon', 1, 'bonus', {'damage': 8, 'bonus': 4, 'draw': 0, 'shield': 0},
                        lambda n: f"Deal {n['damage']} damage" + (f" and gain {n['shield']} Shield" if n['shield'] else '') + f". Next to another hero: deal {n['damage'] + n['bonus']}" + (' and draw 1.' if n['draw'] else ' instead.')),
    'Covering Strike': ('Knight', 'Uncommon', 1, 'shared', {'damage': 8, 'shield': 5},
                        lambda n: f"Deal {n['damage']} damage. Allies next to you gain {n['shield']} Shield."),
    'Fall In': ('Knight', 'Uncommon', 1, 'move', {'shield': 5, 'draw': 0, 'cost': 1},
                lambda n: f"Move up to 2 tiles. Then you and heroes next to you gain {n['shield']} Shield." + (' Draw 1.' if n['draw'] else '')),
    'Phalanx': ('Knight', 'Uncommon', 1, 'shared', {'block': 2},
                lambda n: f"You and allies next to you gain {n['block']} Block."),
    "Guardian's Stance": ('Knight', 'Uncommon', 1, 'bonus', {'block': 2, 'bonus': 2},
                          lambda n: f"Gain {n['block']} Block. Next to another hero: gain {n['block'] + n['bonus']} instead."),
    'Hold the Line': ('Knight', 'Rare', 2, 'shared', {'shield': 10},
                      lambda n: f"You and allies next to you gain {n['shield']} Shield. Draw 1."),
    "Vanguard's Oath": ('Knight', 'Rare', 1, 'discount', {'damage': 10, 'drawalways': 0},
                        lambda n: f"Deal {n['damage']} damage. Draw 1. Costs 0 next to another hero." if n['drawalways'] else f"Deal {n['damage']} damage. Next to another hero: costs 0 and draw 1."),
    'Laying On of Hands': ('Cleric', 'Uncommon', 1, 'shared', {'heal': 5},
                           lambda n: f"Heal {n['heal']} to you and allies next to you."),
    'Guiding Smite': ('Cleric', 'Uncommon', 1, 'shared', {'damage': 6, 'heal': 3},
                      lambda n: f"Deal {n['damage']} damage. You and allies next to you heal {n['heal']}."),
    'Sanctuary Step': ('Cleric', 'Uncommon', 1, 'move', {'heal': 4, 'draw': 0, 'cost': 1},
                       lambda n: f"Move up to 2 tiles. Then you and heroes next to you heal {n['heal']}." + (' Draw 1.' if n['draw'] else '')),
    'Shield of Faith': ('Cleric', 'Uncommon', 1, 'bonus', {'shield': 5, 'bonus': 4, 'draw': 0},
                        lambda n: f"Give {n['shield']} Shield. Next to another hero: give {n['shield'] + n['bonus']}" + (' and draw 1.' if n['draw'] else ' instead.')),
    'Close Prayer': ('Cleric', 'Uncommon', 1, 'bonus', {'heal': 5},
                     lambda n: f"Heal {n['heal']}. Next to another hero: draw 1."),
    'Benediction': ('Cleric', 'Uncommon', 1, 'shared', {'shield': 4, 'draw': 0, 'heal': 0},
                    lambda n: (f"Heal {n['heal']} to an ally and every ally next to them." if n['heal'] else f"Give {n['shield']} Shield to an ally and every ally next to them.") + (' Draw 1.' if n['draw'] else '')),
    'Smite Together': ('Cleric', 'Uncommon', 1, 'bonus', {'damage': 6, 'bonus': 3},
                       lambda n: f"Deal {n['damage']} damage. Heal 3. Next to another hero: deal {n['damage'] + n['bonus']} instead."),
    'Sanctified Ground': ('Cleric', 'Rare', 2, 'shared', {'shield': 6, 'heal': 6, 'cost': 2},
                          lambda n: f"You and allies next to you gain {n['shield']} Shield and heal {n['heal']}."),
    'Rally the Line': ('Knight', 'Rare', 2, 'discount', {},
                       lambda n: "Give 6 Shield to you and allies within 2. Draw 1. Costs 1 next to another hero."),
    'Communion': ('Cleric', 'Rare', 2, 'discount', {},
                  lambda n: "Heal 5 to you and allies within 2. Draw 1. Costs 1 next to another hero."),
}

# The earlier lab cards that can be retuned this lab: name -> (base text with {v}, base value, key)
L4_TUNABLE = {
    'Totem Strike': ("Deal {v} damage. Draw 1. Costs 0 next to a totem.", 9),
    'Brothers in Arms': ("Gain {v} Shield. Draw 1. Costs 0 next to another hero.", 7),
    'Blessed Shield': ("Give {v} Shield.", 8),
    'Shrine Blessing': ("Give {v} Shield. Draw 1. Costs 0 next to a totem.", 6),
}


def numbers(round_name):
    rounds = json.load(open(os.path.join(HERE, 'lab3-numbers.json')))
    return rounds.get(round_name, {})


def text(card, round_name):
    nums = numbers(round_name)
    if card in CARDS:
        hero, rarity, cost, kind, defaults, fn = CARDS[card]
        n = {k: nums.get(f'{card}.{k}', v) for k, v in defaults.items()}
        return fn(n)
    if card in L4_TUNABLE:
        t, base = L4_TUNABLE[card]
        return t.format(v=base + nums.get(f'{card}.plus', 0))
    return None


def cost(card, round_name):
    hero, rarity, base, kind, defaults, fn = CARDS[card]
    return numbers(round_name).get(f'{card}.cost', base)
