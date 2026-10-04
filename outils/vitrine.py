"""Images de presentation du README, tirees des atlas : les tenues cote a cote et un gif ou il danse
en changeant de tenue.

Usage : python outils/vitrine.py      (apres construire_atlas.py)
Sorties : apercus/tenues.png, apercus/danse.gif
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

RACINE = Path(__file__).resolve().parent.parent
PERSO = RACINE / "personnages" / "jungkook"
SORTIE = RACINE / "apercus"
CASE_L, CASE_H = 192, 208
HAUT, BAS = (34, 22, 62), (118, 70, 170)        # degrade violet


def tenues():
    """(nom, atlas) de chaque tenue, dans l'ordre de la fiche."""
    fiche = dict(l.split("=", 1) for l in (PERSO / "perso.txt").read_text(encoding="utf-8").splitlines() if "=" in l)
    noms = fiche.get("tenues", "Classique").split("|")
    liste = []
    for i, nom in enumerate(noms):
        chemin = PERSO / ("atlas.png" if i == 0 else f"tenue{i}.png")
        if chemin.exists():
            liste.append((nom, Image.open(chemin).convert("RGBA")))
    return liste


def case(atlas, ligne, colonne):
    return atlas.crop((colonne * CASE_L, ligne * CASE_H, (colonne + 1) * CASE_L, (ligne + 1) * CASE_H))


def degrade(l, h):
    fond = Image.new("RGBA", (l, h))
    trait = ImageDraw.Draw(fond)
    for y in range(h):
        k = y / max(1, h - 1)
        trait.line([(0, y), (l, y)], fill=tuple(round(a + (b - a) * k) for a, b in zip(HAUT, BAS)) + (255,))
    return fond


def police(taille, gras=True):
    for nom in ("seguisb.ttf" if gras else "segoeui.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(nom, taille)
        except OSError:
            pass
    return ImageFont.load_default()


def bande_tenues(liste, zoom=2):
    largeur_col = 250
    toile = degrade(largeur_col * len(liste), CASE_H * zoom + 36)
    trait = ImageDraw.Draw(toile)
    f = police(26)
    for i, (nom, atlas) in enumerate(liste):
        s = case(atlas, 0, 0).resize((CASE_L * zoom, CASE_H * zoom), Image.Resampling.NEAREST)
        toile.alpha_composite(s, (i * largeur_col + (largeur_col - s.width) // 2, -20))
        l = trait.textlength(nom, font=f)
        trait.text((i * largeur_col + (largeur_col - l) / 2, CASE_H * zoom - 12), nom, font=f, fill=(255, 255, 255))
    return toile


def gif_danse(liste, zoom=2):
    vues = []
    for nom, atlas in liste:
        for colonne in range(8):
            v = degrade(CASE_L * zoom + 40, CASE_H * zoom + 10)
            v.alpha_composite(case(atlas, 12, colonne).resize((CASE_L * zoom, CASE_H * zoom), Image.Resampling.NEAREST), (20, 0))
            vues.append(v.convert("P", palette=Image.Palette.ADAPTIVE))
    vues[0].save(SORTIE / "danse.gif", save_all=True, append_images=vues[1:], duration=240, loop=0)


if __name__ == "__main__":
    SORTIE.mkdir(exist_ok=True)
    liste = tenues()
    bande_tenues(liste).convert("RGB").save(SORTIE / "tenues.png", optimize=True)
    gif_danse(liste)
    print(f"{len(liste)} tenues -> apercus/tenues.png, apercus/danse.gif")
