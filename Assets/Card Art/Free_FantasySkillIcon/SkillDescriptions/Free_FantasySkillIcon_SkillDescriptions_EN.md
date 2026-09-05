# Free Fantasy Skill Icon Skill Data

This folder contains the skill description data for the Free Fantasy Skill Icon package.

## Files

- `Free_FantasySkillIcon_Skills_Index.json` - Unity-friendly wrapper object with an `items` array.
- `Free_FantasySkillIcon_Skills_Array.json` - plain JSON array using the lightweight runtime shape.
- `Free_FantasySkillIcon_Skills_Detailed.json` - full production metadata, including skill descriptions, prompts, source kind, and simplified-icon generation prompt data when available.

## Runtime Item Shape

```json
{
    "id":  "skill_assassin_01_silence_sever",
    "name":  "Silence Sever",
    "category":  "assassin",
    "icon":  "SkillIcon/assassin/01_silence_sever.png",
    "simplifiedIcon":  "SkillIcon_Simplified/assassin/01_silence_sever.png"
}
```

`icon` is relative to `Assets/Free_FantasySkillIcon/`. Add that base path when you need a full Unity asset path. `simplifiedIcon` is `null` when that simplified version is not included in the current package.

## Counts

| Class | Original Icons | Simplified Icons | Missing Simplified |
| --- | ---: | ---: | ---: |
| assassin | 50 | 50 | 0 |
| cleric | 50 | 50 | 0 |

Battlemage is intentionally excluded because that class was removed from the package.
