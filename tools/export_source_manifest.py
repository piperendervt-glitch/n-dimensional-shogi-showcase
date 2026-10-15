from pathlib import Path
import hashlib, json

ROOT=Path(__file__).resolve().parents[1]
GENERATED_STAMP_PATHS={
    'Unity/Assets/Resources/showcase-build.json',
    'Unity/Assets/Resources/showcase-build.json.meta',
}
sources=[]
for folder in ('src','tests','tools','scenarios','Unity/Assets','Unity/Packages','Unity/ProjectSettings'):
    for f in sorted((ROOT/folder).rglob('*')):
        if not f.is_file() or any(p in ('bin','obj','__pycache__') for p in f.parts): continue
        if f.relative_to(ROOT).as_posix() in GENERATED_STAMP_PATHS: continue
        if f.suffix in ('.cs','.csproj','.json','.shader','.hlsl','.compute','.cginc','.asmdef','.py','.ps1','.asset','.mat','.unity','.txt','.png','.meta','.props','.targets'):
            sources.append({'path':f.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'bytes':f.stat().st_size})
for f in sorted(ROOT.glob('Directory.Build.*')):
    if f.is_file(): sources.append({'path':f.name,'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'bytes':f.stat().st_size})
payload=json.dumps(sources,ensure_ascii=False,sort_keys=True).encode('utf-8')
report={'schema':'showcase-source-manifest/1','sourceHash':hashlib.sha256(payload).hexdigest(),'gitCommit':None,'reason':'Local commits were not authorized','excludedGeneratedFiles':sorted(GENERATED_STAMP_PATHS),'files':sources}
(ROOT/'evidence/environment').mkdir(parents=True,exist_ok=True)
manifest_bytes=json.dumps(report,ensure_ascii=False,indent=2).encode('utf-8')
manifest_sha=hashlib.sha256(manifest_bytes).hexdigest()
(ROOT/'evidence/environment/source-manifest.json').write_bytes(manifest_bytes)
archive=ROOT/'evidence/environment/source-manifests'
archive.mkdir(parents=True,exist_ok=True)
archive_path=archive/(manifest_sha+'.json')
if archive_path.exists() and archive_path.read_bytes()!=manifest_bytes:
    raise RuntimeError('Existing immutable manifest archive differs from its SHA-256 filename')
if not archive_path.exists(): archive_path.write_bytes(manifest_bytes)
print(report['sourceHash'],len(sources),'files; manifest SHA-256',manifest_sha)
