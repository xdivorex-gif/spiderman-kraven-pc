"""Build KravenModInstaller.exe: one file, the mod files embedded, for
every game build in VARIANTS.

    py -3 tools/installer/build_installer.py

First run pc_zoo.py once per variant, with that build's data folder and
build dir. The level packages differ per game build, because they carry
that build's own menu code (pc_menus.py takes it from its OSCORP_B).
Everything else (movies, audio, tips) is shared and is taken from the first
variant's build dir. The payload is game-derived: the .exe goes into
build_pc_zoo/ and must never be committed or published.

The installer picks the variant by the MD5 of the target's
M6OSCORP\\OSCORP_B.SX, else by the address of its physics code, else uses
the first one (with a warning).

Compiled with the C# compiler that ships with Windows' .NET Framework 4
(no download). Each variant's exe must contain the installer's byte
signature for the physics fix exactly once (pc_nan_guard.py).
"""
import hashlib
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CSC = Path('C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe')
sys.path.insert(0, str(HERE.parent))
import pc_pkg  # noqa: E402

RETAIL = Path('C:/Program Files (x86)/Activision/Spider-Man (tm) Movie')
MAGI = Path('C:/MagiPacks/Spider-Man - The Movie')
# name, pc_zoo build dir, that build's game folder, an unpatched copy of its exe
VARIANTS = [
    ('retail', ROOT / 'build_pc_zoo', RETAIL, RETAIL / 'SpiderMan.exe.before-nan-guard.bak'),
    ('magipacks', ROOT / 'build_magipacks', MAGI, ROOT / 'build_magipacks/exe_original/SpiderMan.exe'),
]


def shared(build):
    """(source file, path inside the game folder) for the files every build gets"""
    out = [(build / 'animatic/av_zoo.bik', 'animatic/av_zoo.bik'),
           (build / 'movies/kraven.bik', 'movies/kraven.bik')]
    out += [(f, 'data/tips/' + f.name) for f in sorted((build / 'tips').iterdir()) if f.is_file()]
    out += [(f, 'data/' + f.relative_to(build / 'data').as_posix()) for f in sorted((build / 'data').rglob('*')) if f.is_file()]
    return out


def signature(src):
    hexs = re.search(r'Sig = Hex\(\s*((?:"[^"]*"\s*\+?\s*)+)\)', src).group(1)
    sig = ''.join(re.findall(r'"([^"]*)"', hexs)).split()
    return re.compile(b''.join(b'.' if t == '??' else re.escape(bytes([int(t, 16)])) for t in sig), re.S), len(sig)


def hook_va(pat, exe):
    """VA of the one signature match, mapped through the exe's first
    executable section as the installer does."""
    import struct
    d = exe.read_bytes()
    pe = struct.unpack_from('<I', d, 0x3C)[0]
    base = struct.unpack_from('<I', d, pe + 0x34)[0]
    nsec, opt = struct.unpack_from('<H', d, pe + 6)[0], struct.unpack_from('<H', d, pe + 20)[0]
    for i in range(nsec):
        s = pe + 24 + opt + 40 * i
        va, raw_size, raw_off, chars = (struct.unpack_from('<I', d, s + 12)[0], struct.unpack_from('<I', d, s + 16)[0],
                                        struct.unpack_from('<I', d, s + 20)[0], struct.unpack_from('<I', d, s + 36)[0])
        if chars & 0x20000000:
            hits = [m.start() for m in pat.finditer(d, raw_off, raw_off + raw_size)]
            if len(hits) != 1:
                raise SystemExit('%s: physics signature found %d times' % (exe, len(hits)))
            return base + va + hits[0] - raw_off
    raise SystemExit('%s: no executable section' % exe)


def menu_source_md5(game):
    p = pc_pkg.Pkg(game / 'data/levels/OSCORP_B.pkg')
    sx = [d for n, d in p.items() if n.upper() == 'M6OSCORP\\OSCORP_B.SX']
    if len(sx) != 1:
        raise SystemExit('%s: no M6OSCORP\\OSCORP_B.SX' % game)
    return hashlib.md5(sx[0]).hexdigest()


def main():
    src = (HERE / 'KravenModInstaller.cs').read_text(encoding='utf-8')
    pat, n = signature(src)
    retail_hook = hook_va(pat, VARIANTS[0][3])
    if retail_hook != 0x4C54D9 or retail_hook + n != 0x4C5548:
        raise SystemExit('retail signature check failed: %#x, %d bytes' % (retail_hook, n))
    files = [(f, 'payload/' + rel) for f, rel in shared(VARIANTS[0][1])]
    rows = []
    for name, build, game, exe in VARIANTS:
        md5, hook = menu_source_md5(game), hook_va(pat, exe)
        rows.append('%s\t%s\t%X' % (name, md5, hook))
        files += [(build / lv, 'variant/%s/data/levels/%s' % (name, lv)) for lv in ('ZOO_B.pkg', 'ZOO_C.pkg')]
        print('variant %-10s OSCORP_B.SX %s  physics hook %#x' % (name, md5, hook))
    for f, _ in files:
        if not f.is_file():
            raise SystemExit('missing ' + str(f))
    out_dir = VARIANTS[0][1]
    table = out_dir / 'variants.txt'
    table.write_text('\n'.join(rows) + '\n', encoding='ascii')
    out = out_dir / 'KravenModInstaller.exe'
    rsp = out_dir / 'installer.rsp'
    lines = ['/nologo', '/target:exe', '/platform:anycpu', '/optimize+',
             '/win32manifest:"%s"' % (HERE / 'app.manifest'), '/out:"%s"' % out,
             '/r:"%s"' % (CSC.parent / 'System.IO.Compression.dll'),
             '/r:"%s"' % (CSC.parent / 'System.IO.Compression.FileSystem.dll'),
             '/resource:"%s",variants.txt' % table]
    lines += ['/resource:"%s",%s' % (f, name) for f, name in files]
    lines.append('"%s"' % (HERE / 'KravenModInstaller.cs'))
    rsp.write_text('\n'.join(lines), encoding='utf-8')
    r = subprocess.run([str(CSC), '@' + str(rsp)], capture_output=True, text=True)
    print(r.stdout, r.stderr)
    if r.returncode:
        raise SystemExit('csc failed')
    print('%s: %d files embedded, %.1f MB' % (out, len(files), out.stat().st_size / 1e6))


if __name__ == '__main__':
    main()
