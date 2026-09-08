# Free Fantasy Skill Icon スキルデータ

このフォルダには、Free Fantasy Skill Icon パッケージ用のスキル説明データが含まれています。

## ファイル

- `Free_FantasySkillIcon_Skills_Index.json` - Unity で読みやすい `items` 配列つきのラッパー形式です。
- `Free_FantasySkillIcon_Skills_Array.json` - 軽量な実行時形式の JSON 配列です。
- `Free_FantasySkillIcon_Skills_Detailed.json` - スキル説明、生成プロンプト、ソース種別、利用可能な簡略版アイコン生成プロンプトを含む詳細メタデータです。

## 実行時アイテム形式

```json
{
    "id":  "skill_assassin_01_silence_sever",
    "name":  "Silence Sever",
    "category":  "assassin",
    "icon":  "SkillIcon/assassin/01_silence_sever.png",
    "simplifiedIcon":  "SkillIcon_Simplified/assassin/01_silence_sever.png"
}
```

`icon` は `Assets/Free_FantasySkillIcon/` からの相対パスです。完全な Unity アセットパスが必要な場合は、そのベースパスを付けてください。現在のパッケージに簡略版がない場合、`simplifiedIcon` は `null` になります。

## 数量

| クラス | 元アイコン | 簡略版アイコン | 簡略版なし |
| --- | ---: | ---: | ---: |
| assassin | 50 | 50 | 0 |
| cleric | 50 | 50 | 0 |

Battlemage はパッケージから削除されたため、このデータからも除外されています。
