# Free Fantasy Skill Icon 技能数据

此文件夹包含 Free Fantasy Skill Icon 资源包使用的技能说明数据。

## 文件

- `Free_FantasySkillIcon_Skills_Index.json` - 适合 Unity 读取的包装对象，内部包含 `items` 数组。
- `Free_FantasySkillIcon_Skills_Array.json` - 轻量运行时结构的纯 JSON 数组。
- `Free_FantasySkillIcon_Skills_Detailed.json` - 完整制作元数据，包含技能说明、生成提示词、来源类型，以及可用的简化图标生成提示词。

## 运行时条目格式

```json
{
    "id":  "skill_assassin_01_silence_sever",
    "name":  "Silence Sever",
    "category":  "assassin",
    "icon":  "SkillIcon/assassin/01_silence_sever.png",
    "simplifiedIcon":  "SkillIcon_Simplified/assassin/01_silence_sever.png"
}
```

`icon` 是相对于 `Assets/Free_FantasySkillIcon/` 的路径。需要完整 Unity 资源路径时，请加上该基础路径。当前包中没有对应简化图标时，`simplifiedIcon` 为 `null`。

## 数量

| 职业 | 原始图标 | 简化图标 | 缺少的简化图标 |
| --- | ---: | ---: | ---: |
| assassin | 50 | 50 | 0 |
| cleric | 50 | 50 | 0 |

Battlemage 已从资源包中删除，因此在本数据中也被排除。
