from pathlib import Path
import json, os, re, shutil, subprocess, zipfile
from package_support import DependencyCollector, sha256_file
root=Path.cwd(); prefix=root/'device-build/prefix'; out=root/'device-build/package'
bins=out/'windows_amd64'; bins.mkdir(parents=True,exist_ok=True)
mingw=Path(subprocess.check_output(['cygpath','-w','/mingw64'],text=True).strip())/'bin'
system=Path(os.environ['SYSTEMROOT'])/'System32'
def inspect_dependencies(file):
    data=subprocess.check_output(['objdump','-p',str(file)],text=True)
    return re.findall(r'DLL Name:\s*(\S+)',data)

collector=DependencyCollector(prefix/'bin',mingw,system,inspect_dependencies)
files=collector.collect(prefix/'bin'/name for name in ('idevice_id.exe','ideviceinfo.exe','ideviceinstaller.exe'))
for file in files:
    shutil.copy2(file,bins/file.name)
licenses=out/'LICENSES'; licenses.mkdir()
for project in (root/'device-build/source').iterdir():
    target=licenses/project.name; target.mkdir()
    for name in ('COPYING','COPYING.LESSER','COPYING.GPLv3','LICENSE','LICENSE.md','LICENSE.txt','COPYING.LGPLv2.1'):
        if (project/name).is_file(): shutil.copy2(project/name,target/name)
    if not list(target.iterdir()): raise RuntimeError(f'Missing license for {project.name}')
shutil.copytree(mingw.parent/'share/licenses',licenses/'MSYS2',dirs_exist_ok=True)
shutil.copy2('Tools/DeviceTools/pins.json',out/'sources.json')
shutil.copy2('Tools/DeviceTools/README.md',out/'README.md')
shutil.copy2('device-build/msys2-packages.txt',out/'msys2-packages.txt')
version=Path('Tools/DeviceTools/VERSION').read_text().strip()
(out/'version.txt').write_text(version+'\n')
manifest={p.relative_to(out).as_posix():sha256_file(p) for p in out.rglob('*') if p.is_file()}
(out/'sha256.json').write_text(json.dumps(manifest,indent=2)+'\n')
for path,folder in [(root/f'IPA-DeviceTools-{version}-Windows-x64.zip',out),(root/f'IPA-DeviceTools-{version}-Sources.zip',root/'device-build/source-archives')]:
    with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(folder.rglob('*')):
            if file.is_file(): archive.write(file,file.relative_to(folder).as_posix())
print(f'Packaged {len(files)} Windows binaries and license notices')
