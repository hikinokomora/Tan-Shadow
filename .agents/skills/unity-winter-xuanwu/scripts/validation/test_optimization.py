import unittest, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def load(n): return json.loads((ROOT/'templates'/n).read_text())
class Optimization(unittest.TestCase):
 def test_role_budgets(self):
  b=load('optimization-budgets.json'); roles=b['roles']; lib=load('meshy-prompt-library.json')['items']; self.assertEqual(set(roles),{x['id'] for x in lib});self.assertFalse(b['generated_texture_default']);self.assertFalse(b['premium_model_default']);self.assertTrue(b['exceptions_require_user_approval'])
  for x in lib:
   r=roles[x['id']];self.assertTrue(0<r['low_target']<=r['default_target']<=r['review_cap']);self.assertEqual(x['target_triangle_default'],r['default_target']);self.assertEqual(x['review_cap'],r['review_cap']);self.assertIn('no microgeometry',x['prompt_en']);self.assertLessEqual(len(x['prompt_en']),800)
 def test_report_safe_defaults(self):
  d=load('asset-optimization-report.json'); self.assertFalse(d['records']);self.assertEqual(d['default_status'],'NOT RUN');self.assertIn('actual_triangles_by_lod',d['record_fields']);self.assertFalse(load('meshy-budget.json')['authorized_by_user']);self.assertEqual(load('meshy-budget.json')['max_credits'],0)
 def test_required_docs(self):
  for p in ('SKILL.md','references/BUILD_GUIDE.md','references/MESHY_WORKFLOW.md','references/MESHY_PROMPT_GUIDE.md','references/PERFORMANCE_GUIDE.md','references/QUALITY_CHECKLIST.md'):
   text=(ROOT/p).read_text();self.assertTrue('v4.4' in text,p)
  text=(next(p for p in (ROOT.parent/'INSTALL_FOR_AI.md',ROOT.parents[2]/'Docs/Xuanwu/INSTALL_FOR_AI.md') if p.is_file())).read_text();self.assertIn('немедленно работать с готовым Graybox',text);self.assertIn('Xuanwu_Complete_v4_4_Optimization_First.zip',text)
