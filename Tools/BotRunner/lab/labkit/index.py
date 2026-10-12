"""The index page: every lab, with its headline numbers over time.

Reads each Tools/BotRunner/lab/labs/NN-slug/lab.json:
    title, date, url, tested (one line), headline: {"kcNormal": [mean, se], "kcHard3": [mean, se], "note": "..."}
and writes BotRuns/labs/index.html. The published page's URL lives in Tools/BotRunner/lab/index.json; republish
to that same URL after every lab, so the link never changes.
"""
import html
import json
import os

from .lab import Lab
from .paths import STYLE, LABS_DATA, INDEX_JSON
from .report import est, dots

E = html.escape


def index_url():
    if os.path.exists(INDEX_JSON):
        with open(INDEX_JSON, encoding='utf-8') as f:
            return json.load(f).get('url')
    return None


def set_index_url(url):
    with open(INDEX_JSON, 'w', encoding='utf-8') as f:
        json.dump({'url': url}, f, indent=1)


def build():
    labs = [lab.meta for lab in Lab.all()]
    rows = []
    for m in labs:
        h = m.get('headline') or {}
        cell = lambda k: est(*h[k]) if h.get(k) else '–'
        title = f'<a href="{E(m["url"])}">{E(m["title"])}</a>' if m.get('url') else E(m['title'])
        rows.append(f'<tr><td>{m["number"]:02d}</td><td>{title}</td><td class="num">{E(m.get("date") or m.get("created", "")[:10])}</td>'
                    f'<td>{E(m.get("tested", ""))}</td><td class="num">{cell("kcNormal")}</td><td class="num">{cell("kcHard3")}</td>'
                    f'<td>{E(h.get("note", ""))}</td></tr>')
    chart_rows = [(f'{m["number"]:02d}', {'Normal': tuple(m['headline']['kcNormal']) if (m.get('headline') or {}).get('kcNormal') else (None, None),
                                         'Hard III': tuple(m['headline']['kcHard3']) if (m.get('headline') or {}).get('kcHard3') else (None, None)})
                  for m in labs if (m.get('headline') or {}).get('kcNormal') or (m.get('headline') or {}).get('kcHard3')]
    chart = dots(chart_rows, [('Normal', 'var(--s1)'), ('Hard III', 'var(--t3)')], lo=0, hi=10,
                 caption='Levels cleared per run (of 10), Knight + Cleric, the headline arm of each lab, ± one standard error. '
                         'Content and bot change between labs, so a step here is the lab\'s whole package, not one change.') if chart_rows else ''
    body = f'''
<div class="page" style="grid-template-columns:minmax(0,1fr)">
<main>
<header class="hero">
  <div class="eyebrow">Escape the Spire · balance bot</div>
  <h1>Spire Labs</h1>
  <p class="lede">Every balance lab so far, newest last. Each title opens that lab's full report.</p>
</header>
<section><div class="table-wrap"><table class="index-table"><thead><tr><th>#</th><th>Lab</th><th class="num">Date</th><th>What it tested</th>
<th class="num">K+C Normal</th><th class="num">K+C Hard III</th><th>Headline</th></tr></thead><tbody>{"".join(rows)}</tbody></table></div>
<div style="max-width:760px">{chart}</div></section>
</main></div>'''
    with open(STYLE, encoding='utf-8') as f:
        style = f.read()
    os.makedirs(LABS_DATA, exist_ok=True)
    path = os.path.join(LABS_DATA, 'index.html')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(f'<title>Spire Labs</title>\n{style}\n{body}')
    return path
