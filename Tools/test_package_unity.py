import subprocess,unittest
from pathlib import Path
from unittest.mock import Mock,patch
import package_unity

class PackageProvenanceTests(unittest.TestCase):
    def provenance(self,status):
        root=Path('/source')
        commit='0123456789abcdef0123456789abcdef01234567'
        with patch.object(package_unity.subprocess,'run',side_effect=[Mock(stdout=str(root.resolve())+'\n'),Mock(stdout=commit+'\n'),Mock(stdout=status)]) as run:
            result=package_unity.source_provenance(root)
        self.assertEqual(result['source_commit'],commit)
        self.assertEqual(run.call_args_list[0].args[0],['git','rev-parse','--show-toplevel'])
        self.assertEqual(run.call_args_list[1].args[0],['git','rev-parse','--verify','HEAD'])
        self.assertEqual(run.call_args_list[2].args[0],['git','status','--porcelain=v1','--untracked-files=all','--ignore-submodules=none'])
        for call in run.call_args_list:
            self.assertEqual(call.kwargs,dict(cwd=root,check=True,capture_output=True,text=True))
        return result

    def test_clean_snapshot_records_exact_head(self):
        self.assertEqual(self.provenance('')['source_state'],'clean committed snapshot')

    def test_status_whitespace_is_clean(self):
        self.assertEqual(self.provenance('\n')['source_state'],'clean committed snapshot')

    def test_dirty_snapshot_records_base_head(self):
        for status in (' M file\n','M  file\n',' D file\n','?? new file\n'):
            with self.subTest(status=status):
                self.assertEqual(self.provenance(status)['source_state'],'local uncommitted development snapshot')

    def test_git_failure_never_claims_clean_snapshot(self):
        top=Mock(stdout=str(Path('/source').resolve())+'\n')
        failures=([FileNotFoundError('git')],
                  [subprocess.CalledProcessError(128,['git','rev-parse'])],
                  [top,subprocess.CalledProcessError(128,['git','rev-parse'])],
                  [top,Mock(stdout='0123456789abcdef0123456789abcdef01234567\n'),subprocess.CalledProcessError(128,['git','status'])])
        for results in failures:
            with self.subTest(results=results),patch.object(package_unity.subprocess,'run',side_effect=results):
                self.assertEqual(package_unity.source_provenance(Path('/source')),{'source_state':'unknown source state','source_commit':None})

    def test_parent_repository_is_not_source_provenance(self):
        with patch.object(package_unity.subprocess,'run',return_value=Mock(stdout=str(Path('/parent').resolve())+'\n')) as run:
            self.assertEqual(package_unity.source_provenance(Path('/parent/extracted')),{'source_state':'unknown source state','source_commit':None})
            self.assertEqual(run.call_count,1)

    def test_provenance_precedes_output_creation(self):
        class Inspected(Exception):pass
        with patch('sys.argv',['package_unity.py']),patch.object(package_unity,'source_provenance',side_effect=Inspected) as provenance,patch.object(Path,'mkdir') as mkdir:
            with self.assertRaises(Inspected):package_unity.main()
            provenance.assert_called_once_with(package_unity.ROOT)
            mkdir.assert_not_called()

if __name__=='__main__':unittest.main()
