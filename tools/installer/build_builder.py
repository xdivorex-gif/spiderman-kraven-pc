"""Build KravenModBuilder.exe: the Kraven mod installer that carries no game
content. It builds the mod on the user's PC from their own Xbox disc image
and their own Windows copy of the game.

    py -3 tools/installer/build_builder.py [python embeddable zip]

Embeds, as resources:
  builder/python.zip    the official Python embeddable package for 64-bit
                        Windows (python.org; its licence allows redistribution),
                        default tools_ext/python_embed/python-3.12.7-embed-amd64.zip
  builder/tools/*.py    make_mod.py and the tools it imports

Output: dist/KravenModBuilder.exe. It holds no game files, so it can be shared.
"""
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CSC = Path('C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe')
NET = CSC.parent
TOOLS = ['make_mod.py', 'xiso.py', 'pc_zoo.py', 'pc_zoo_audio.py', 'pc_menus.py', 'pcmesh.py',
         'dds2tga.py', 'sx.py', 'sxdis.py', 'xbs.py', 'adpcm.py', 'pc_pkg.py']
REFS = ['System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll']


def main():
    py = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'tools_ext/python_embed/python-3.12.7-embed-amd64.zip'
    if not py.is_file():
        raise SystemExit('missing ' + str(py))
    out_dir = ROOT / 'dist'
    out_dir.mkdir(exist_ok=True)
    out = out_dir / 'KravenModBuilder.exe'
    # a window (GUI), run as the user: an elevated window takes no drag-and-drop
    # from Explorer; it reopens itself elevated when the game folder needs it
    lines = ['/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/define:GUI',
             '/win32manifest:"%s"' % (HERE / 'app_user.manifest'), '/out:"%s"' % out]
    lines += ['/r:"%s"' % (NET / r) for r in REFS]
    lines.append('/resource:"%s",builder/python.zip' % py)
    lines += ['/resource:"%s",builder/tools/%s' % (HERE.parent / t, t) for t in TOOLS]
    lines.append('"%s"' % (HERE / 'KravenModInstaller.cs'))
    rsp = out_dir / 'builder.rsp'
    rsp.write_text('\n'.join(lines), encoding='utf-8')
    r = subprocess.run([str(CSC), '@' + str(rsp)], capture_output=True, text=True)
    print(r.stdout, r.stderr)
    if r.returncode:
        raise SystemExit('csc failed')
    print('%s: %.1f MB (Python + %d tools, no game content)' % (out, out.stat().st_size / 1e6, len(TOOLS)))


if __name__ == '__main__':
    main()
