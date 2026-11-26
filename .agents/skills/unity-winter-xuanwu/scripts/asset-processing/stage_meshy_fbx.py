"""Offline FBX staging helper; no network, no Unity invocation, no overwrite."""
import argparse, hashlib, json, re, shutil
from pathlib import Path


def plan(project, source, asset_id):
    project = Path(project).resolve(strict=True)
    source = Path(source).resolve(strict=True)
    if not all((project / p).exists() for p in ('Assets', 'Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt')):
        raise ValueError('Not a Unity project root')
    if not re.fullmatch(r'MESHY_[A-Za-z0-9_-]{1,64}', asset_id):
        raise ValueError('Invalid asset ID; use MESHY_001 or similar')
    if not source.is_file() or source.suffix.lower() != '.fbx':
        raise ValueError('Expected an existing FBX file')
    if source.is_relative_to(project / 'Assets'):
        raise ValueError('Source must be staged outside Assets')
    if not 32 <= source.stat().st_size <= 500 * 1024 * 1024:
        raise ValueError('File empty, truncated or larger than 500 MiB; review manually')
    with source.open('rb') as f:
        header = f.read(1024)
    if not (header.startswith(b'Kaydara FBX Binary  \x00\x1a\x00') or re.match(rb'\s*;\s*FBX\s+\d', header)):
        raise ValueError('Unrecognized FBX header; inspect or convert locally')
    dest = project / 'Assets' / 'XuanwuGenerated' / 'Meshy' / asset_id / 'Source' / 'model.fbx'
    for p in [dest,*dest.parents]:
        if p == project: break
        if p.is_symlink(): raise ValueError('Symlink destination not allowed')
    if not dest.resolve().is_relative_to(project / 'Assets'):
        raise ValueError('Unsafe destination')
    def digest(p):
        with p.open('rb') as f:
            h=hashlib.sha256()
            for chunk in iter(lambda:f.read(1024*1024),b''): h.update(chunk)
            return h.hexdigest()
    sha=digest(source)
    status='READY'
    if dest.exists():
        if not dest.is_file() or digest(dest)!=sha: raise FileExistsError(f'Will not overwrite {dest}')
        status='ALREADY_STAGED'
    return source,dest,sha,status


def stage(project,source,asset_id,apply=False):
    source,dest,sha,status=plan(project,source,asset_id)
    if apply and status=='READY':
        dest.parent.mkdir(parents=True,exist_ok=True)
        # Exclusive creation protects existing models; clean up only our partial file.
        with dest.open('xb') as out:
            try:
                with source.open('rb') as inp: shutil.copyfileobj(inp,out)
            except BaseException:
                out.close();dest.unlink();raise
        if plan(project,source,asset_id)[3]!='ALREADY_STAGED':
            raise ValueError('Post-copy verification failed')
        status='STAGED'
    return {'status':status,'source':str(source),'destination':str(dest),'sha256':sha,'apply':apply}


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--project',required=True);p.add_argument('--source',required=True);p.add_argument('--asset-id',required=True);p.add_argument('--apply',action='store_true')
    a=p.parse_args();print(json.dumps(stage(a.project,a.source,a.asset_id,a.apply),ensure_ascii=False,indent=2))
