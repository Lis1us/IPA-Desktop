from pathlib import Path
import json, os, re, shutil, subprocess, zipfile
root=Path.cwd(); prefix=root/'device-build/prefix'; out=root/'device-build/package'
bins=out/'windows_amd64'; bins.mkdir(parents=True,exist_ok=True)
mingw=Path('/mingw64/bin'); system=Path('/c/Windows/System32')
queue=[prefix/'bin'/n for n in ('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe')]
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
for project in (root/'device-build/source').iterdir():
    target=licenses/project.name; target.mkdir()
    for name in ('COPYING','COPYING.LESSER','COPYING.GPLv3','LICENSE','LICENSE.md','LICENSE.txt','COPYING.LGPLv2.1'):
        if (project/name).is_file(): shutil.copy2(project/name,target/name)
    if not list(target.iterdir()): raise RuntimeError(f'Missing license for {project.name}')
shutil.copytree('/mingw64/share/licenses',licenses/'MSYS2',dirs_exist_ok=True)
shutil.copy2('Tools/DeviceTools/pins.json',out/'sources.json')
shutil.copy2('Tools/DeviceTools/README.md',out/'README.md')
shutil.copy2('device-build/msys2-packages.txt',out/'msys2-packages.txt')
version=Path('Tools/DeviceTools/VERSION').read_text().strip()
(out/'version.txt').write_text(version+'\n')
manifest={p.relative_to(out).as_posix():__import__('hashlib').sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file()}
(out/'sha256.json').write_text(json.dumps(manifest,indent=2)+'\n')
for path,folder in [(root/f'IPA-DeviceTools-{version}-Windows-x64.zip',out),(root/f'IPA-DeviceTools-{version}-Sources.zip',root/'device-build/source-archives')]:
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(folder.rglob('*')):
            if file.is_file(): archive.write(file,file.relative_to(folder).as_posix())
print(f'Packaged {len(seen)} Windows binaries and license notices')
