"""The lab report: one template for every Spire Lab, built on Card Lab IV's page.

    path = page(lab,
        lede='Twelve "alone" cards ...',
        tiles=[('9,400', 'bot runs'), (est(0.21, 0.06, sign=True), 'alone was worth, levels')],
        verdict=['Alone cards work at distance 3 ...'],                  # 1-2 short paragraphs
        decisions=['Build Lone Stand and Iron Will (handoff: Docs/SpireLab08-Handoff.md)', ...],
        assumptions=['Read "2 spaces away" as Chebyshev distance 2 ...', ...],
        sections=[section('distance', 'Distance 2, 3, 4', paras([...]), table(...), dots(...))],
        method=['Ratings are ...'])

The opening block - verdict, decisions waiting for you, assumptions made while you were away - is required:
page() raises if any of the three is missing. An empty decisions or assumptions list must say why
(no_decisions= / no_assumptions=), so "none" is a stated fact, never an omission. The method section gets
the freeze, builds, batches and seed blocks appended from lab.json and the ledger automatically.

Prose arguments are HTML; table cells are escaped unless wrapped in Raw (est() and chip() return Raw).
"""
import html
import math
import os

from .paths import STYLE

E = html.escape


class Raw(str):
    """Already-HTML: not escaped by table()."""


def fmt(x, d=2, sign=False):
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return '–'
    s = f'{x:+.{d}f}' if sign else f'{x:.{d}f}'
    if float(s) == 0:
        s = s.lstrip('+-')
    return s.replace('-', '−')


def est(m, se, d=2, sign=False):
    """'1.23 ±0.05' with the error muted."""
    if m is None:
        return Raw('–')
    return Raw(f'{fmt(m, d, sign)}<span class="muted"> ±{se:.{d}f}</span>' if se is not None else fmt(m, d, sign))


def chip(text, kind='in-line'):
    """kind: underpriced (hot), overpriced (cold), in-line (muted), existing (green), new-mech (orange)."""
    return Raw(f'<span class="chip {kind}">{E(text)}</span>')


def paras(items):
    return ''.join(f'<p>{x}</p>' for x in items)


def table(head, rows, note=None, num=None):
    """head: column titles; rows: lists of cells. Numeric cells (and columns in `num`) align right."""
    num = set(num or [])
    out = ['<div class="table-wrap"><table><thead><tr>']
    for i, h in enumerate(head):
        out.append(f'<th class="num">{E(h)}</th>' if i in num else f'<th>{E(h)}</th>')
    out.append('</tr></thead><tbody>')
    for row in rows:
        out.append('<tr>')
        for i, c in enumerate(row):
            is_num = i in num or isinstance(c, (int, float))
            text = c if isinstance(c, Raw) else E(fmt(c) if isinstance(c, float) else str(c))
            out.append(f'<td class="num">{text}</td>' if is_num else f'<td>{text}</td>')
        out.append('</tr>')
    out.append('</tbody></table></div>')
    if note:
        out.append(f'<p class="muted" style="font-size:.9rem">{note}</p>')
    return ''.join(out)


def cards(items):
    """items: dicts with name, meta, text, and optional rating (HTML), was (older text), why."""
    out = ['<div class="card-grid">']
    for c in items:
        out.append(f'<div class="card"><div class="name">{E(c["name"])}</div><div class="meta">{E(c.get("meta", ""))}</div>'
                   f'<div class="text">{E(c.get("text", ""))}</div>'
                   + (f'<div class="muted" style="font-size:.9rem">Started as: {E(c["was"])}</div>' if c.get('was') else '')
                   + (f'<div>{c["rating"]}</div>' if c.get('rating') else '')
                   + (f'<div class="why">{c["why"]}</div>' if c.get('why') else '') + '</div>')
    out.append('</div>')
    return ''.join(out)


def dots(rows, series, band=None, lo=None, hi=None, caption=None, unit='levels'):
    """Dot-and-whisker chart. rows: [(label, {series: (value, se)})]; series: [(name, css colour)] e.g.
    [('distance 2', 'var(--s1)')]. band: (low, high) shaded target."""
    vals = [v for _, d in rows for v, s in d.values() if v is not None]
    errs = [s or 0 for _, d in rows for _, s in d.values()]
    lo = lo if lo is not None else min([0] + [v - e for v, e in zip(vals, errs)]) - 0.1
    hi = hi if hi is not None else max([0] + [v + e for v, e in zip(vals, errs)]) + 0.1
    W, left, right, rowh, top = 640, 150, 20, 18 + 8 * len(series), 26
    H = top + rowh * len(rows) + 26
    x = lambda v: left + (min(hi, max(lo, v)) - lo) / (hi - lo) * (W - left - right)
    step = 0.25 if hi - lo <= 2 else 0.5 if hi - lo <= 4 else 1.0
    ticks = [round(t * step, 2) for t in range(math.ceil(lo / step), math.floor(hi / step) + 1)]
    p = [f'<svg class="chart" viewBox="0 0 {W} {H}" role="img">']
    if band:
        p.append(f'<rect x="{x(band[0]):.1f}" y="{top - 8}" width="{x(band[1]) - x(band[0]):.1f}" '
                 f'height="{H - 14 - top}" fill="var(--accent-wash)"/>')
    for t in ticks:
        p.append(f'<line class="{"base" if t == 0 else "grid"}" x1="{x(t):.1f}" x2="{x(t):.1f}" y1="{top - 8}" y2="{H - 22}"/>')
        p.append(f'<text class="tick" x="{x(t):.1f}" y="{H - 6}" text-anchor="middle">{fmt(t, 2 if step < 0.5 else 1)}</text>')
    for i, (label, d) in enumerate(rows):
        y = top + i * rowh + rowh / 2
        p.append(f'<text class="cat" x="{left - 10}" y="{y + 4:.1f}" text-anchor="end">{E(label)}</text>')
        for j, (name, colour) in enumerate(series):
            if name not in d or d[name][0] is None:
                continue
            v, s = d[name]
            yy = y + (j - (len(series) - 1) / 2) * 7
            if s:
                p.append(f'<line class="whisker" style="stroke:{colour}" x1="{x(v - s):.1f}" x2="{x(v + s):.1f}" '
                         f'y1="{yy:.1f}" y2="{yy:.1f}" opacity="0.55"/>')
            p.append(f'<circle cx="{x(v):.1f}" cy="{yy:.1f}" r="4.5" fill="{colour}" stroke="var(--surface)" '
                     f'stroke-width="1.5"><title>{E(label)}, {E(name)}: {fmt(v, 2, True)}'
                     + (f' ± {s:.2f}' if s else '') + f' {unit}</title></circle>')
    p.append('</svg>')
    legend = ('<div class="legend">' + ''.join(f'<span style="--c:{c}">{E(n)}</span>' for n, c in series)
              + ('<span style="--c:var(--accent-wash)">target band</span>' if band else '') + '</div>')
    cap = f'<figcaption>{caption}</figcaption>' if caption else ''
    return f'<figure class="figure">{legend}{"".join(p)}{cap}</figure>'


def section(sid, title, *blocks, sub=None):
    return {'id': sid, 'title': title,
            'html': (f'<p class="sub">{sub}</p>' if sub else '') + ''.join(blocks)}


def _facts(lab):
    m, led = lab.meta, lab.ledger()
    fz = m.get('freeze') or {}
    batches = [b for b in led.get('batches', []) if b.get('complete')]
    out = []
    if fz:
        out.append(('Frozen', f"{fz['at']} from {E(fz['mode'])} at commit <code>{E(fz['commit'])}</code>"
                    + (f", {len(fz.get('uncommitted', []))} uncommitted file(s) "
                       + ('included' if fz.get('uncommittedIncluded') else 'left out')
                       + f" (diff sha256 <code>{E(fz['patchSha256'])}</code>)" if fz.get('uncommitted') else '')))
    builds = [b for b in led.get('builds', []) if b.get('ok')]
    if builds:
        out.append(('Bot players', ', '.join(E(b['name']) for b in builds)))
    if batches:
        out.append(('Batches', f"{len(batches)} finished, {sum(b['runs'] for b in batches):,} runs"))
    if led.get('seedBlocks'):
        out.append(('Seed blocks', ', '.join(f'{E(k)} {v}' for k, v in led['seedBlocks'].items())))
    return out


def page(lab, *, lede, tiles, verdict, decisions, assumptions, sections, method, date=None,
         no_decisions=None, no_assumptions=None, index_url=None):
    if not verdict or len(verdict) > 2:
        raise ValueError('verdict: one or two short paragraphs, required')
    if not decisions and not no_decisions:
        raise ValueError('decisions waiting for the user: list them, or say why there are none (no_decisions=)')
    if not assumptions and not no_assumptions:
        raise ValueError('assumptions made while the user was away: list them, or say why there are none')
    if not method:
        raise ValueError('method: required')
    title = lab.meta['title']
    date = date or lab.meta.get('deadline', '')[:10]
    toc = [('verdict', 'Verdict'), ('decisions', 'Waiting for you'), ('assumptions', 'Assumptions')] + \
          [(s['id'], s['title']) for s in sections] + [('method', 'Method')]
    dec = f'<ol>{"".join(f"<li>{d}</li>" for d in decisions)}</ol>' if decisions else f'<p>{no_decisions}</p>'
    asm = f'<ul>{"".join(f"<li>{a}</li>" for a in assumptions)}</ul>' if assumptions else f'<p>{no_assumptions}</p>'
    facts = ''.join(f'<dt>{k}</dt><dd>{v}</dd>' for k, v in _facts(lab))
    earlier = f'<p><a href="{E(index_url)}">Every lab so far</a></p>' if index_url else ''
    body = f'''
<div class="page">
<nav class="toc" aria-label="Contents"><div class="toc-title">Contents</div><ol>{''.join(f'<li><a href="#{a}">{E(b)}</a></li>' for a, b in toc)}</ol></nav>
<main>
<header class="hero">
  <div class="eyebrow">Escape the Spire · balance bot · {E(date)}</div>
  <h1>{E(title)}</h1>
  <p class="lede">{lede}</p>
  <div class="tiles">{''.join(f'<div class="tile"><div class="v">{v}</div><div class="k">{E(k)}</div></div>' for v, k in tiles)}</div>
  <div class="opening">
    <div class="panel verdict" id="verdict"><h2>Verdict</h2>{paras(verdict)}</div>
    <div class="panel decide" id="decisions"><h2>Waiting for you</h2>{dec}</div>
    <div class="panel" id="assumptions"><h2>Assumptions I made</h2>{asm}</div>
  </div>
</header>
{''.join(f'<section id="{s["id"]}"><h2>{E(s["title"])}</h2>{s["html"]}</section>' for s in sections)}
<section id="method"><h2>Method</h2>{paras(method)}<dl class="facts">{facts}</dl>{earlier}</section>
</main></div>'''
    with open(STYLE, encoding='utf-8') as f:
        style = f.read()
    path = os.path.join(lab.data_dir, 'report.html')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(f'<title>{E(title)}</title>\n{style}\n{body}')
    return path
