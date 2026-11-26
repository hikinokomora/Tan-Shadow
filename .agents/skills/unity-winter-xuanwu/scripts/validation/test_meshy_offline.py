import importlib.util, json, tempfile, unittest, tomllib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('stage',ROOT/'scripts/asset-processing/stage_meshy_fbx.py'); m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class TestMeshy(unittest.TestCase):
    def test_config(self):
        for p in (ROOT/'templates').glob('meshy-*.toml.example'):
            d=tomllib.loads(p.read_text())['mcp_servers']['meshy']; self.assertEqual(d['env_vars'],['MESHY_API_KEY']);self.assertNotIn('env',d);self.assertIn('@meshy-ai/meshy-mcp-server',d['args'])
        b=json.loads((ROOT/'templates/meshy-budget.json').read_text());self.assertFalse(b['authorized_by_user']);self.assertEqual(b['max_credits'],0);self.assertFalse(b['allow_purchase'])
    def test_qa(self):
        d=json.loads((ROOT/'templates/qa-results.json').read_text());self.assertEqual([x['id'] for x in d['results']],[f'QA{i:02}' for i in range(1,26)])
    def test_staging(self):
        with tempfile.TemporaryDirectory() as td:
            p=Path(td);(p/'Assets').mkdir();(p/'Packages').mkdir();(p/'ProjectSettings').mkdir();(p/'Packages/manifest.json').write_text('{}');(p/'ProjectSettings/ProjectVersion.txt').write_text('test');f=p/'test.fbx';f.write_bytes(b'Kaydara FBX Binary  \x00\x1a\x00'+b'\x00'*50)
            r=m.stage(p,f,'MESHY_001'); dest=Path(r['destination']);self.assertFalse(dest.exists());self.assertEqual(r['status'],'READY');self.assertEqual(m.stage(p,f,'MESHY_001',True)['status'],'STAGED');self.assertEqual(m.stage(p,f,'MESHY_001',True)['status'],'ALREADY_STAGED');f.write_bytes(f.read_bytes()+b'changed');self.assertRaises(FileExistsError,m.stage,p,f,'MESHY_001',True);self.assertRaises(ValueError,m.stage,p,f,'../bad');f.write_bytes(b'<html>'+b'x'*100);self.assertRaises(ValueError,m.stage,p,f,'MESHY_002')
if __name__=='__main__':unittest.main(verbosity=2)
