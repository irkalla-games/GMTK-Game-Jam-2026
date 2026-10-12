"""Card lab 4: each lab card's hero, rarity, cost and text for a given numbers dict (mirrors lab4_isolation.cs.txt)."""


def N(nums, key, d):
    return int(nums.get(key, d))


def cards(nums=None, alone=None):
    n = nums or {}
    g = lambda k, d: N(n, k, d)
    C = {}

    def add(name, hero, kind, rarity, cost, text):
        C[name] = dict(hero=hero, kind=kind, rarity=rarity, cost=cost, text=text)

    add('Challenge', 'Knight', 'taunt', 'Uncommon', 1, f"Taunt an enemy. Gain {g('Challenge.shield', 7)} Shield.")
    add('Provoke', 'Knight', 'taunt', 'Uncommon', 1, f"Taunt every enemy within 2. Gain {g('Provoke.block', 2)} Block.")
    add('Taunting Blow', 'Knight', 'taunt', 'Uncommon', 1, f"Deal {g('Taunting Blow.damage', 8)} damage. Taunt the target.")
    add('Defiant Roar', 'Knight', 'taunt', 'Rare', g('Defiant Roar.cost', 2),
        f"Taunt every enemy within 2. Gain {g('Defiant Roar.shield', 12)} Shield. Draw 1.")
    s, b = g('Lone Stand.shield', 6), g('Lone Stand.bonus', 6)
    add('Lone Stand', 'Knight', 'alone', 'Uncommon', 1, f"Gain {s} Shield. Alone: gain {s + b} instead.")
    s, b = g('Solitary Guard.block', 2), g('Solitary Guard.bonus', 2)
    add('Solitary Guard', 'Knight', 'alone', 'Uncommon', 1, f"Gain {s} Block. Alone: gain {s + b} instead.")
    s, b = g('Lone Wolf.damage', 8), g('Lone Wolf.bonus', 5)
    add('Lone Wolf', 'Knight', 'alone', 'Uncommon', 1,
        f"Deal {s} damage. Alone: deal {s + b}" + (" and draw 1." if g('Lone Wolf.draw', 0) else " instead."))
    s, b = g('Last Bastion.shield', 6), g('Last Bastion.bonus', 6)
    add('Last Bastion', 'Knight', 'alone', 'Rare', 1, f"Taunt every enemy within 2. Gain {s} Shield. Alone: gain {s + b} and draw 1.")
    add('Outrider', 'Knight', 'alone', 'Uncommon', g('Outrider.cost', 0),
        f"Move up to 2 tiles. Then, if alone, gain {g('Outrider.shield', 6)} Shield and draw 1.")
    add('Iron Will', 'Knight', 'alone', 'Uncommon', 1, f"Gain {g('Iron Will.shield', 7)} Shield. Draw 1. Costs 0 when alone.")
    s, b = g("Hermit's Smite.damage", 7), g("Hermit's Smite.bonus", 4)
    add("Hermit's Smite", 'Cleric', 'alone', 'Uncommon', 1, f"Deal {s} damage. Heal 3. Alone: deal {s + b} instead.")
    s, b = g('Distant Grace.shield', 6), g('Distant Grace.bonus', 4)
    add('Distant Grace', 'Cleric', 'alone', 'Uncommon', 1, f"Give {s} Shield. Alone: give {s + b} instead.")
    add('Far Mend', 'Cleric', 'alone', 'Uncommon', 1, f"Heal {g('Far Mend.heal', 6)}. Alone: draw 1.")
    add('Withdraw', 'Cleric', 'alone', 'Uncommon', g('Withdraw.cost', 0),
        f"Move up to 2 tiles. Then, if alone, heal {g('Withdraw.heal', 4)} and draw 1.")
    add('Vigil', 'Cleric', 'alone', 'Rare', 2, f"Heal {g('Vigil.heal', 10)}. Draw 1. Costs 1 when alone.")
    add('Judgement', 'Cleric', 'alone', 'Rare', 1, f"Deal {g('Judgement.damage', 10)} damage. Alone: costs 0 and draw 1.")
    return C


# How each alone card's payoff is shaped, for the report.
SHAPE = {
    'Lone Stand': 'bigger number', 'Solitary Guard': 'bigger number', 'Lone Wolf': 'bigger number',
    "Hermit's Smite": 'bigger number', 'Distant Grace': 'bigger number',
    'Last Bastion': 'bigger number + draw', 'Far Mend': 'draw', 'Judgement': 'cheaper + draw',
    'Iron Will': 'cheaper', 'Vigil': 'cheaper', 'Outrider': 'after a move', 'Withdraw': 'after a move',
}
