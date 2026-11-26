import unittest, importlib.util, tempfile, csv,json
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
def mod(n,p):
 spec=importlib.util.spec_from_file_location(n,p);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
catalog=mod('catalog',ROOT/'scripts/asset-processing/catalog.py');recolor=mod('recolor',ROOT/'scripts/asset-processing/recolor.py')
class Offline(unittest.TestCase):
 def test_catalog_consistency(self):
  rows=catalog.load();self.assertGreaterEqual(len(rows),150)
  with (ROOT/'assets/ASSET_DATABASE.csv').open(encoding='utf-8-sig') as f: csvrows=list(csv.DictReader(f))
  self.assertEqual([r['id'] for r in rows],[r['id'] for r in csvrows]);self.assertEqual([r['url'] for r in rows],[r['url'] for r in csvrows]);self.assertEqual(len(rows),(ROOT/'assets/ASSET_ATLAS.html').read_text().count('<article '))
 def test_gate(self):
  rows=catalog.load();self.assertTrue(all(x['gate']=='excluded' for x in rows if x['ai_status']=='no'));self.assertFalse(any(x['gate']=='approved' for x in rows))
 def test_warm_recolor(self):
  a=np.array([[[154,108,70,77],[230,190,150,255]]],dtype=np.uint8);b=recolor.transform(a,'K2');self.assertTrue(np.all(b[...,:3].mean(-1)<a[...,:3].mean(-1)));self.assertTrue(np.all(b[...,2]>b[...,0]));self.assertTrue(np.array_equal(b[...,3],a[...,3]))
 def test_all_profiles(self):
  a=np.array([[[0,0,0,0],[255,255,255,255],[35,90,42,12]]],dtype=np.uint8)
  for profile in ('K1','K2','K3','K4','K5'):
   b=recolor.transform(a,profile);self.assertEqual(b.dtype,np.uint8);self.assertTrue(np.array_equal(b[...,3],a[...,3]));self.assertEqual(b.shape,a.shape)
 def test_json_templates(self):
  for p in (ROOT/'templates').glob('*.json'):json.loads(p.read_text())
 def test_all_legacy_urls(self):
  import re
  r={x['url'].lower() for x in catalog.load()}
  for u in re.findall(r'https?://[^)\s|]+',(ROOT/'legacy/ASSET_CATALOG.md').read_text()):
   if '/listings/' in u or 'polyhaven.com/a/' in u or 'ambientcg.com/view' in u:
    self.assertIn((u if 'ambientcg.com/view' in u else u.split('?')[0]).lower(),r)
if __name__=='__main__':unittest.main(verbosity=2)
