from pathlib import Path
import json, os, re, shutil, subprocess, zipfile
root=Path.cwd(); prefix=root/'firmware-build/prefix'; out=root/'firmware-build/package'
bins=out/'windows_amd64'; bins.mkdir(parents=True,exist_ok=True)
mingw=Path(subprocess.check_output(['cygpath','-w','/mingw64'],text=True).strip())/'bin'
system=Path(os.environ['SYSTEMROOT'])/'System32'
queue=[prefix/'bin'/n for n in ('idevice_id.exe','ideviceinfo.exe','idevicerestore.exe','irecovery.exe')]
seen=set()
while queue:
    file=queue.pop(0)
    if file.name.lower() in seen: continue
    if not file.is_file(): raise RuntimeError(f'Missing binary: {file}')
    seen.add(file.name.lower()); shutil.copy2(file,bins/file.name)
    data=subprocess.check_output(['objdump','-p',str(file)],text=True)
    for dll in re.findall(r'DLL Name:\s*(\S+)',data):
        if dll.lower() in seen: continue
        found=next((p for folder in (prefix/'bin',mingw) for p in folder.iterdir() if p.name.lower()==dll.lower()),None)
        if found: queue.append(found)
        elif dll.lower().startswith(('api-ms-win-','ext-ms-win-')) or any(p.name.lower()==dll.lower() for p in system.iterdir()): pass
        else: raise RuntimeError(f'Unresolved dependency {dll} of {file.name}')
licenses=out/'LICENSES'; licenses.mkdir()
for project in (root/'firmware-build/source').iterdir():
    target=licenses/project.name; target.mkdir()
    for name in ('COPYING','COPYING.LESSER','COPYING.GPLv3','LICENSE','LICENSE.md','LICENSE.txt','COPYING.LGPLv2.1'):
        if (project/name).is_file(): shutil.copy2(project/name,target/name)
    if not list(target.iterdir()): raise RuntimeError(f'Missing license for {project.name}')
shutil.copytree(mingw.parent/'share/licenses',licenses/'MSYS2',dirs_exist_ok=True)
shutil.copy2('Tools/FirmwareTools/pins.json',out/'sources.json')
shutil.copy2('Tools/FirmwareTools/patch_restore.py',out/'update-only-patch.py')
shutil.copy2('Tools/FirmwareTools/README.md',out/'README.md')
shutil.copy2('firmware-build/msys2-packages.txt',out/'msys2-packages.txt')
version=Path('Tools/FirmwareTools/VERSION').read_text().strip()
(out/'version.txt').write_text(version+'\n')
manifest={p.relative_to(out).as_posix():__import__('hashlib').sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file()}
(out/'sha256.json').write_text(json.dumps(manifest,indent=2)+'\n')
for path,folder in [(root/f'IPA-FirmwareTools-{version}-Windows-x64.zip',out),(root/f'IPA-FirmwareTools-{version}-Sources.zip',root/'firmware-build/source-archives')]:
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(folder.rglob('*')):
            if file.is_file(): archive.write(file,file.relative_to(folder).as_posix())
print(f'Packaged {len(seen)} Windows binaries and license notices')
