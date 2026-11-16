"""Vendor immutable test fixtures; package consumers need no sibling checkout."""
import argparse,hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--interop',type=Path,default=ROOT.parent/'SpatialFabric-Interop');p.add_argument('--check',action='store_true');args=p.parse_args()
    target=ROOT/'Packages/com.nplaudio.spatialfabric/Tests/EditMode/Fixtures'
    sources=['Fixtures/osc-v1.json','Fixtures/coordinates-v1.json','Fixtures/custom-v1.json','Schemas/scene-v1.schema.json']
    hashes={}
    for name in sources:
        data=(args.interop/name).read_bytes();dest=target/Path(name).name;hashes[name]=hashlib.sha256(data).hexdigest()
        if args.check:
            if not dest.exists() or dest.read_bytes()!=data:raise SystemExit('Fixture differs: '+str(dest))
        else:dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
    manifest=json.dumps({'contract':'SpatialFabric interoperability v1','source':'SpatialFabric-Interop','sha256':hashes},indent=2)+'\n'
    path=target/'PROVENANCE.json'
    if args.check:
        if path.read_text()!=manifest:raise SystemExit('Provenance differs')
    else:path.write_text(manifest,encoding='utf-8')
    print('Interop fixtures verified' if args.check else 'Interop fixtures vendored with SHA-256 provenance')
if __name__=='__main__':main()
