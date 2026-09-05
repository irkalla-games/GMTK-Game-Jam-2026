# Free Fantasy Skill Icon 스킬 데이터

이 폴더에는 Free Fantasy Skill Icon 패키지용 스킬 설명 자료가 들어 있습니다.

## 파일

- `Free_FantasySkillIcon_Skills_Index.json` - Unity에서 읽기 쉬운 `items` 배열 포함 wrapper 형식입니다.
- `Free_FantasySkillIcon_Skills_Array.json` - 런타임에서 바로 쓰기 좋은 가벼운 JSON 배열입니다.
- `Free_FantasySkillIcon_Skills_Detailed.json` - 스킬 설명, 생성 프롬프트, 소스 종류, 사용 가능한 심플 아이콘 생성 프롬프트까지 포함한 상세 메타데이터입니다.

## 런타임 아이템 형식

```json
{
    "id":  "skill_assassin_01_silence_sever",
    "name":  "Silence Sever",
    "category":  "assassin",
    "icon":  "SkillIcon/assassin/01_silence_sever.png",
    "simplifiedIcon":  "SkillIcon_Simplified/assassin/01_silence_sever.png"
}
```

`icon`은 `Assets/Free_FantasySkillIcon/` 기준 상대 경로입니다. Unity 전체 에셋 경로가 필요하면 앞에 해당 기준 경로를 붙이면 됩니다. 현재 패키지에 심플 버전이 없으면 `simplifiedIcon`은 `null`입니다.

## 수량

| 클래스 | 원본 아이콘 | 심플 아이콘 | 심플 누락 |
| --- | ---: | ---: | ---: |
| assassin | 50 | 50 | 0 |
| cleric | 50 | 50 | 0 |

Battlemage는 패키지에서 삭제된 직업이라 이 데이터에서도 제외했습니다.
