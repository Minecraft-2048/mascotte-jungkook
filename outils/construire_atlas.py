"""Construit l'atlas d'animation de chaque mascotte a partir de sa planche de poses generee par l'IA.

Entree  : une planche de 12 poses sur fond vert, grille 4 x 3
          (sources/planche-1.png pour Claude, personnages/<nom>/planche.png pour les autres)
          et, pour une star, une seconde planche planche2.png : bisous, coeurs, danse et chant
Sorties : l'atlas (8 colonnes x 10 lignes, 14 avec la seconde planche, cases de 192x208), l'icone,
          deux apercus de controle (assets/ et sources/ pour Claude, personnages/<nom>/ pour les autres)
          et, avec la seconde planche, les petits dessins qui s'envolent (particules.png)

Usage : python outils/construire_atlas.py [nom ...]      (sans nom : toutes les mascottes)
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

RACINE = Path(__file__).resolve().parent.parent

CASE_L, CASE_H = 192, 208      # case de l'atlas (contrat des pets Codex)
SOL = 200                      # ligne du sol (bas des pieds) dans la case
HAUTEUR = 132                  # hauteur de la pose de repos dans la case

POSES = ["repos", "clin", "salut_a", "salut_b", "marche_a", "marche_b",
         "saut", "accroupi", "rate", "attente", "travail", "revue"]

# seconde planche (facultative) : la premiere pose, debout comme au repos, sert de repere de taille
POSES2 = ["repere", "bisou_a", "bisou_b", "coeur_a", "coeur_b",
          "danse_a", "danse_b", "danse_c", "danse_d", "danse_e", "chant_a", "chant_b"]

# (nom, durees en ms) : les 9 premieres lignes ont les memes rythmes que les pets Codex,
# la dixieme donne la pose en l'air, tournee a droite puis a gauche
ETATS = [
    ("idle", [280, 110, 110, 140, 140, 320]),
    ("running-right", [120] * 7 + [220]),
    ("running-left", [120] * 7 + [220]),
    ("waving", [140, 140, 140, 280]),
    ("jumping", [140, 140, 140, 140, 280]),
    ("failed", [140] * 7 + [240]),
    ("waiting", [150] * 5 + [260]),
    ("running", [120] * 5 + [220]),
    ("review", [150] * 5 + [280]),
    ("air", [400, 400]),
    # lignes 10 a 13, avec la seconde planche
    ("kiss", [240, 260, 380, 650]),
    ("heart", [300, 300, 330, 520]),
    ("dance", [250] * 8),
    ("sing", [250] * 8),
]


# poses a reprendre dans planche-retouche.png (le chat saluait avec une patte en trop)
RETOUCHES = {"chat": ("salut_a", "salut_b")}
RETOUCHES2 = ("danse_d",)


def personnages():
    liste = {"claude": dict(planche=RACINE / "sources" / "planche-1.png", atlas=RACINE / "assets" / "claude-atlas.png",
                            icone=RACINE / "assets" / "icone.ico", apercus=RACINE / "sources", clin=clin_visiere)}
    for dossier in sorted((RACINE / "personnages").iterdir()):
        sorties = dict(atlas=dossier / "atlas.png", icone=dossier / "icone.ico", apercus=dossier)
        if dossier.name != "claude" and (dossier / "planche.png").exists():
            liste[dossier.name] = dict(planche=dossier / "planche.png", clin=clin_difference, **sorties)
        elif (dossier / "planche-nes.png").exists():
            liste[dossier.name] = dict(planche=dossier / "planche-nes.png", formes=NES_FORMES, **sorties)
    return liste


# ------------------------------------------------- sprites 8 bits tout faits
# Planche de Super Mario Bros (NES) : la moitie droite regarde a droite. Les sprites sont agrandis
# sans lissage pour garder leurs gros pixels nets : 6 pixels de case par pixel d'origine, ce qui
# laisse le grand Mario (32 pixels de haut) tenir dans une case.
NES_GROS = 16          # agrandissement des sprites avant mise en case
NES_ECHELLE = 0.375    # puis reduction exacte : 16 x 0.375 = 6
NES_TUILE = 96         # un bloc de 16 pixels a la meme echelle

# Une forme = une bande de la planche (y) et, pour chaque pose, (colonne, rangee) parmi ses sprites.
# Suffixe du fichier d'atlas : "" petit Mario, "2" grand Mario, "3" Mario de feu.
NES_FORMES = {
    "": dict(y=(0, 48), poses={"repos": (0, 0), "marche_a": (1, 0), "marche_c": (2, 0), "marche_b": (3, 0),
                               "attente": (4, 0), "saut": (5, 0), "rate": (6, 0), "travail": (4, 1), "travail_b": (5, 1)}),
    "2": dict(y=(52, 85), poses={"repos": (0, 0), "marche_a": (1, 0), "marche_c": (2, 0), "marche_b": (3, 0),
                                 "attente": (4, 0), "saut": (5, 0), "accroupi": (6, 0)}),
    "3": dict(y=(122, 155), poses={"repos": (0, 0), "marche_a": (1, 0), "marche_c": (2, 0), "marche_b": (3, 0),
                                   "attente": (5, 0), "saut": (6, 0), "accroupi": (7, 0)}),
}


def sprites_nes(chemin, forme):
    a = np.asarray(Image.open(chemin).convert("RGBA"))
    y0, y1 = forme["y"]
    a = a[y0:y1, a.shape[1] // 2:]
    plein = a[..., 3] > 0
    colonnes = bandes(plein.any(axis=0), mini=3)
    sprites = {}
    for nom, (c, r) in forme["poses"].items():
        x0, x1 = colonnes[c]
        rangees = bandes(plein[:, x0:x1].any(axis=1), mini=3)
        ya, yb = rangees[min(r, len(rangees) - 1)]
        sprites[nom] = np.repeat(np.repeat(a[ya:yb, x0:x1], NES_GROS, axis=0), NES_GROS, axis=1)
    # ces sprites n'ont ni clin d'oeil ni salut : on fait avec les poses qui existent
    for nom, source in (("clin", "repos"), ("accroupi", "repos"), ("revue", "repos"), ("salut_a", "saut"),
                        ("salut_b", "repos"), ("rate", "accroupi"), ("travail", "repos")):
        sprites.setdefault(nom, sprites[source])
    return sprites


def objets_nes(perso):
    """Bande d'objets pour l'animation du bloc : bloc ?, bloc vide, champignon, piece, fleur de feu."""
    source = perso["planche"].with_name("objets-nes.png")
    if not source.exists():
        return
    a = Image.open(source).convert("RGBA")

    def tuile(x, y, h=16):
        t = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        t.alpha_composite(a.crop((x, y, x + 16, y + h)), (0, 16 - h))
        return t

    vide = Image.new("RGBA", (16, 16), (200, 76, 12, 255))          # le bloc deja frappe n'est pas sur la planche
    px = vide.load()
    for i in range(16):
        px[i, 0] = px[0, i] = (252, 188, 176, 255)
        px[i, 15] = px[15, i] = (0, 0, 0, 255)
    for x, y in ((2, 2), (13, 2), (2, 13), (13, 13)):
        px[x, y] = (0, 0, 0, 255)

    tuiles = [tuile(4, 4), vide, tuile(184, 34), tuile(124, 94, 15), tuile(4, 64)]
    bande = Image.new("RGBA", (NES_TUILE * len(tuiles), NES_TUILE), (0, 0, 0, 0))
    for i, t in enumerate(tuiles):
        bande.paste(t.resize((NES_TUILE, NES_TUILE), Image.Resampling.NEAREST), (i * NES_TUILE, 0))
    bande.save(perso["atlas"].with_name("objets.png"))
    print("  objets ecrits")


# ---------------------------------------------------------------- decoupage
def masque_fond(a):
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (g - np.maximum(r, b)) > 60


def bandes(profil, mini=12):
    """Intervalles [debut, fin) ou le profil booleen est vrai, en ignorant les miettes."""
    res, debut = [], None
    for i, v in enumerate(list(profil) + [False]):
        if v and debut is None:
            debut = i
        elif not v and debut is not None:
            if i - debut >= mini:
                res.append((debut, i))
            debut = None
    return res


def decouper(a):
    plein = ~masque_fond(a)
    boites = []
    for y0, y1 in bandes(plein.any(axis=1)):
        for x0, x1 in bandes(plein[y0:y1].any(axis=0)):
            ys = np.where(plein[y0:y1, x0:x1].any(axis=1))[0]
            boites.append((x0, y0 + ys[0], x1, y0 + ys[-1] + 1))
    # un filigrane ou une miette dans un coin n'est pas une pose : on ecarte ce qui est bien plus petit que les autres
    aires = [(x1 - x0) * (y1 - y0) for x0, y0, x1, y1 in boites]
    return [b for b, aire in zip(boites, aires) if aire > 0.15 * np.median(aires)]


def detourer(a, boite):
    """Sprite RGBA a la resolution d'origine, fond vert retire et reflets verts neutralises."""
    x0, y0, x1, y1 = boite
    crop = a[y0:y1, x0:x1].copy()
    fond = masque_fond(crop)
    plafond = np.maximum(crop[..., 0], crop[..., 2])
    crop[..., 1] = np.minimum(crop[..., 1], plafond)      # aucun personnage n'a de vert dominant
    rgba = np.dstack([crop, np.where(fond, 0, 255).astype(np.uint8)])
    rgba[fond, :3] = 0
    return rgba


# ------------------------------------------------------------------ clin d'oeil
# L'IA redessine tout le corps dans la pose "yeux fermes". Pour que le corps ne tremble pas a chaque
# clin d'oeil, on garde celui de la pose de repos et on n'y reporte que les yeux.
def filtrer(masque, filtre, taille):
    im = Image.fromarray((masque * 255).astype(np.uint8)).filter(filtre(taille))
    return np.asarray(im) > 127


def dilater(masque, rayon):
    return filtrer(masque, ImageFilter.MaxFilter, 2 * rayon + 1)


def boite_visiere(s):
    """Rectangle englobant de la visiere : la tache sombre qui contient le milieu du visage.
    (Le contour du corps est sombre lui aussi, d'ou le remplissage de proche en proche.)"""
    r, g, b = (s[..., i].astype(int) for i in range(3))
    sombre = (s[..., 3] > 0) & (r < 85) & (g < 70) & (b < 70)
    h, w = sombre.shape
    y, x = int(h * 0.42), w // 2
    while not sombre[y, x]:                    # le milieu peut tomber sur un oeil
        x -= 1
    vu = np.zeros_like(sombre)
    pile = [(y, x)]
    vu[y, x] = True
    while pile:
        y, x = pile.pop()
        for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= yy < h and 0 <= xx < w and sombre[yy, xx] and not vu[yy, xx]:
                vu[yy, xx] = True
                pile.append((yy, xx))
    ys, xs = np.where(vu)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1, np.median(s[vu][:, :3], axis=0)


def masque_yeux(s, boite):
    x0, y0, x1, y1 = boite[:4]
    r, g, b = (s[..., i].astype(int) for i in range(3))
    clair = (r > 170) & (g > 150) & (b > 110)
    dedans = np.zeros(clair.shape, bool)
    dedans[y0:y1, x0:x1] = True
    return clair & dedans


def clin_visiere(repos, clin):
    """Claude : les yeux sont des traits clairs sur une visiere sombre, on les efface et on les redessine."""
    br, bc = boite_visiere(repos), boite_visiere(clin)
    res = repos.copy()
    res[dilater(masque_yeux(repos, br), 4), :3] = br[4]
    dx = (br[0] + br[2]) // 2 - (bc[0] + bc[2]) // 2
    dy = (br[1] + br[3]) // 2 - (bc[1] + bc[3]) // 2
    ys, xs = np.where(dilater(masque_yeux(clin, bc), 2))
    res[ys + dy, xs + dx] = clin[ys, xs]
    return res


def clin_difference(repos, clin):
    """Les autres : on cale les deux poses l'une sur l'autre et on ne reporte que la zone du visage qui differe."""
    h, w = repos.shape[:2]

    def cale(dx, dy):
        toile = np.zeros_like(repos)
        ch, cw = clin.shape[:2]
        xa, ya, xb, yb = max(dx, 0), max(dy, 0), min(dx + cw, w), min(dy + ch, h)
        toile[ya:yb, xa:xb] = clin[ya - dy:yb - dy, xa - dx:xb - dx]
        return toile

    def ecart(toile):
        d = np.abs(repos[..., :3].astype(int) - toile[..., :3].astype(int)).max(axis=2)
        d[(repos[..., 3] > 0) != (toile[..., 3] > 0)] = 255
        return d

    # depart : pieds alignes ; puis le petit decalage qui fait le mieux coincider les deux dessins
    dx0, dy0 = int(round(centre_pieds(repos) - centre_pieds(clin))), h - clin.shape[0]
    _, dx, dy = min((ecart(cale(dx0 + i, dy0 + j)).mean(), dx0 + i, dy0 + j) for i in range(-6, 7) for j in range(-6, 7))
    toile = cale(dx, dy)

    zone = np.zeros((h, w), bool)
    zone[int(h * 0.08):int(h * 0.72), int(w * 0.12):int(w * 0.88)] = True
    change = filtrer((ecart(toile) > 70) & zone, ImageFilter.MinFilter, 3)      # sans les liseres d'un pixel
    change = dilater(change, 4) & (toile[..., 3] > 0)
    part = change.mean()
    print(f"  clin d'oeil : {part:.1%} du sprite repris de la pose aux yeux fermes")
    if part > 0.25:                                                             # deux dessins trop differents
        return toile
    res = repos.copy()
    res[change] = toile[change]
    return res


# ---------------------------------------------------- coeurs et notes qui s'envolent
# Une bande de tuiles de 16 pixels, dessinees ici pixel par pixel : coeur rose, coeur violet,
# coeur rouge, croche, double croche. La mascotte les agrandit sans lissage.
COEUR = ["................",
         "..oooo....oooo..",
         ".offffo..offffo.",
         "offhhffooffffffo",
         "offhffffffffffso",
         "offfffffffffffso",
         "offfffffffffffso",
         ".offfffffffffso.",
         "..offfffffffso..",
         "...offfffffso...",
         "....offfffso....",
         ".....offfso.....",
         "......offo......",
         ".......oo.......",
         "................",
         "................"]
NOTE = ["................",
        "......##........",
        "......###.......",
        "......####......",
        "......#.###.....",
        "......#..##.....",
        "......#...#.....",
        "......#...#.....",
        "......#..#......",
        "......#.........",
        "...####.........",
        "..#####.........",
        "..#####.........",
        "...###..........",
        "................",
        "................"]
DOUBLE_NOTE = ["................",
               ".....########...",
               ".....########...",
               ".....#......#...",
               ".....#......#...",
               ".....#......#...",
               ".....#......#...",
               ".....#......#...",
               ".....#......#...",
               "..####...####...",
               ".#####..#####...",
               ".#####..#####...",
               "..###....###....",
               "................",
               "................",
               "................"]


def particules(chemin):
    tuiles = []
    # coeur : o contour sombre, f remplissage, h reflet, s ombre
    for teinte, sombre in (((255, 95, 162), (110, 20, 64)), ((165, 100, 235), (60, 25, 105)), ((235, 45, 65), (100, 10, 22))):
        couleurs = {"o": (*sombre, 255), "f": (*teinte, 255), "h": (255, 240, 248, 255),
                    "s": (*(int(c * 0.82) for c in teinte), 255)}
        t = np.zeros((16, 16, 4), np.uint8)
        for y, ligne in enumerate(COEUR):
            for x, c in enumerate(ligne):
                if c in couleurs:
                    t[y, x] = couleurs[c]
        tuiles.append(t)
    # notes : violet fonce, avec un liseré clair pour se voir sur tous les fonds
    for dessin in (NOTE, DOUBLE_NOTE):
        forme = np.array([[c == "#" for c in ligne] for ligne in dessin])
        lisere = dilater(forme, 1) & ~forme
        t = np.zeros((16, 16, 4), np.uint8)
        t[lisere] = (250, 245, 255, 230)
        t[forme] = (70, 30, 120, 255)
        tuiles.append(t)
    Image.fromarray(np.concatenate(tuiles, axis=1), "RGBA").save(chemin)
    print(f"  particules ecrites : {chemin.name}")


# ------------------------------------------------------------ pose des images
def centre_pieds(s):
    bas = s[int(s.shape[0] * 0.9):, :, 3] > 0
    cols = np.where(bas.any(axis=0))[0]
    return (cols[0] + cols[-1] + 1) / 2


class Atelier:
    def __init__(self, sprites, echelle=None, k=None):
        self.sprites = sprites
        self.net = echelle is not None          # gros pixels nets : ni lissage ni ecrasement
        # on aligne les poses sur le milieu des pieds ; les sprites 8 bits, deja cadres, sur leur milieu
        self.cx = {n: s.shape[1] / 2 if self.net else centre_pieds(s) for n, s in sprites.items()}
        # la pose de repos donne l'echelle, sauf si une pose plus large ou plus haute ne tenait pas dans la case
        large = max(max(c, s.shape[1] - c) for s, c in ((s, self.cx[n]) for n, s in sprites.items()))
        haut = max(s.shape[0] for s in sprites.values())
        self.k = k or echelle or min(HAUTEUR / sprites["repos"].shape[0], (CASE_L / 2 - 4) / large, (SOL - 6) / haut)

    def image(self, nom, dx=0, dy=0, e=0, m=False):
        """Une case de l'atlas. e = ecrasement (respiration, appui), m = miroir."""
        s, cx = self.sprites[nom], self.cx[nom]
        if m:
            s, cx = s[:, ::-1], s.shape[1] - cx
        if self.net:
            e = 0
        kx, ky = self.k * (1 + 0.012 * e), self.k * (1 - 0.022 * e)
        l, h = max(1, round(s.shape[1] * kx)), max(1, round(s.shape[0] * ky))
        im = Image.fromarray(np.ascontiguousarray(s), "RGBA").convert("RGBa")
        im = im.resize((l, h), Image.Resampling.NEAREST if self.net else Image.Resampling.LANCZOS).convert("RGBA")
        x0, y0 = round(CASE_L / 2 - cx * kx) + dx, SOL - h + dy
        if x0 < 0 or y0 < 0 or x0 + l > CASE_L or y0 + h > CASE_H:
            print(f"  attention : {nom} deborde de la case ({x0},{y0} {l}x{h})")
        toile = Image.new("RGBA", (CASE_L * 3, CASE_H * 3), (0, 0, 0, 0))
        toile.alpha_composite(im, (x0 + CASE_L, y0 + CASE_H))
        return toile.crop((CASE_L, CASE_H, 2 * CASE_L, 2 * CASE_H))


def lire_planche(chemin, poses):
    a = np.asarray(Image.open(chemin).convert("RGB"))
    boites = decouper(a)
    print(f"  {chemin.name} : {len(boites)} poses trouvees")
    assert len(boites) == len(poses), f"{chemin.name} doit contenir {len(poses)} poses bien separees"
    return {n: detourer(a, b) for n, b in zip(poses, boites)}


def redimensionner(s, k):
    im = Image.fromarray(np.ascontiguousarray(s), "RGBA").convert("RGBa")
    im = im.resize((max(1, round(s.shape[1] * k)), max(1, round(s.shape[0] * k))), Image.Resampling.LANCZOS)
    return np.asarray(im.convert("RGBA"))


def construire(nom, perso):
    print(f"== {nom}")
    if "formes" in perso:
        for suffixe, forme in perso["formes"].items():
            sprites = sprites_nes(perso["planche"], forme)
            assembler(sprites, Atelier(sprites, NES_ECHELLE), perso, suffixe)
        objets_nes(perso)
        return
    sprites = lire_poses(nom, perso, perso["planche"])
    if perso["planche"].with_name("planche2.png").exists():
        particules(perso["atlas"].with_name("particules.png"))
    atelier = Atelier(sprites)
    assembler(sprites, atelier, perso, "")

    # tenues : personnages/<nom>/tenues/<n>-<tenue>/planche.png et planche2.png, memes poses aux memes places,
    # seuls les vetements changent. Chacune donne un atlas complet, tenue<n>.png, a la meme echelle.
    tenues = perso["planche"].parent / "tenues"
    if not tenues.is_dir():
        return
    for dossier in sorted(tenues.iterdir(), key=lambda d: int(d.name.split("-")[0])):
        if not (dossier / "planche.png").exists() or not (dossier / "planche2.png").exists():
            print(f"  tenue {dossier.name} : il manque une planche, ignoree")
            continue
        print(f"  -- tenue {dossier.name}")
        t = lire_poses(nom, perso, dossier / "planche.png")
        k = sprites["repos"].shape[0] / t["repos"].shape[0]
        if abs(k - 1) > 0.01:
            print(f"  remise a la taille de la tenue de base : x{k:.3f}")
            t = {n: redimensionner(s, k) for n, s in t.items()}
        sortie = dict(perso, atlas=perso["atlas"].with_name(f"tenue{dossier.name.split('-')[0]}.png"), apercus=dossier)
        assembler(t, Atelier(t, k=atelier.k), sortie, "", icone=False)


def lire_poses(nom, perso, planche):
    """Les 12 poses d'une planche (et les 11 de sa seconde planche s'il y en a une), detourees."""
    sprites = lire_planche(planche, POSES)
    # planche-retouche.png : la meme planche redessinee par l'IA pour corriger une erreur. On n'y reprend
    # que les poses listees dans RETOUCHES, les autres restent celles de la planche d'origine.
    retouche = planche.with_name("planche-retouche.png")
    if nom in RETOUCHES and retouche.exists():
        r = np.asarray(Image.open(retouche).convert("RGB"))
        boites_r = decouper(r)
        assert len(boites_r) == len(POSES), "la planche retouchee doit garder ses 12 poses"
        for pose in RETOUCHES[nom]:
            sprites[pose] = detourer(r, boites_r[POSES.index(pose)])
        print(f"  poses reprises de la retouche : {', '.join(RETOUCHES[nom])}")
    sprites["clin"] = perso["clin"](sprites["repos"], sprites["clin"])
    # seconde planche : l'IA ne l'a pas dessinee tout a fait a la meme taille, on la cale sur le repos
    seconde = planche.with_name("planche2.png")
    if seconde.exists():
        sprites2 = lire_planche(seconde, POSES2)
        # planche2-retouche.png : la seconde planche redessinee pour corriger un pas de danse (l'IA lui avait
        # donne le visage triste de la premiere planche) ; on n'en reprend que les poses de RETOUCHES2
        retouche2 = seconde.with_name('planche2-retouche.png')
        if retouche2.exists():
            corrigees = lire_planche(retouche2, POSES2)
            for pose in RETOUCHES2:
                sprites2[pose] = corrigees[pose]
            print(f"  poses reprises de la retouche : {', '.join(RETOUCHES2)}")
        # remplacer.txt : une pose ratee par l'IA et pas encore redessinee, prise ailleurs dans la planche
        # (lignes cible=source, avec "miroir" pour la retourner)
        remplacer = seconde.with_name("remplacer.txt")
        if remplacer.exists():
            for ligne in remplacer.read_text(encoding="utf-8").splitlines():
                if "=" in ligne and not ligne.startswith("#"):
                    cible, source = ligne.split("=", 1)
                    nom = source.split()[0]
                    sprites2[cible.strip()] = sprites2[nom][:, ::-1].copy() if "miroir" in source else sprites2[nom]
                    print(f"  {cible.strip()} remplacee par {source.strip()}")
        k = sprites["repos"].shape[0] / sprites2.pop("repere").shape[0]
        print(f"  seconde planche remise a l'echelle : x{k:.3f}")
        sprites.update({n: redimensionner(s, k) for n, s in sprites2.items()})
    return sprites


def assembler(sprites, atelier, perso, suffixe, icone=True):
    """Ecrit un atlas (et, pour la forme principale, l'icone et les apercus ; pas d'icone pour une tenue)."""
    principal = suffixe == ""
    chemin_atlas = perso["atlas"].with_name(perso["atlas"].stem + suffixe + ".png")
    f = atelier.image
    # variantes facultatives : une troisieme pose de marche, une seconde pose de travail
    if "marche_c" in sprites:
        pas = [("marche_a", 0), ("marche_c", 0), ("marche_b", 0), ("marche_c", 0)] * 2
    else:
        pas = [("marche_a", 0), ("marche_a", -3), ("marche_b", 0), ("marche_b", -3)] * 2
    travail = [f("travail"), f("travail_b")] * 3 if "travail_b" in sprites else [f("travail"), f("travail", e=1)] * 3
    saut_haut = max(0, min(46, SOL - 6 - round(sprites["saut"].shape[0] * atelier.k)))   # ce que la case laisse de marge

    if principal and icone:
        # icone de l'exe : la pose de repos, a sa resolution d'origine
        repos = Image.fromarray(sprites["repos"], "RGBA")
        cote = max(repos.size)
        icone = Image.new("RGBA", (cote, cote), (0, 0, 0, 0))
        icone.alpha_composite(repos, ((cote - repos.width) // 2, (cote - repos.height) // 2))
        perso["icone"].parent.mkdir(exist_ok=True)
        icone.resize((256, 256), Image.Resampling.LANCZOS).save(
            perso["icone"], sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    lignes = [
        # 0 idle : respiration, clin d'oeil
        [f("repos"), f("repos", e=1), f("repos", e=2), f("repos", e=1), f("clin"), f("repos")],
        # 1 running-right : marche vers la droite
        [f(n, dy=d) for n, d in pas],
        # 2 running-left : la meme marche en miroir
        [f(n, dy=d, m=True) for n, d in pas],
        # 3 waving
        [f("salut_a"), f("salut_b"), f("salut_a"), f("salut_b")],
        # 4 jumping : appel, montee, sommet, descente, reception
        [f("accroupi"), f("saut", dy=-saut_haut // 2), f("saut", dy=-saut_haut), f("saut", dy=-saut_haut // 2), f("accroupi")],
        # 5 failed : frisson puis affaissement
        [f("rate"), f("rate", dx=-3), f("rate", dx=3), f("rate", dx=-3), f("rate", dx=3),
         f("rate", e=1), f("rate", e=3), f("rate", e=3)],
        # 6 waiting : petits rebonds pour attirer l'attention
        [f("attente"), f("attente", dy=-3), f("attente", dy=-6), f("attente", dy=-3), f("attente"), f("attente", e=1)],
        # 7 running : au travail (ou au jeu)
        travail,
        # 8 review : se penche pour verifier (ou dort, en respirant)
        [f("revue"), f("revue", e=1), f("revue", e=2), f("revue", e=2), f("revue", e=1), f("revue")],
        # 9 air : en plein saut, vers la droite puis vers la gauche
        [f("saut"), f("saut", m=True)],
    ]
    if "bisou_a" in sprites:
        lignes += [
            # 10 kiss : la main aux levres, puis le bras tendu vers l'ecran
            [f("bisou_a"), f("bisou_a", e=1), f("bisou_b"), f("bisou_b", e=1)],
            # 11 heart : coeur avec les doigts, puis grand coeur avec les bras (rebonds)
            [f("coeur_a"), f("coeur_a", dy=-3), f("coeur_b"), f("coeur_b", dy=-3)],
            # 12 dance : quatre temps, deux images par temps (la paire tombe sur le temps)
            [f("danse_a"), f("danse_a", e=1), f("danse_b"), f("danse_b", m=True),
             f("danse_c"), f("danse_c", dy=-5), f("danse_d"), f("danse_e")],
            # 13 sing : au micro, en marquant le rythme
            [f("chant_a"), f("chant_a", e=1), f("chant_a"), f("chant_a", e=1),
             f("chant_b"), f("chant_b", dy=-3), f("chant_b"), f("chant_b", dy=-3)],
        ]

    atlas = Image.new("RGBA", (CASE_L * 8, CASE_H * len(lignes)), (0, 0, 0, 0))
    for l, images in enumerate(lignes):
        assert len(images) == len(ETATS[l][1]), ETATS[l][0]
        for c, case in enumerate(images):
            atlas.paste(case, (c * CASE_L, l * CASE_H))
    chemin_atlas.parent.mkdir(exist_ok=True)
    atlas.save(chemin_atlas, optimize=True)
    print(f"  atlas ecrit : {chemin_atlas} ({atlas.width}x{atlas.height})")

    # controle visuel : planche contact + gif de tous les etats
    teinte = (43, 45, 49, 255)
    fond = Image.new("RGBA", atlas.size, teinte)
    fond.alpha_composite(atlas)
    fond.resize((atlas.width // 2, atlas.height // 2), Image.Resampling.LANCZOS).save(perso["apercus"] / f"apercu-atlas{suffixe}.png")
    if not principal:
        return
    vues, durees = [], []
    for images, (_, ms) in [(i, e) for n, (i, e) in enumerate(zip(lignes, ETATS)) if n != 9]:
        for _ in range(2):
            for case, d in zip(images, ms):
                v = Image.new("RGBA", (CASE_L, CASE_H), teinte)
                v.alpha_composite(case)
                vues.append(v.convert("P", palette=Image.Palette.ADAPTIVE))
                durees.append(d)
    vues[0].save(perso["apercus"] / "apercu.gif", save_all=True, append_images=vues[1:], duration=durees, loop=0)


if __name__ == "__main__":
    tous = personnages()
    for nom in sys.argv[1:] or tous:
        construire(nom, tous[nom])
