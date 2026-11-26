import json, unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
class Prompts(unittest.TestCase):
 def test_library(self):
  items=json.loads((ROOT/'templates/meshy-prompt-library.json').read_text())['items']; self.assertEqual(len(items),14);self.assertEqual(len({a['id'] for a in items}),14)
  md=(ROOT/'references/MESHY_PROMPTS.md').read_text()
  for a in items:
   p=a['prompt_en'];self.assertTrue(p.startswith('Single '));self.assertLessEqual(len(p),800);self.assertIn(p,md);self.assertTrue(a['shape_acceptance_ru']);self.assertNotIn('texture_prompt',p)
 def test_shape_brief_not_authorized(self):
  d=json.loads((ROOT/'templates/meshy-shape-request.json').read_text());self.assertEqual(d['status'],'DRAFT_NOT_AUTHORIZED');self.assertIsNone(d['estimated_credits']);self.assertIsNone(d['prompt_en']);self.assertFalse(d['candidate_ids_and_shape_rejections'])
 def test_links(self):
  for name in ('MESHY_PROMPT_GUIDE.md','MESHY_PROMPTS.md'):
   self.assertTrue((ROOT/'references'/name).is_file());self.assertIn(name,(ROOT/'SKILL.md').read_text()+(ROOT/'references/MESHY_WORKFLOW.md').read_text());self.assertIn(name,(next(p for p in (ROOT.parent/'INSTALL_FOR_AI.md',ROOT.parents[2]/'Docs/Xuanwu/INSTALL_FOR_AI.md') if p.is_file())).read_text())
if __name__=='__main__':unittest.main(verbosity=2)
