"""Make the Kraven mod's files from the user's own game copies.

    py -3 tools/make_mod.py <Xbox disc image> <PC game folder> <stage dir>

Reads only what the mod needs from the Xbox image (tools/xiso.py), builds
the level packages against the PC copy's own data (pc_zoo.py: its
COMMON.pkg, its other levels, its OSCORP_B menu code, so any game build
gets matching levels) and writes the zoo sounds as .wav (pc_zoo_audio.py
without RAD Video Tools). Result, for the installer:

    <stage>/files/...       the files, at their paths in the game folder
    <stage>/unbink.txt      GLOBAL.SND paths that must lose " bink"

Run by KravenModBuilder.exe with the Python it carries; works standalone too.
"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pc_zoo  # noqa: E402
import pc_zoo_audio  # noqa: E402
import xiso  # noqa: E402

NEEDED = (['data/levels/ZOO_B.xbs', 'data/levels/ZOO_C.xbs',
           'data/Animatic/av_zoo.bik', 'data/movies/kraven.bik']
          + ['data/tips/zoo_%s.%s' % (l, e) for l in 'bc' for e in ('txt', 'fre', 'ger')]
          + ['data/xboxsound/ENGLISH/%s' % n for n in (
              'zoo_b.XSH', 'zoo_b.XSD', 'zoo_c.XSH', 'zoo_c.XSD', 'bronx_zoo_main.xss', 'bronx_zoo_edit2.xss',
              'zoo_b_in.xss', 'zoo_b_1.xss', 'zoo_b_2.xss', 'zoo_c_in.xss', 'zoo_c_out.xss')])


def step(msg):
    print('>> ' + msg, flush=True)


def main(iso, game, stage):
    pc_data = os.path.join(game, 'data')
    for p in (os.path.join(game, 'SpiderMan.exe'), os.path.join(pc_data, 'COMMON.pkg'),
              os.path.join(pc_data, 'levels', 'OSCORP_B.pkg')):
        if not os.path.isfile(p):
            raise SystemExit('not a Spider-Man (2002) Windows game folder: missing ' + p)
    xbox = os.path.join(stage, 'xbox')
    files = os.path.join(stage, 'files')
    build = os.path.join(stage, 'build')
    step('reading the Xbox disc image')
    img = xiso.Image(iso)
    for p in NEEDED:
        img.extract(p, os.path.join(xbox, p))
    xdata = os.path.join(xbox, 'data')
    step('building the two zoo levels against this PC copy (a few minutes)')
    pc_zoo.build(xdata, pc_data, build)
    os.makedirs(os.path.join(files, 'data', 'levels'), exist_ok=True)
    for lv in ('ZOO_B.pkg', 'ZOO_C.pkg'):
        shutil.copyfile(os.path.join(build, lv), os.path.join(files, 'data', 'levels', lv))
    step('copying movies and tips')
    for src, dst in (('Animatic/av_zoo.bik', 'animatic/av_zoo.bik'), ('movies/kraven.bik', 'movies/kraven.bik')):
        os.makedirs(os.path.dirname(os.path.join(files, dst)), exist_ok=True)
        shutil.copyfile(os.path.join(xdata, src), os.path.join(files, dst))
    os.makedirs(os.path.join(files, 'data', 'tips'), exist_ok=True)
    for l in 'bc':
        for e in ('txt', 'fre', 'ger'):
            shutil.copyfile(os.path.join(xdata, 'tips', 'zoo_%s.%s' % (l, e)),
                            os.path.join(files, 'data', 'tips', ('zoo_%s.%s' % (l, e)).upper()))
    step('converting Kraven\'s voice, the zoo music and the cut-scene dialogue to .wav')
    unbink = pc_zoo_audio.build(xdata, pc_data, os.path.join(files, 'data'))
    with open(os.path.join(stage, 'unbink.txt'), 'w', encoding='latin1') as f:
        f.write('\n'.join(unbink) + '\n')
    shutil.rmtree(xbox)
    step('done')


if __name__ == '__main__':
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    main(*sys.argv[1:])
