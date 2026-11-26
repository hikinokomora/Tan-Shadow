"""Single source of truth. Python 3, standard library only. Rebuild/validate/filter."""
import argparse,csv,json,html,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]; A=ROOT/'assets'; DB=A/'ASSET_DATABASE.json'
def load():
 rows=json.loads(DB.read_text(encoding='utf-8')); ids=set(); urls=set()
 for r in rows:
  assert r['id'] not in ids and r['url'] not in urls, r['id'];ids.add(r['id']);urls.add(r['url'])
  assert r['url'].startswith('https://') and r['gate'] in ('verify_before_use','excluded','approved')
  assert r['ai_status']!='no' or r['gate']=='excluded'
  assert r['gate']!='approved' or r['free_status']=='listed_free'
 for r in rows: assert set(r['compatible_ids'])<=ids
 return rows
def rebuild(rows):
 keys=list(rows[0]); enc=lambda v:json.dumps(v,ensure_ascii=False) if isinstance(v,(list,dict)) else '' if v is None else v
 with (A/'ASSET_DATABASE.csv').open('w',encoding='utf-8-sig',newline='') as f:
  w=csv.DictWriter(f,fieldnames=keys);w.writeheader();w.writerows({k:enc(v) for k,v in r.items()} for r in rows)
 e=lambda v:html.escape(str(v or ''),quote=True)
 cards=[];md=['# Каталог v4 (генерируется из JSON)','',f'Всего {len(rows)} уникальных карточек; исключены {sum(r["gate"]=="excluded" for r in rows)}. Не являются одобренными для импорта. Изображения — только ссылки провайдера, при наличии. Совместимость по тегам — гипотеза, не тест.','', '| ID | Ассет | Тип / семейство | Доступ / AI | Обработка и роль |','|---|---|---|---|---|']
 for r in rows:
  desc=r['adaptation'];md.append(f"| {r['id']} | [{r['name']}]({r['url']}) | {r['kind']} / {r['family']} | {r['free_status']} / {r['ai_status']} / {r['gate']} | {desc.replace('|','/')} |")
  thumb=f'<img loading="lazy" src="{e(r["preview_url"])}" alt="Превью поставщика" onerror="this.hidden=true">' if r['preview_url'] else '<div class="placeholder">Превью на странице источника</div>'
  cards.append(f'<article data-kind="{e(r["kind"])}" data-gate="{e(r["gate"])}" data-family="{e(r["family"])}">{thumb}<h3>{e(r["id"])} · <a href="{e(r["url"])}">{e(r["name"])}</a></h3><small>{e(r["kind"])} · {e(r["family"])} · {e(r["priority"])} · {e(r["gate"])}</small><p>{e(desc)}</p><p>{e(r["free_status"])} / AI {e(r["ai_status"])} · {e(r["license"])}</p><p>{e(r["roles"])} · {e(r["size_m"])} · tris {e(r["triangles"]) or "?"}</p></article>')
 (ROOT/'references'/'ASSET_CATALOG.md').write_text('\n'.join(md)+'\n',encoding='utf-8')
 (A/'ASSET_COMPATIBILITY.json').write_text(json.dumps({'note':'Provisional candidates by family; verify visual match on test slice','rules':{'volcanic':'jagged basalt / slate; no lava hue','layered':'align bedding and texture scale','unclassified':'review before combining','snow':'same whiteness and roughness','ice':'local only'},'candidate_pairs':{r['id']:r['compatible_ids'] for r in rows}},ensure_ascii=False,indent=2),encoding='utf-8')
 (A/'ASSET_ATLAS.html').write_text('''<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Xuanwu Asset Atlas</title><style>body{font:16px system-ui;background:#111c26;color:#e7eef5;margin:24px}a{color:#a2cbee}header{position:sticky;top:0;background:#111c26;padding:12px 0}input,select{padding:8px;background:#223647;color:white;border:1px solid #617587}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:14px}article{background:#1d2b38;border:1px solid #3c5265;padding:14px;border-radius:12px}article[hidden]{display:none}img{width:100%;height:140px;object-fit:contain;background:#0e151c}.placeholder{height:64px;background:#263a48;padding:12px}small{color:#a7c6df}p{font-size:14px}article[data-gate=excluded]{opacity:.65}</style><header><h1>Сюаньу · каталог кандидатов v4</h1><p>Никакого автоматического допуска: цена, лицензия, AI и импорт проверяются отдельно. Превью Poly Haven загружаются по сети; остальные — по ссылке.</p><input id="q" placeholder="ID, роль, цвет, поставщик"><select id="k"><option value="">Все типы</option><option>mesh</option><option>PBR</option><option>pack</option></select><select id="g"><option value="">Все статусы</option><option>verify_before_use</option><option>excluded</option><option>approved</option></select> <span id="count"></span></header><main>'''+''.join(cards)+'''</main><script>const q=document.querySelector('#q'),k=document.querySelector('#k'),g=document.querySelector('#g'),a=[...document.querySelectorAll('article')];function filter(){let n=0;for(const x of a){x.hidden=!(x.textContent.toLowerCase().includes(q.value.toLowerCase())&&(!k.value||x.dataset.kind===k.value)&&(!g.value||x.dataset.gate===g.value));if(!x.hidden)n++}document.querySelector('#count').textContent=n+' / '+a.length}for(const x of [q,k,g])x.addEventListener('input',filter);filter()</script></html>''',encoding='utf-8')
def main():
 p=argparse.ArgumentParser();p.add_argument('--rebuild',action='store_true');p.add_argument('--role');p.add_argument('--family');p.add_argument('--kind');p.add_argument('--limit',type=int,default=8);p.add_argument('--include-excluded',action='store_true');a=p.parse_args();rows=load()
 if a.rebuild:rebuild(rows)
 else:
  rows=[r for r in rows if (a.include_excluded or r['gate']!='excluded') and (not a.role or a.role in r['roles']) and (not a.family or a.family==r['family']) and (not a.kind or a.kind==r['kind'])];rows.sort(key=lambda r:(r['priority'],r['id']));print(json.dumps([{k:r[k] for k in ('id','name','url','gate','free_status','ai_status','adaptation')} for r in rows[:max(0,a.limit)]],ensure_ascii=False,indent=2))
if __name__=='__main__':main()
